// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#pragma warning disable OTEL1007 // IDeclarativeConfigProperties is experimental; this internal interface is part of that surface

namespace OpenTelemetry;

/// <summary>
/// Type-erased plugin component provider, keyed by (<see cref="ComponentType"/>, <see cref="Name"/>).
/// Internal: consumers use the typed <see cref="PluginComponentProvider{TComponent}"/> surface;
/// only <see cref="PluginComponentProviderRegistry"/> and registration helpers interact with this type.
/// </summary>
internal interface IPluginComponentProvider
{
    /// <summary>Gets the CLR type of the component this provider creates.</summary>
    Type ComponentType { get; }

    /// <summary>Gets the schema-level name that selects this provider (case-sensitive).</summary>
    string Name { get; }

    /// <summary>Creates a component instance, returning it as <see cref="object"/>.</summary>
    /// <param name="properties">The declarative configuration property bag for this component node.</param>
    /// <param name="serviceProvider">The SDK's <see cref="IServiceProvider"/>.</param>
    /// <returns>The constructed component, cast to <see cref="object"/>.</returns>
    object Create(IDeclarativeConfigProperties properties, IServiceProvider serviceProvider);
}
