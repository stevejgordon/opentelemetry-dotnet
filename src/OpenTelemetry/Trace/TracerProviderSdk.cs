// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#pragma warning disable OTEL1007 // IDeclarativeConfigProperties is experimental; used internally for env-var adapter

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Internal;
using OpenTelemetry.Resources;

namespace OpenTelemetry.Trace;

internal sealed class TracerProviderSdk : TracerProvider
{
    internal const string TracesSamplerConfigKey = "OTEL_TRACES_SAMPLER";
    internal const string TracesSamplerArgConfigKey = "OTEL_TRACES_SAMPLER_ARG";

    // OTEL_TRACES_SAMPLER environment variable values (spec-defined, case-insensitive).
    internal const string SamplerEnvVarAlwaysOn = "always_on";
    internal const string SamplerEnvVarAlwaysOff = "always_off";
    internal const string SamplerEnvVarTraceIdRatio = "traceidratio";
    internal const string SamplerEnvVarParentBasedAlwaysOn = "parentbased_always_on";
    internal const string SamplerEnvVarParentBasedAlwaysOff = "parentbased_always_off";
    internal const string SamplerEnvVarParentBasedTraceIdRatio = "parentbased_traceidratio";

    internal readonly IServiceProvider ServiceProvider;
    internal IDisposable? OwnedServiceProvider;
    internal int ShutdownCount;
    internal bool Disposed;

    private readonly ActivityListener listener;
    private readonly Action<Activity> getRequestedDataAction;
    private readonly bool supportLegacyActivity;

