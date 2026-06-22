// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace OpenTelemetry.Trace;

/// <summary>
/// Options for configuring the trace sampler.
/// </summary>
/// <remarks>
/// <para>
/// Values are sourced from <c>OTEL_TRACES_SAMPLER</c> and
/// <c>OTEL_TRACES_SAMPLER_ARG</c> by default. Named options prefixed with
/// <c>opentelemetry:</c> read from the canonical
/// <c>opentelemetry:traces:sampler:arg</c> key in
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>, which any
/// reload-capable source (declarative YAML, telemetry policy provider, etc.)
/// can write to in order to drive a runtime ratio update.
/// </para>
/// <para>
/// Use <c>services.Configure&lt;SamplerOptions&gt;</c> for programmatic
/// overrides; <see cref="Microsoft.Extensions.Options.IConfigureOptions{T}"/>
/// callbacks run after the factory sets the env-var or declarative defaults, so
/// they take precedence over both.
/// </para>
/// </remarks>
public sealed class SamplerOptions
{
    /// <summary>Gets or sets the sampler type name.</summary>
    /// <remarks>
    /// Recognized values (case-insensitive): <c>always_on</c>,
    /// <c>always_off</c>, <c>traceidratio</c>, <c>parentbased_always_on</c>,
    /// <c>parentbased_always_off</c>, <c>parentbased_traceidratio</c>.
    /// For the declarative (YAML) path, schema names are used instead
    /// (<c>always_on</c>, <c>trace_id_ratio_based</c>, <c>parent_based</c>,
    /// etc.). Unknown values fall back to the default sampler.
    /// </remarks>
    public string? SamplerType { get; set; }

    /// <summary>
    /// Gets or sets the sampler argument (e.g. the sample ratio for
    /// ratio-based samplers). Must be in [0, 1]; values outside that range
    /// are treated as absent and the default ratio of 1.0 is used.
    /// </summary>
    public double? SamplerArgument { get; set; }

    /// <summary>Gets or sets the raw string value of the sampler argument before numeric parsing.</summary>
    internal string? SamplerArgumentRaw { get; set; }

    /// <summary>
    /// Attempts to parse <paramref name="raw"/> as a sampler ratio in [0, 1].
    /// Returns <see langword="null"/> for absent, invalid, NaN, infinite, or
    /// out-of-range inputs.
    /// </summary>
    /// <param name="raw">The raw string value to parse, or <see langword="null"/>.</param>
    /// <returns>The parsed ratio in [0, 1], or <see langword="null"/> if the input is absent or invalid.</returns>
    internal static double? TryParseRatio(string? raw)
    {
        if (raw == null)
        {
            return null;
        }

        if (double.TryParse(
                raw,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var ratio)
            && !double.IsNaN(ratio)
            && !double.IsInfinity(ratio)
            && ratio >= 0.0
            && ratio <= 1.0)
        {
            return ratio;
        }

        return null;
    }
}
