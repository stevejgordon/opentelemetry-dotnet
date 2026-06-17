// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using OpenTelemetry.Internal;

namespace OpenTelemetry;

/// <summary>
/// Base class for SDK extension plugin component providers that create SDK components from a
/// declarative configuration property bag.
/// </summary>
/// <typeparam name="TComponent">
/// The type of component this provider creates (e.g. <see cref="Trace.Sampler"/>).
/// </typeparam>
/// <remarks>
/// <para>
/// Implement this class to register a provider for a specific component type and
/// schema name. Register the provider with the SDK using the per-category helpers
/// (e.g. <c>services.AddSamplerPluginProvider(provider)</c>).
/// </para>
/// <para>
/// The abstract class (rather than an interface) is used here for two reasons:
/// </para>
/// <list type="number">
///   <item><description>
///     It provides the type-erasure bridge (<see cref="IPluginComponentProvider"/>
///     explicit implementation) without relying on default interface methods,
///     which are unavailable on <c>netstandard2.0</c> / <c>net462</c>.
///   </description></item>
///   <item><description>
///     It allows non-breaking evolvability: new virtual members can be added to
///     the base class without forcing re-compilation of existing implementations.
///   </description></item>
/// </list>
/// </remarks>
[Experimental(DiagnosticDefinitions.PluginComponentProviderExperimentalApi, UrlFormat = DiagnosticDefinitions.ExperimentalApiUrlFormat)]
public abstract class PluginComponentProvider<TComponent>
    : IPluginComponentProvider
    where TComponent : class
{
    /// <summary>
    /// Gets the schema-level name that selects this provider (case-sensitive,
    /// e.g. <c>"always_on"</c>, <c>"trace_id_ratio_based"</c>).
    /// </summary>
    public abstract string Name { get; }

    // IPluginComponentProvider is internal. Explicit implementations below satisfy the type-erasure bridge
    // without making the members part of the public surface. CA2119 ("seal methods that satisfy private
    // interfaces") cannot be applied to abstract/non-sealed classes, so it is suppressed on each member.
#pragma warning disable CA2119
    string IPluginComponentProvider.Name => this.Name;

    Type IPluginComponentProvider.ComponentType => typeof(TComponent);

    object IPluginComponentProvider.Create(IDeclarativeConfigProperties properties, IServiceProvider serviceProvider)
        => this.CreateComponent(properties, serviceProvider);
#pragma warning restore CA2119

    /// <summary>
    /// Creates and returns a new <typeparamref name="TComponent"/> instance.
    /// </summary>
    /// <param name="properties">
    /// The property bag sourced from the declarative configuration document for
    /// this component's configuration node.
    /// </param>
    /// <param name="serviceProvider">
    /// The SDK's <see cref="IServiceProvider"/>. Resolve
    /// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> from
    /// it to wire reloadable scalar values.
    /// </param>
    /// <returns>A fully-constructed <typeparamref name="TComponent"/>.</returns>
    public abstract TComponent CreateComponent(IDeclarativeConfigProperties properties, IServiceProvider serviceProvider);
}