    internal TracerProviderSdk(
        IServiceProvider serviceProvider,
        bool ownsServiceProvider)
    {
        Debug.Assert(serviceProvider != null, "serviceProvider was null");

        var state = serviceProvider!.GetRequiredService<TracerProviderBuilderSdk>();
        state.RegisterProvider(this);

        this.ServiceProvider = serviceProvider!;

        if (ownsServiceProvider)
        {
            this.OwnedServiceProvider = serviceProvider as IDisposable;
            Debug.Assert(this.OwnedServiceProvider != null, "serviceProvider was not IDisposable");
        }

        OpenTelemetrySdkEventSource.Log.TracerProviderSdkEvent("Building TracerProvider.");

        var configureProviderBuilders = serviceProvider!.GetServices<IConfigureTracerProviderBuilder>();
        foreach (var configureProviderBuilder in configureProviderBuilders)
        {
            configureProviderBuilder.ConfigureBuilder(serviceProvider!, state);
        }

        var processorsAdded = new StringBuilder();
        var instrumentationFactoriesAdded = new StringBuilder();

        var resourceBuilder = state.ResourceBuilder ?? ResourceBuilder.CreateDefault();
        resourceBuilder.ServiceProvider = serviceProvider;
        this.Resource = resourceBuilder.Build();

        this.Sampler = GetSampler(
            serviceProvider!.GetRequiredService<IOptions<SamplerOptions>>().Value,
            state.Sampler,
            state.DeclarativeSampler,
            serviceProvider!.GetService<PluginComponentProviderRegistry>(),
            serviceProvider!);
        OpenTelemetrySdkEventSource.Log.TracerProviderSdkEvent($"Sampler added = \"{this.Sampler.GetType()}\".");

        this.supportLegacyActivity = state.LegacyActivityOperationNames.Count > 0;

        Regex? legacyActivityWildcardModeRegex = null;
        foreach (var legacyName in state.LegacyActivityOperationNames)
        {
            if (WildcardHelper.ContainsWildcard(legacyName))
            {
                legacyActivityWildcardModeRegex = WildcardHelper.GetWildcardRegex(state.LegacyActivityOperationNames);
                break;
            }
        }

        // Note: Linq OrderBy performs a stable sort, which is a requirement here
        IEnumerable<BaseProcessor<Activity>> processors = state.Processors.OrderBy(p => p.PipelineWeight);

        state.AddExceptionProcessorIfEnabled(ref processors);

        foreach (var processor in processors)
        {
            this.AddProcessor(processor);
            processorsAdded.Append(processor.GetType());
            processorsAdded.Append(';');
        }

        foreach (var instrumentation in state.Instrumentation)
        {
            if (instrumentation.Instance is not null)
            {
                this.Instrumentations.Add(instrumentation.Instance);
            }

            instrumentationFactoriesAdded.Append(instrumentation.Name);
            instrumentationFactoriesAdded.Append(';');
        }

        if (processorsAdded.Length != 0)
        {
            processorsAdded.Remove(processorsAdded.Length - 1, 1);
            OpenTelemetrySdkEventSource.Log.TracerProviderSdkEvent($"Processors added = \"{processorsAdded}\".");
        }

        if (instrumentationFactoriesAdded.Length != 0)
        {
            instrumentationFactoriesAdded.Remove(instrumentationFactoriesAdded.Length - 1, 1);
            OpenTelemetrySdkEventSource.Log.TracerProviderSdkEvent($"Instrumentations added = \"{instrumentationFactoriesAdded}\".");
        }

        var activityListener = new ActivityListener();

        if (this.supportLegacyActivity)
        {
            Func<Activity, bool>? legacyActivityPredicate = legacyActivityWildcardModeRegex != null
                ? (activity => legacyActivityWildcardModeRegex.IsMatch(activity.OperationName))
                : (activity => state.LegacyActivityOperationNames.Contains(activity.OperationName));

            activityListener.ActivityStarted = activity =>
            {
                OpenTelemetrySdkEventSource.Log.ActivityStarted(activity);

                if (string.IsNullOrEmpty(activity.Source.Name))
                {
                    if (legacyActivityPredicate(activity))
                    {
                        // Legacy activity matches the user configured list.
                        // Call sampler for the legacy activity
                        // unless suppressed.
                        if (!Sdk.SuppressInstrumentation)
                        {
                            this.getRequestedDataAction!(activity);
                        }
                        else
                        {
                            activity.IsAllDataRequested = false;
                        }
                    }
                    else
                    {
                        // Legacy activity doesn't match the user configured list. No need to proceed further.
                        return;
                    }
                }

                if (!activity.IsAllDataRequested)
                {
                    return;
                }

                if (SuppressInstrumentationScope.IncrementIfTriggered() == 0)
                {
                    this.Processor?.OnStart(activity);
                }
            };

            activityListener.ActivityStopped = activity =>
            {
                OpenTelemetrySdkEventSource.Log.ActivityStopped(activity);

                if (string.IsNullOrEmpty(activity.Source.Name) && !legacyActivityPredicate(activity))
                {
                    // Legacy activity doesn't match the user configured list. No need to proceed further.
                    return;
                }

                if (!activity.IsAllDataRequested)
                {
                    return;
                }

                // Spec says IsRecording must be false once span ends.
                // https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/trace/api.md#isrecording
                // However, Activity has slightly different semantic
                // than Span and we don't have strong reason to do this
                // now, as Activity anyway allows read/write always.
                // Intentionally commenting the following line.
                // activity.IsAllDataRequested = false;

                if (SuppressInstrumentationScope.DecrementIfTriggered() == 0)
                {
                    this.Processor?.OnEnd(activity);
                }
            };
        }
        else
        {
            activityListener.ActivityStarted = activity =>
            {
                OpenTelemetrySdkEventSource.Log.ActivityStarted(activity);

                if (activity.IsAllDataRequested && SuppressInstrumentationScope.IncrementIfTriggered() == 0)
                {
                    this.Processor?.OnStart(activity);
                }
            };

            activityListener.ActivityStopped = activity =>
            {
                OpenTelemetrySdkEventSource.Log.ActivityStopped(activity);

                if (!activity.IsAllDataRequested)
                {
                    return;
                }

                // Spec says IsRecording must be false once span ends.
                // https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/trace/api.md#isrecording
                // However, Activity has slightly different semantic
                // than Span and we don't have strong reason to do this
                // now, as Activity anyway allows read/write always.
                // Intentionally commenting the following line.
                // activity.IsAllDataRequested = false;

                if (SuppressInstrumentationScope.DecrementIfTriggered() == 0)
                {
                    this.Processor?.OnEnd(activity);
                }
            };
        }

        if (this.Sampler is AlwaysOnSampler)
        {
            activityListener.Sample = static (ref _) =>
                !Sdk.SuppressInstrumentation ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.None;
            this.getRequestedDataAction = this.RunGetRequestedDataAlwaysOnSampler;
        }
        else if (this.Sampler is AlwaysOffSampler)
        {
            activityListener.Sample = (ref options) =>
                !Sdk.SuppressInstrumentation ? PropagateOrIgnoreData(ref options) : ActivitySamplingResult.None;
            this.getRequestedDataAction = this.RunGetRequestedDataAlwaysOffSampler;
        }
        else
        {
            // This delegate informs ActivitySource about sampling decision when the parent context is an ActivityContext.
            activityListener.Sample = (ref options) =>
                !Sdk.SuppressInstrumentation ? ComputeActivitySamplingResult(ref options, this.Sampler) : ActivitySamplingResult.None;
            this.getRequestedDataAction = this.RunGetRequestedDataOtherSampler;
        }

        // Sources can be null. This happens when user
        // is only interested in InstrumentationLibraries
        // which do not depend on ActivitySources.
        if (state.Sources.Count > 0)
        {
            // Validation of source name is already done in builder.
            if (state.Sources.Any(WildcardHelper.ContainsWildcard))
            {
                var regex = WildcardHelper.GetWildcardRegex(state.Sources);

                // Function which takes ActivitySource and returns true/false to indicate if it should be subscribed to
                // or not.
                activityListener.ShouldListenTo = this.supportLegacyActivity ?
                    (activitySource) => string.IsNullOrEmpty(activitySource.Name) || regex.IsMatch(activitySource.Name) :
                    (activitySource) => regex.IsMatch(activitySource.Name);
            }
            else
            {
                var activitySources = new HashSet<string>(state.Sources, StringComparer.OrdinalIgnoreCase);

                if (this.supportLegacyActivity)
                {
                    activitySources.Add(string.Empty);
                }

                // Function which takes ActivitySource and returns true/false to indicate if it should be subscribed to
                // or not.
                activityListener.ShouldListenTo = activitySource => activitySources.Contains(activitySource.Name);
            }
        }
        else
        {
            if (this.supportLegacyActivity)
            {
                activityListener.ShouldListenTo = activitySource => string.IsNullOrEmpty(activitySource.Name);
            }
        }

        ActivitySource.AddActivityListener(activityListener);
        this.listener = activityListener;
        OpenTelemetrySdkEventSource.Log.TracerProviderSdkEvent("TracerProvider built successfully.");
    }

