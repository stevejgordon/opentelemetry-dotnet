// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace OpenTelemetry.Configuration.Declarative;

/// <summary>
/// Typed in-memory model for the <c>tracer_provider</c> section of a declarative configuration document.
/// </summary>
internal sealed class TracerProviderConfiguration
{
    /// <summary>Gets the sampler configuration.</summary>
    internal ConfigProperty<SamplerConfiguration> Sampler { get; init; }
}
