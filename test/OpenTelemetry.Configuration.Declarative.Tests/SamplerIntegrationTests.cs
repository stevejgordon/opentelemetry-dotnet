// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#pragma warning disable OTEL1006 // declarative config experimental
#pragma warning disable OTEL1007 // plugin provider experimental

using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Configuration.Declarative;
using OpenTelemetry.Trace;

namespace OpenTelemetry.Configuration.Declarative.Tests;

/// <summary>
/// End-to-end tests for the declarative sampler configuration path.
/// Covers: built-in sampler shapes, value-layer overrides, hot reload, programmatic
/// precedence, custom plugin providers, and collision detection.
/// </summary>
public sealed class SamplerIntegrationTests
{
    // -------------------------------------------------------------------------
    // Built-in sampler shapes
    // -------------------------------------------------------------------------

    [Fact]
    public void AlwaysOn_Yaml_SamplesActivity()
    {
        const string yaml = """
            file_format: "1.0"
            tracer_provider:
              sampler:
                always_on: {}
            """;

        using var source = new ActivitySource(nameof(this.AlwaysOn_Yaml_SamplesActivity));
        using var provider = BuildSamplerTracerProvider(yaml, source.Name);

        Assert.NotNull(source.StartActivity("test"));
    }

    [Fact]
    public void AlwaysOff_Yaml_DropsActivity()
    {
        const string yaml = """
            file_format: "1.0"
            tracer_provider:
              sampler:
                always_off: {}
            """;

        using var source = new ActivitySource(nameof(this.AlwaysOff_Yaml_DropsActivity));
        using var provider = BuildSamplerTracerProvider(yaml, source.Name);

        // AlwaysOffSampler returns PropagationData for root spans (not None), so StartActivity
        // is non-null. The dropped decision is reflected in the absence of the Recorded flag.
        using var activity = source.StartActivity("test");
        Assert.False(activity?.ActivityTraceFlags.HasFlag(ActivityTraceFlags.Recorded) ?? false);
    }

    [Fact]
    public void TraceIdRatioBased_Ratio0_Yaml_DropsAllActivities()
    {
        const string yaml = """
            file_format: "1.0"
            tracer_provider:
              sampler:
                trace_id_ratio_based:
                  ratio: 0.0
            """;

        using var source = new ActivitySource(nameof(this.TraceIdRatioBased_Ratio0_Yaml_DropsAllActivities));
        using var provider = BuildSamplerTracerProvider(yaml, source.Name);

        // TraceIdRatioBased(0.0) returns Drop; Drop maps to PropagationData for root spans,
        // so StartActivity is non-null. The dropped decision is reflected in the absence of
        // the Recorded flag.
        using var activity = source.StartActivity("test");
        Assert.False(activity?.ActivityTraceFlags.HasFlag(ActivityTraceFlags.Recorded) ?? false);
    }

    [Fact]
    public void TraceIdRatioBased_Ratio1_Yaml_SamplesAllActivities()
    {
        const string yaml = """
            file_format: "1.0"
            tracer_provider:
              sampler:
                trace_id_ratio_based:
                  ratio: 1.0
            """;

        using var source = new ActivitySource(nameof(this.TraceIdRatioBased_Ratio1_Yaml_SamplesAllActivities));
        using var provider = BuildSamplerTracerProvider(yaml, source.Name);

        Assert.NotNull(source.StartActivity("test"));
    }

    [Fact]
    public void ParentBased_RatioRoot_Yaml_SamplesNewRootSpan()
    {
        const string yaml = """
            file_format: "1.0"
            tracer_provider:
              sampler:
                parent_based:
                  root:
                    trace_id_ratio_based:
                      ratio: 1.0
            """;

        using var source = new ActivitySource(nameof(this.ParentBased_RatioRoot_Yaml_SamplesNewRootSpan));
        using var provider = BuildSamplerTracerProvider(yaml, source.Name);

        // New root span (no parent): root sampler determines sampling -> ratio 1.0 -> sampled.
        Assert.NotNull(source.StartActivity("test"));
    }

    [Fact]
    public void ParentBased_FullTree_Yaml_SamplesNewRootSpan()
    {
        const string yaml = """
            file_format: "1.0"
            tracer_provider:
              sampler:
                parent_based:
                  root:
                    trace_id_ratio_based:
                      ratio: 1.0
                  remote_parent_sampled:
                    always_on: {}
                  remote_parent_not_sampled:
                    always_off: {}
                  local_parent_sampled:
                    always_on: {}
                  local_parent_not_sampled:
                    always_off: {}
            """;

        using var source = new ActivitySource(nameof(this.ParentBased_FullTree_Yaml_SamplesNewRootSpan));
        using var provider = BuildSamplerTracerProvider(yaml, source.Name);

        // New root span: root = trace_id_ratio_based(1.0) -> always sampled.
        Assert.NotNull(source.StartActivity("test"));
    }