    internal Resource Resource { get; }

    internal List<object> Instrumentations { get; } = [];

    internal BaseProcessor<Activity>? Processor { get; private set; }

    internal Sampler Sampler { get; }

    internal TracerProviderSdk AddProcessor(BaseProcessor<Activity> processor)
    {
        Guard.ThrowIfNull(processor);

        processor.SetParentProvider(this);

        if (this.Processor == null)
        {
            this.Processor = processor;
        }
        else if (this.Processor is CompositeProcessor<Activity> compositeProcessor)
        {
            compositeProcessor.AddProcessor(processor);
        }
        else
        {
            var newCompositeProcessor = new CompositeProcessor<Activity>([this.Processor]);
            newCompositeProcessor.SetParentProvider(this);
            newCompositeProcessor.AddProcessor(processor);
            this.Processor = newCompositeProcessor;
        }

        return this;
    }

    internal bool OnForceFlush(int timeoutMilliseconds)
        => this.Processor?.ForceFlush(timeoutMilliseconds) ?? true;

    /// <summary>
    /// Called by <c>Shutdown</c>. This function should block the current
    /// thread until shutdown completed or timed out.
    /// </summary>
    /// <param name="timeoutMilliseconds">
    /// The number (non-negative) of milliseconds to wait, or
    /// <c>Timeout.Infinite</c> to wait indefinitely.
    /// </param>
    /// <returns>
    /// Returns <c>true</c> when shutdown succeeded; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// This function is called synchronously on the thread which made the
    /// first call to <c>Shutdown</c>. This function should not throw
    /// exceptions.
    /// </remarks>
    internal bool OnShutdown(int timeoutMilliseconds)
    {
        // TODO Put OnShutdown logic in a task to run within the user provider timeoutMilliseconds
        foreach (var item in this.Instrumentations)
        {
            (item as IDisposable)?.Dispose();
        }

        this.Instrumentations.Clear();

        var result = this.Processor?.Shutdown(timeoutMilliseconds);
        this.listener?.Dispose();
        return result ?? true;
    }

