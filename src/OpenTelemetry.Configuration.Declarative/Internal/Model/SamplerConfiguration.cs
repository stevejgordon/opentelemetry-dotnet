// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#pragma warning disable OTEL1007 // IDeclarativeConfigProperties is experimental; used internally

using OpenTelemetry;

namespace OpenTelemetry.Configuration.Declarative;

/// <summary>
/// Typed model for the <c>tracer_provider.sampler</c> SDK extension plugin node.
/// </summary>
/// <remarks>
/// The sampler node uses the OTel declarative-config SDK extension plugin pattern: a single-key mapping
/// where the key is the plugin name and the value is the inner configuration mapping.
/// Example: <c>{ "trace_id_ratio_based": { "ratio": 0.25 } }</c>.
/// </remarks>
internal sealed class SamplerConfiguration
{
    /// <summary>
    /// Gets a sentinel value representing an absent or unresolvable SDK extension plugin node.
    /// The parser now throws on malformed nodes, so this is only reachable via defensive code paths.
    /// </summary>
    internal static readonly SamplerConfiguration Empty = new() { PluginName = string.Empty, Properties = EmptyDeclarativeConfigProperties.Instance };

    /// <summary>Gets the schema-level sampler plugin name (e.g. <c>"trace_id_ratio_based"</c>).</summary>
    internal string PluginName { get; init; } = string.Empty;

    /// <summary>Gets the inner property bag for the sampler plugin.</summary>
    internal IDeclarativeConfigProperties Properties { get; init; } = EmptyDeclarativeConfigProperties.Instance;

    /// <summary>
    /// Gets a value indicating whether this configuration represents a valid sampler selection.
    /// Returns <see langword="false"/> for the <see cref="Empty"/> sentinel (malformed or absent plugin node).
    /// </summary>
    internal bool IsValid => this.PluginName.Length > 0;
}