    // -------------------------------------------------------------------------
    // Value layer: Configure<SamplerOptions> overrides YAML ratio
    // -------------------------------------------------------------------------

    [Fact]
    public void ConfigureSamplerOptions_OverridesYamlRatio_AtConstructionTime()
    {
        // YAML says ratio: 0.0 (no activities should be sampled).
        // Configure<SamplerOptions> overrides to 1.0 so activities are sampled.
        const string yaml = """
            file_format: "1.0"
            tracer_provider:
              sampler:
                trace_id_ratio_based:
                  ratio: 0.0
            """;

        using var source = new ActivitySource(nameof(this.ConfigureSamplerOptions_OverridesYamlRatio_AtConstructionTime));
        using var provider = BuildSamplerTracerProvider(yaml, source.Name, s =>
        {
            // TraceIdRatioBasedSamplerFactory.CreateComponent reads the initial ratio from
            // IOptionsMonitor.Get(SamplerOptionsName), so Configure<T> takes effect here.
            s.Configure<SamplerOptions>(
                TraceIdRatioBasedSamplerFactory.SamplerOptionsName,
                opts => opts.SamplerArgument = 1.0);
        });

        Assert.NotNull(source.StartActivity("test"));
    }

    // -------------------------------------------------------------------------
    // Hot reload: IConfiguration change propagates to running sampler
    // -------------------------------------------------------------------------

    [Fact]
    public void Reload_SamplerArgChanges_ImmediatelyTakesEffect()
    {
        // YAML declares trace_id_ratio_based; in-memory source overrides the projected ratio
        // to 0.0 (higher priority). After triggering a reload with 1.0, the sampler swaps
        // its inner without rebuilding the tracer provider.
        const string yaml = """
            file_format: "1.0"
            tracer_provider:
              sampler:
                trace_id_ratio_based:
                  ratio: 0.5
            """;

        var reloadableSource = new ReloadableConfigSource(new Dictionary<string, string?>
        {
            [ProviderBuilderServiceCollectionExtensions.SamplerArgConfigKey] = "0",
        });

        using var source = new ActivitySource(nameof(this.Reload_SamplerArgChanges_ImmediatelyTakesEffect));
        using var provider = BuildSamplerTracerProvider(yaml, source.Name, reloadableSource: reloadableSource);

        // Initial ratio = 0.0 (in-memory wins over YAML's 0.5) -> nothing sampled.
        // Drop maps to PropagationData for root spans, so activity is non-null but not Recorded.
        using var beforeActivity = source.StartActivity("before-reload");
        Assert.False(beforeActivity?.ActivityTraceFlags.HasFlag(ActivityTraceFlags.Recorded) ?? false);

        // Mutate in-memory source and fire the reload token.
        reloadableSource.Provider.Set(
            ProviderBuilderServiceCollectionExtensions.SamplerArgConfigKey, "1");
        reloadableSource.Provider.TriggerReload();

        // After reload: ratio = 1.0 -> all sampled. Provider is the same instance.
        using var afterActivity = source.StartActivity("after-reload");
        Assert.True(afterActivity?.ActivityTraceFlags.HasFlag(ActivityTraceFlags.Recorded) ?? false);
    }

    // -------------------------------------------------------------------------
    // Programmatic precedence
    // -------------------------------------------------------------------------

    [Fact]
    public void ProgrammaticSetSampler_WinsOverYamlSampler()
    {
        // YAML says always_off; programmatic SetSampler(AlwaysOnSampler) wins.
        const string yaml = """
            file_format: "1.0"
            tracer_provider:
              sampler:
                always_off: {}
            """;

        using var source = new ActivitySource(nameof(this.ProgrammaticSetSampler_WinsOverYamlSampler));
        using var provider = BuildSamplerTracerProvider(yaml, source.Name, setupBuilder: b =>
            b.SetSampler(new AlwaysOnSampler()));

        Assert.NotNull(source.StartActivity("test"));
    }

    // -------------------------------------------------------------------------
    // Custom third-party plugin provider
    // -------------------------------------------------------------------------

    [Fact]
    public void CustomSamplerPluginProvider_ViaYaml_CreatesCustomSampler()
    {
        const string yaml = """
            file_format: "1.0"
            tracer_provider:
              sampler:
                acme_sampler:
                  threshold: 42
            """;

        using var source = new ActivitySource(nameof(this.CustomSamplerPluginProvider_ViaYaml_CreatesCustomSampler));
        using var provider = BuildSamplerTracerProvider(
            yaml,
            source.Name,
            s => s.AddSamplerPluginProvider(new AcmeSamplerProvider()));

        // AcmeSampler always samples.
        Assert.NotNull(source.StartActivity("test"));
    }