    protected override void Dispose(bool disposing)
    {
        if (!this.Disposed)
        {
            if (disposing)
            {
                foreach (var item in this.Instrumentations)
                {
                    (item as IDisposable)?.Dispose();
                }

                this.Instrumentations.Clear();

                (this.Sampler as IDisposable)?.Dispose();

                // Wait for up to 5 seconds grace period
                this.Processor?.Shutdown(5000);
                this.Processor?.Dispose();
                this.Processor = null;

                // Shutdown the listener last so that anything created while instrumentation cleans up will still be processed.
                // Redis instrumentation, for example, flushes during dispose which creates Activity objects for any profiling
                // sessions that were open.
                this.listener?.Dispose();

                this.OwnedServiceProvider?.Dispose();
                this.OwnedServiceProvider = null;
            }

            this.Disposed = true;
            OpenTelemetrySdkEventSource.Log.ProviderDisposed(nameof(TracerProvider));
        }

        base.Dispose(disposing);
    }

    // Precedence: programmatic (programmaticSampler) > declarative (declarativeSampler) > env-var (options) > default.
    private static Sampler GetSampler(
        SamplerOptions options,
        Sampler? programmaticSampler,
        Sampler? declarativeSampler,
        PluginComponentProviderRegistry? registry,
        IServiceProvider serviceProvider)
    {
        if (programmaticSampler != null)
        {
            var envSamplerType = options.SamplerType;
            if (!string.IsNullOrWhiteSpace(envSamplerType))
            {
                OpenTelemetrySdkEventSource.Log.TracerProviderSdkEvent(
                    $"Trace sampler configuration value '{envSamplerType}' has been ignored because a value '{programmaticSampler.GetType().FullName}' was set programmatically.");
            }

            if (declarativeSampler != null)
            {
                OpenTelemetrySdkEventSource.Log.TracerProviderSdkEvent(
                    $"Declarative trace sampler '{declarativeSampler.GetType().FullName}' has been ignored because a value '{programmaticSampler.GetType().FullName}' was set programmatically.");
            }

            return programmaticSampler;
        }

        if (declarativeSampler != null)
        {
            var envSamplerType = options.SamplerType;
            if (!string.IsNullOrWhiteSpace(envSamplerType))
            {
                OpenTelemetrySdkEventSource.Log.TracerProviderSdkEvent(
                    $"Trace sampler configuration value '{envSamplerType}' has been ignored because a declarative sampler '{declarativeSampler.GetType().FullName}' was configured.");
            }

            return declarativeSampler;
        }

        Sampler? sampler = null;
        var samplerType = options.SamplerType;
        if (!string.IsNullOrWhiteSpace(samplerType))
        {
            sampler = CreateSamplerFromEnvVar(samplerType!, options, registry, serviceProvider);
            if (sampler == null)
            {
                OpenTelemetrySdkEventSource.Log.TracesSamplerConfigInvalid(samplerType!);
            }
            else
            {
                OpenTelemetrySdkEventSource.Log.TracerProviderSdkEvent($"Trace sampler set to '{sampler.GetType().FullName}' from configuration.");
            }
        }

        return sampler ?? new ParentBasedSampler(AlwaysOnSampler.Instance);
    }

    private static double GetSamplerArgument(SamplerOptions options)
    {
        var ratio = options.SamplerArgument;
        if (ratio.HasValue && !double.IsNaN(ratio.Value) && !double.IsInfinity(ratio.Value)
            && ratio.Value >= 0.0 && ratio.Value <= 1.0)
        {
            return ratio.Value;
        }

        // Log with the raw string when available; fall back to the numeric value so programmatic
        // out-of-range assignments (e.g. Configure<SamplerOptions>(o => o.SamplerArgument = 2.0)) still
        // produce a useful diagnostic rather than an empty string.
        var logValue = options.SamplerArgumentRaw
            ?? ratio?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ?? string.Empty;
        OpenTelemetrySdkEventSource.Log.TracesSamplerArgConfigInvalid(logValue);
        return 1.0;
    }

