// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#pragma warning disable OTEL1007

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry;
using OpenTelemetry.Internal;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering declarative configuration SDK extension plugin
/// component providers in an <see cref="IServiceCollection"/>.
/// </summary>
[Experimental(DiagnosticDefinitions.PluginComponentProviderExperimentalApi, UrlFormat = DiagnosticDefinitions.ExperimentalApiUrlFormat)]
public static class ComponentRegistrationExtensions
{
    /// <summary>
    /// Adds a <see cref="SamplerPluginProvider"/> to the service collection so that the
    /// declarative configuration engine can use it when building a
    /// <see cref="OpenTelemetry.Trace.Sampler"/> from a YAML file.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <param name="provider">The sampler plugin provider to register.</param>
    /// <returns>The original <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddSamplerPluginProvider(this IServiceCollection services, SamplerPluginProvider provider)
    {
        Guard.ThrowIfNull(services);
        Guard.ThrowIfNull(provider);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPluginComponentProvider>(provider));
        return services;
    }

    /// <summary>
    /// Adds a <see cref="SpanProcessorPluginProvider"/> to the service collection so
    /// that the declarative configuration engine can use it when building a
    /// <see cref="BaseProcessor{Activity}"/> from a YAML file.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <param name="provider">The span processor plugin provider to register.</param>
    /// <returns>The original <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddSpanProcessorPluginProvider(this IServiceCollection services, SpanProcessorPluginProvider provider)
    {
        Guard.ThrowIfNull(services);
        Guard.ThrowIfNull(provider);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPluginComponentProvider>(provider));
        return services;
    }

    /// <summary>
    /// Adds a <see cref="SpanExporterPluginProvider"/> to the service collection so
    /// that the declarative configuration engine can use it when building a
    /// <see cref="BaseExporter{Activity}"/> from a YAML file.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <param name="provider">The span exporter plugin provider to register.</param>
    /// <returns>The original <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddSpanExporterPluginProvider(this IServiceCollection services, SpanExporterPluginProvider provider)
    {
        Guard.ThrowIfNull(services);
        Guard.ThrowIfNull(provider);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPluginComponentProvider>(provider));
        return services;
    }
}
