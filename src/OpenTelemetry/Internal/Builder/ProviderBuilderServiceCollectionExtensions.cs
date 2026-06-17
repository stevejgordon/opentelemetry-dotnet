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
    // Full IConfiguration key for the ratio value projected by DeclarativeConfigurationConverter (Flow 1).
    // DeclarativeConfigurationConverter.DeclarativeSamplerArgKey must equal this value.
    // Read directly from the root IConfiguration rather than a subsection so there is one canonical
    // path and no implicit dependency on the options name used by the caller.
    internal const string DeclarativeSamplerArgKey = "declarative:traces:sampler:arg";

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
                if (name.StartsWith("declarative:", StringComparison.Ordinal))
                {
                    // Read directly from root IConfiguration using the full projected key.
                    // The key is emitted by DeclarativeConfigurationConverter (Flow 1).
                    var argRaw = configuration[DeclarativeSamplerArgKey];
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
}