    // Maps the closed OTEL_TRACES_SAMPLER vocabulary (case-insensitive env-var names) to schema
    // names and property bags, then dispatches through the PluginComponentProviderRegistry. This keeps
    // the env-var path and the declarative-YAML path sharing the same factory implementations.
    private static Sampler? CreateSamplerFromEnvVar(
        string samplerType,
        SamplerOptions options,
        PluginComponentProviderRegistry? registry,
        IServiceProvider serviceProvider)
    {
        if (registry == null)
        {
            return null;
        }

        string schemaName;
        IDeclarativeConfigProperties bag;

        if (string.Equals(samplerType, SamplerEnvVarAlwaysOn, StringComparison.OrdinalIgnoreCase))
        {
            schemaName = AlwaysOnSamplerFactory.SchemaName;
            bag = EmptyDeclarativeConfigProperties.Instance;
        }
        else if (string.Equals(samplerType, SamplerEnvVarAlwaysOff, StringComparison.OrdinalIgnoreCase))
        {
            schemaName = AlwaysOffSamplerFactory.SchemaName;
            bag = EmptyDeclarativeConfigProperties.Instance;
        }
        else if (string.Equals(samplerType, SamplerEnvVarTraceIdRatio, StringComparison.OrdinalIgnoreCase))
        {
            var ratio = GetSamplerArgument(options); // validates range, logs if invalid
            schemaName = TraceIdRatioBasedSamplerFactory.SchemaName;
            bag = SimpleDeclarativeConfigProperties.WithStringEntry(
                "ratio", ratio.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (string.Equals(samplerType, SamplerEnvVarParentBasedAlwaysOn, StringComparison.OrdinalIgnoreCase))
        {
            schemaName = ParentBasedSamplerFactory.SchemaName;
            bag = BuildParentBasedBag(AlwaysOnSamplerFactory.SchemaName, null);
        }
        else if (string.Equals(samplerType, SamplerEnvVarParentBasedAlwaysOff, StringComparison.OrdinalIgnoreCase))
        {
            schemaName = ParentBasedSamplerFactory.SchemaName;
            bag = BuildParentBasedBag(AlwaysOffSamplerFactory.SchemaName, null);
        }
        else if (string.Equals(samplerType, SamplerEnvVarParentBasedTraceIdRatio, StringComparison.OrdinalIgnoreCase))
        {
            var ratio = GetSamplerArgument(options); // validates range, logs if invalid
            schemaName = ParentBasedSamplerFactory.SchemaName;
            bag = BuildParentBasedBag(
                TraceIdRatioBasedSamplerFactory.SchemaName,
                ratio.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            return null; // unknown sampler type
        }

        return registry.TryCreate<Sampler>(schemaName, bag, serviceProvider, out var sampler)
            ? sampler
            : null;
    }

    // Builds the bag that parent_based expects: { "root": { rootSchemaName: innerBag } }.
    // When ratioRaw is null the inner bag is empty (factory uses default 1.0).
    private static IDeclarativeConfigProperties BuildParentBasedBag(string rootSchemaName, string? ratioRaw)
    {
        IDeclarativeConfigProperties innerBag = ratioRaw != null
            ? SimpleDeclarativeConfigProperties.WithStringEntry("ratio", ratioRaw)
            : EmptyDeclarativeConfigProperties.Instance;

        var rootPluginProperties = SimpleDeclarativeConfigProperties.WithNestedBag(rootSchemaName, innerBag);
        return SimpleDeclarativeConfigProperties.WithNestedBag("root", rootPluginProperties);
    }

    private static ActivitySamplingResult ComputeActivitySamplingResult(
        ref ActivityCreationOptions<ActivityContext> options,
        Sampler sampler)
    {
        var samplingParameters = new SamplingParameters(
            options.Parent,
            options.TraceId,
            options.Name,
            options.Kind,
            options.Tags,
            options.Links);

        var samplingResult = sampler.ShouldSample(samplingParameters);

        var activitySamplingResult = samplingResult.Decision switch
        {
            SamplingDecision.RecordAndSample => ActivitySamplingResult.AllDataAndRecorded,
            SamplingDecision.RecordOnly => ActivitySamplingResult.AllData,
            SamplingDecision.Drop or _ => PropagateOrIgnoreData(ref options),
        };

        if (activitySamplingResult > ActivitySamplingResult.PropagationData)
        {
            if (samplingResult.AttributesOrNull is { } attributes)
            {
                foreach (var att in attributes)
                {
                    options.SamplingTags.Add(att.Key, att.Value);
                }
            }
        }

        if (activitySamplingResult != ActivitySamplingResult.None
            && samplingResult.TraceStateString != null)
        {
            // https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/trace/sdk.md#sampler
            // Spec requires clearing Tracestate if empty Tracestate is returned.
            // Since .NET did not have this capability, it'll break
            // existing samplers if we did that. So the following is
            // adopted to remain spec-compliant and backward compat.
            // The behavior is:
            // if sampler returns null, its treated as if it has not intended
            // to change Tracestate. Existing SamplingResult ctors will put null as default TraceStateString,
            // so all existing samplers will get this behavior.
            // if sampler returns non-null, then it'll be used as the
            // new value for Tracestate
            // A sampler can return string.Empty if it intends to clear the state.
            options = options with { TraceState = samplingResult.TraceStateString };
        }

        return activitySamplingResult;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ActivitySamplingResult PropagateOrIgnoreData(ref ActivityCreationOptions<ActivityContext> options)
    {
        var isRootSpan = options.Parent.TraceId == default;

        // If it is the root span or the parent is remote select PropagationData so the trace ID is preserved
        // even if no activity of the trace is recorded (sampled per OpenTelemetry parlance).
        return (isRootSpan || options.Parent.IsRemote)
            ? ActivitySamplingResult.PropagationData
            : ActivitySamplingResult.None;
    }

    private void RunGetRequestedDataAlwaysOnSampler(Activity activity)
    {
        activity.IsAllDataRequested = true;
        activity.ActivityTraceFlags |= ActivityTraceFlags.Recorded;
    }

    private void RunGetRequestedDataAlwaysOffSampler(Activity activity)
    {
        activity.IsAllDataRequested = false;
        activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
    }

    private void RunGetRequestedDataOtherSampler(Activity activity)
    {
        // Check activity.ParentId alone is sufficient to normally determine if a activity is root or not. But if one uses activity.SetParentId to override the TraceId (without intending to set an actual parent), then additional check of parentspanid being empty is required to confirm if an activity is root or not.
        // This checker can be removed, once Activity exposes an API to customize ID Generation (https://github.com/dotnet/runtime/issues/46704) or issue https://github.com/dotnet/runtime/issues/46706 is addressed.
        var parentContext = string.IsNullOrEmpty(activity.ParentId) || activity.ParentSpanId.ToHexString() == "0000000000000000"
            ? default
            : activity.Parent != null ?
                activity.Parent.Context :
                new ActivityContext(
                    activity.TraceId,
                    activity.ParentSpanId,
                    activity.ActivityTraceFlags,
                    activity.TraceStateString,
                    isRemote: true);

        var samplingParameters = new SamplingParameters(
            parentContext,
            activity.TraceId,
            activity.DisplayName,
            activity.Kind,
            activity.TagObjects,
            activity.Links);

        var samplingResult = this.Sampler.ShouldSample(samplingParameters);

        switch (samplingResult.Decision)
        {
            case SamplingDecision.Drop:
                activity.IsAllDataRequested = false;
                activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
                break;
            case SamplingDecision.RecordOnly:
                activity.IsAllDataRequested = true;
                activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
                break;
            case SamplingDecision.RecordAndSample:
                activity.IsAllDataRequested = true;
                activity.ActivityTraceFlags |= ActivityTraceFlags.Recorded;
                break;
            default:
                break;
        }

        if (samplingResult.Decision != SamplingDecision.Drop)
        {
            if (samplingResult.AttributesOrNull is { } attributes)
            {
                foreach (var att in attributes)
                {
                    activity.SetTag(att.Key, att.Value);
                }
            }
        }

        if (samplingResult.TraceStateString != null)
        {
            activity.TraceStateString = samplingResult.TraceStateString;
        }
    }
}
