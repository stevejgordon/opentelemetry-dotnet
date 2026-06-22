// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#pragma warning disable OTEL1007 // SamplerPluginProvider is experimental; this internal class is part of that surface

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Trace;

internal sealed class TraceIdRatioBasedSamplerFactory : SamplerPluginProvider
{
    internal const string SchemaName = "trace_id_ratio_based";

    // Options name used by the sampler reload path. Must start with "opentelemetry:" so that
    // ProviderBuilderServiceCollectionExtensions's name-aware factory reads the projected
    // opentelemetry:traces:sampler:arg key rather than OTEL_TRACES_SAMPLER_ARG.
    // This is the contract key that any IConfiguration source (declarative YAML, telemetry
    // policy provider, etc.) must write to in order to drive a ratio reload.
    internal const string SamplerOptionsName = "opentelemetry:traces:sampler";

    public override string Name => SchemaName;

    public override Sampler CreateComponent(IDeclarativeConfigProperties properties, IServiceProvider serviceProvider)
    {
        var monitor = serviceProvider.GetRequiredService<IOptionsMonitor<SamplerOptions>>();
        var options = monitor.Get(SamplerOptionsName);

        double ratio;
        if (options.SamplerArgument.HasValue)
        {
            // IOptionsMonitor has a value - this covers two scenarios:
            // 1. YAML path: DeclarativeConfigurationConverter projected the ratio into IConfiguration
            //    and it flowed through the name-aware SamplerOptions factory.
            // 2. Configure<SamplerOptions>(SamplerOptionsName, ...) override: consumer overrides
            //    the parsed ratio at construction time.
            ratio = options.SamplerArgument.Value;
        }
        else
        {
            if (options.SamplerArgumentRaw != null)
            {
                OpenTelemetrySdkEventSource.Log.TraceIdRatioBasedSamplerRatioInvalid(options.SamplerArgumentRaw);
            }

            // Fallback to the properties bag. This covers the env-var path where
            // CreateSamplerFromEnvVar passes the ratio via SimpleDeclarativeConfigProperties
            // but never projects it into IConfiguration.
            properties.TryGetDouble("ratio", out var propRatio);
            ratio = propRatio ?? 1.0;
        }

        return new ReloadingTraceIdRatioSampler(monitor, SamplerOptionsName, ratio);
    }
}
