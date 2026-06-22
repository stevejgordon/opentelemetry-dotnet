// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.DependencyInjection;

internal static class ProviderBuilderServiceCollectionExtensions
{
    // Canonical IConfiguration key for the sampler ratio reload contract.
    // Any IConfiguration source that wants to drive a sampler ratio reload - the declarative
    // YAML provider, a telemetry policy provider (OpAMP, file-based, custom), or a test -
    // must write to this key. Sources registered later in IConfigurationBuilder win by
    // precedence, so policies override declarative config without any special casing.
    // DeclarativeConfigurationConverter.SamplerArgConfigKey must equal this value.
    internal const string SamplerArgConfigKey = "opentelemetry:traces:sampler:arg";

    public static IServiceCollection AddOpenTelemetryLoggerProviderBuilderServices(this IServiceCollection services)
    {
        Debug.Assert(services != null, "services was null");

#pragma warning disable CS8604 // Possible null reference argument.
        services.TryAddSingleton<LoggerProviderBuilderSdk>();
        services.RegisterOptionsFactory(configuration => new BatchExportLogRecordProcessorOptions(configuration));
        services.RegisterOptionsFactory(
            (sp, configuration, name) => new LogRecordExportProcessorOptions(
                sp.GetRequiredService<IOptionsMonitor<BatchExportLogRecordProcessorOptions>>().Get(name)));
#pragma warning restore CS8604 // Possible null reference argument.

        return services;
    }

    public static IServiceCollection AddOpenTelemetryMeterProviderBuilderServices(this IServiceCollection services)
    {
        Debug.Assert(services != null, "services was null");

#pragma warning disable CS8604 // Possible null reference argument.
        services.TryAddSingleton<MeterProviderBuilderSdk>();
        services.RegisterOptionsFactory(configuration => new PeriodicExportingMetricReaderOptions(configuration));
        services.RegisterOptionsFactory(
            (sp, configuration, name) => new MetricReaderOptions(
                sp.GetRequiredService<IOptionsMonitor<PeriodicExportingMetricReaderOptions>>().Get(name)));
#pragma warning restore CS8604 // Possible null reference argument.

        return services;
    }

    public static IServiceCollection AddOpenTelemetryTracerProviderBuilderServices(this IServiceCollection services)
    {
        Debug.Assert(services != null, "services was null");

#pragma warning disable CS8604 // Possible null reference argument.
        services.TryAddSingleton<TracerProviderBuilderSdk>();
        services.RegisterOptionsFactory(configuration => new BatchExportActivityProcessorOptions(configuration));
        services.RegisterOptionsFactory(
            (sp, configuration, name) => new ActivityExportProcessorOptions(
                sp.GetRequiredService<IOptionsMonitor<BatchExportActivityProcessorOptions>>().Get(name)));
        services.RegisterOptionsFactory(
            (sp, configuration, name) =>
            {
                if (name.StartsWith("opentelemetry:", StringComparison.Ordinal))
                {
                    // Read the canonical sampler ratio key. Written by any reload-capable
                    // IConfiguration source: declarative YAML (via DeclarativeConfigurationConverter),
                    // telemetry policy providers, or a consumer's own IConfigurationProvider.
                    var argRaw = configuration[SamplerArgConfigKey];
                    return new SamplerOptions
                    {
                        SamplerArgumentRaw = argRaw,
                        SamplerArgument = SamplerOptions.TryParseRatio(argRaw),
                    };
                }

                // Default: read from OTEL_TRACES_SAMPLER / OTEL_TRACES_SAMPLER_ARG.
                var envArgRaw = configuration[TracerProviderSdk.TracesSamplerArgConfigKey];
                return new SamplerOptions
                {
                    SamplerType = configuration[TracerProviderSdk.TracesSamplerConfigKey],
                    SamplerArgumentRaw = envArgRaw,
                    SamplerArgument = SamplerOptions.TryParseRatio(envArgRaw),
                };
            });
#pragma warning restore CS8604 // Possible null reference argument.

        // PluginComponentProviderRegistry is constructed once from all IPluginComponentProvider singletons.
        // TryAdd... methods ensure a single instance is registered, even when this method is called more than once
        // (e.g. Sdk.CreateTracerProviderBuilder() vs. services.AddOpenTelemetry().WithTracing()).
        // TryAddEnumerable prevents duplicate built-in plugin provider entries when the method is re-entered.
        services.TryAddSingleton<PluginComponentProviderRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPluginComponentProvider, AlwaysOnSamplerFactory>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPluginComponentProvider, AlwaysOffSamplerFactory>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPluginComponentProvider, TraceIdRatioBasedSamplerFactory>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPluginComponentProvider, ParentBasedSamplerFactory>());

        // Register a change token source so IOptionsMonitor<SamplerOptions> fires when IConfiguration
        // changes. This enables the reloadable sample-rate story: ReloadingTraceIdRatioSampler subscribes
        // to OnChange for SamplerOptionsName and rebuilds its inner sampler on every change.
        // A sealed subclass is used so TryAddEnumerable can distinguish it by implementation type;
        // factory-based descriptors where the implementation type equals the service type are rejected.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOptionsChangeTokenSource<SamplerOptions>, DeclarativeSamplerChangeTokenSource>());

        return services;
    }

    public static IServiceCollection AddOpenTelemetrySharedProviderBuilderServices(this IServiceCollection services)
    {
        Debug.Assert(services != null, "services was null");

        // Accessing Sdk class is just to trigger its static ctor,
        // which sets default Propagators and default Activity Id format
        _ = Sdk.SuppressInstrumentation;

#pragma warning disable CS8604 // Possible null reference argument.
        services.AddOptions();
#pragma warning restore CS8604 // Possible null reference argument.

        // Note: When using a host builder IConfiguration is automatically
        // registered and this registration will no-op. This only runs for
        // Sdk.Create* style or when manually creating a ServiceCollection. The
        // point of this registration is to make IConfiguration available in
        // those cases.
        services.TryAddSingleton<IConfiguration>(
            sp => new ConfigurationBuilder().AddEnvironmentVariables().Build());

        return services;
    }

    // Subclass gives TryAddEnumerable a distinct implementation type; factory descriptors
    // where implementation type == service type are rejected by TryAddEnumerable.
    private sealed class DeclarativeSamplerChangeTokenSource : ConfigurationChangeTokenSource<SamplerOptions>
    {
        public DeclarativeSamplerChangeTokenSource(IConfiguration configuration)
            : base(TraceIdRatioBasedSamplerFactory.SamplerOptionsName, configuration)
        {
        }
    }
}
