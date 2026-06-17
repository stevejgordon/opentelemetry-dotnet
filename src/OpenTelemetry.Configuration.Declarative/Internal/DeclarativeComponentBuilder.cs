// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#pragma warning disable OTEL1007 // PluginComponentProviderRegistry / IDeclarativeConfigProperties are experimental; used internally

using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace OpenTelemetry.Configuration.Declarative;

/// <summary>
/// Walks the typed <see cref="DeclarativeConfiguration"/> model and creates SDK components
/// from the <see cref="PluginComponentProviderRegistry"/>, wiring them into the
/// <see cref="TracerProviderBuilder"/> being built.
/// </summary>
/// <remarks>
/// This class is registered via <c>services.ConfigureOpenTelemetryTracerProvider</c> and is
/// invoked once when the <see cref="TracerProvider"/> singleton is first resolved from the
/// application service provider.
/// </remarks>
internal static class DeclarativeComponentBuilder
{
    internal static void Configure(IServiceProvider serviceProvider, TracerProviderBuilder builder)
    {
        var cache = serviceProvider.GetService<DeclarativeConfigurationModelCache>();
        if (cache == null)
        {
            return;
        }

        var result = cache.TryGetResult();
        if (result == null)
        {
            OpenTelemetryDeclarativeConfigurationEventSource.Log.DeclarativeConfigNotLoadedAtBuildTime(cache.FilePath.ToString());
            return;
        }

        if (!result.Model.TracerProvider.TryGetValue(out var tpConfig))
        {
            return;
        }

        // The SDK builder is needed for SetDeclarativeSampler.
        // If the builder is not the SDK implementation (e.g. a no-op builder during testing),
        // skip silently rather than throw.
        if (builder is not TracerProviderBuilderSdk sdk)
        {
            return;
        }

        var registry = serviceProvider.GetRequiredService<PluginComponentProviderRegistry>();

        ConfigureSampler(sdk, tpConfig, registry, serviceProvider);
    }

    private static void ConfigureSampler(
        TracerProviderBuilderSdk sdk,
        TracerProviderConfiguration tpConfig,
        PluginComponentProviderRegistry registry,
        IServiceProvider serviceProvider)
    {
        if (!tpConfig.Sampler.TryGetValue(out var samplerConfig))
        {
            return;
        }

        if (!samplerConfig.IsValid)
        {
            OpenTelemetryDeclarativeConfigurationEventSource.Log.MalformedDeclarativeComponentSkipped("sampler");
            return;
        }

        try
        {
            var sampler = registry.Create<Sampler>(samplerConfig.PluginName, samplerConfig.Properties, serviceProvider);
            sdk.SetDeclarativeSampler(sampler);
        }
        catch (Exception ex)
        {
            OpenTelemetryDeclarativeConfigurationEventSource.Log.ComponentBuildFailed(
                "sampler", samplerConfig.PluginName, ex);
            throw;
        }
    }

}