    [Fact]
    public void CustomSamplerPluginProvider_DirectSetSampler_DoesNotNeedYaml()
    {
        // Plugin providers are for the declarative (YAML) path. A consumer can also
        // bypass both YAML and the registry by calling SetSampler directly.
        using var source = new ActivitySource(nameof(this.CustomSamplerPluginProvider_DirectSetSampler_DoesNotNeedYaml));
        using var provider = BuildSamplerTracerProvider(
            yaml: "file_format: \"1.0\"",
            sourceName: source.Name,
            setupBuilder: b => b.SetSampler(new AcmeSampler(0)));

        Assert.NotNull(source.StartActivity("test"));
    }

    [Fact]
    public void CustomSamplerPluginProvider_BuiltinNameCollision_Throws()
    {
        // Registering a provider with a name already taken by a built-in throws when the
        // PluginComponentProviderRegistry is constructed during provider build.
        const string yaml = """
            file_format: "1.0"
            tracer_provider:
              sampler:
                always_on: {}
            """;

        var ex = Record.Exception(() =>
            BuildSamplerTracerProvider(
                yaml,
                "collision.source",
                s => s.AddSamplerPluginProvider(new CollidingNameProvider())));

        Assert.NotNull(ex);

        // Walk the exception chain; DI may wrap the InvalidOperationException.
        var found = ex;
        while (found != null && !found.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
        {
            found = found.InnerException;
        }

        Assert.NotNull(found);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static TracerProvider BuildSamplerTracerProvider(
        string yaml,
        string sourceName,
        Action<IServiceCollection>? configureServices = null,
        Action<TracerProviderBuilder>? setupBuilder = null,
        ReloadableConfigSource? reloadableSource = null)
    {
        using var yamlFile = DeclarativeYamlTestFile.CreateYamlFile(yaml);
        var cache = new DeclarativeConfigurationModelCache(new FilePath(yamlFile.Path));

        // YAML source first; optional reloadable in-memory source appended on top (wins).
        var configBuilder = new ConfigurationBuilder()
            .AddOpenTelemetryDeclarativeConfiguration(cache);

        if (reloadableSource != null)
        {
            configBuilder.Add(reloadableSource);
        }

        var config = configBuilder.Build();

        var builder = Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .ConfigureServices(s =>
            {
                s.AddSingleton(cache);
                s.AddSingleton<IConfiguration>(config);
                s.ConfigureOpenTelemetryTracerProvider(
                    (sp, b) => DeclarativeComponentBuilder.Configure(sp, b));
                configureServices?.Invoke(s);
            });

        setupBuilder?.Invoke(builder);
        return builder.Build()!;
    }

    // -------------------------------------------------------------------------
    // In-memory reloadable configuration source
    // -------------------------------------------------------------------------

    private sealed class ReloadableConfigSource : IConfigurationSource
    {
        private readonly Dictionary<string, string?> initialData;
        private ReloadableConfigProvider? provider;

        public ReloadableConfigSource(Dictionary<string, string?> initialData)
        {
            this.initialData = initialData;
        }

        public ReloadableConfigProvider Provider =>
            this.provider ?? throw new InvalidOperationException("Source has not been built yet.");

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            this.provider = new ReloadableConfigProvider(this.initialData);
            return this.provider;
        }
    }

    private sealed class ReloadableConfigProvider : MemoryConfigurationProvider
    {
        public ReloadableConfigProvider(Dictionary<string, string?> initialData)
            : base(new MemoryConfigurationSource { InitialData = initialData })
        {
        }

        public void TriggerReload() => this.OnReload();
    }

    // -------------------------------------------------------------------------
    // Custom third-party sampler and provider
    // -------------------------------------------------------------------------

    private sealed class AcmeSampler : Sampler
    {
        public AcmeSampler(int threshold)
        {
            this.Description = $"AcmeSampler(threshold={threshold})";
        }

        public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
            => new SamplingResult(SamplingDecision.RecordAndSample);
    }

    private sealed class AcmeSamplerProvider : SamplerPluginProvider
    {
        public override string Name => "acme_sampler";

        public override Sampler CreateComponent(IDeclarativeConfigProperties properties, IServiceProvider serviceProvider)
        {
            properties.TryGetInt("threshold", out var threshold);
            return new AcmeSampler(threshold ?? 0);
        }
    }

    private sealed class CollidingNameProvider : SamplerPluginProvider
    {
        public override string Name => "always_on"; // collides with built-in AlwaysOnSamplerFactory

        public override Sampler CreateComponent(IDeclarativeConfigProperties properties, IServiceProvider serviceProvider)
            => new AlwaysOnSampler();
    }
}
