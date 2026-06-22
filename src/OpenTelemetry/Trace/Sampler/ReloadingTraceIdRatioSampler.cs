// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;

namespace OpenTelemetry.Trace;

/// <summary>
/// A <see cref="TraceIdRatioBasedSampler"/> wrapper that reloads the sampling ratio when
/// the named <see cref="SamplerOptions"/> changes via <see cref="IOptionsMonitor{T}"/>.
/// </summary>
/// <remarks>
/// The sampling rate can change at runtime without rebuilding the tracer provider.
/// Structural configuration (which sampler type to use) remains fixed at construction
/// time; only the ratio value reloads. Only reloads when <see cref="SamplerOptions.SamplerArgument"/>
/// is non-null; absent or invalid values leave the current state unchanged.
/// Implements <see cref="IDisposable"/> so that the <see cref="IOptionsMonitor{T}"/>
/// subscription is torn down when the owning <see cref="TracerProvider"/> is disposed.
/// </remarks>
internal sealed class ReloadingTraceIdRatioSampler : Sampler, IDisposable
{
    private readonly string optionsName;
    private readonly IDisposable? subscription;

    // Immutable state bundle published via a single volatile reference.
    // This is the canonical BCL pattern (e.g. ConcurrentDictionary._tables):
    // swapping the reference is pointer-sized and therefore atomic on all platforms;
    // once a reader has the reference, all readonly fields are fully visible because
    // the CLR guarantees publication safety for readonly fields of a class.
    // The volatile qualifier provides the acquire/release fence required on ARM64
    // so the new State is immediately visible to all cores after a write.
    //
    // Sampler.Description has no virtual getter, so it cannot be backed by this
    // volatile field. We write to it via the non-virtual protected setter on each
    // reload. Reads of Description may briefly lag on ARM64, which is acceptable
    // because Description is a diagnostic property only; ShouldSample always
    // uses the volatile state and is never affected.
    private volatile State state;

    internal ReloadingTraceIdRatioSampler(
        IOptionsMonitor<SamplerOptions> monitor,
        string optionsName,
        double initialRatio)
    {
        this.optionsName = optionsName;
        this.state = new State(initialRatio);
        this.Description = this.state.Description;
        this.subscription = monitor.OnChange(this.OnOptionsChange);
    }

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
        => this.state.Inner.ShouldSample(in samplingParameters);

    public void Dispose() => this.subscription?.Dispose();

    private void OnOptionsChange(SamplerOptions options, string? name)
    {
        if (!string.Equals(name, this.optionsName, StringComparison.Ordinal))
        {
            return;
        }

        // Only reload when there is an explicit ratio value. A null SamplerArgument means
        // the key was absent or invalid in the configuration; in that case, keep the current
        // state rather than silently resetting to the 1.0 default. This prevents an unrelated
        // configuration reload (e.g. appsettings.json changing) from corrupting the ratio when
        // no declarative sampler key is projected.
        if (!options.SamplerArgument.HasValue)
        {
            return;
        }

        var newRatio = options.SamplerArgument.Value;

        // Skip the allocation when the ratio has not actually changed. Any config
        // change fires all IOptionsChangeTokenSource listeners, so this guard
        // prevents churn on unrelated reloads.
        if (newRatio == this.state.Ratio)
        {
            return;
        }

        var newState = new State(newRatio);
        this.state = newState;

        // KNOWN LIMITATION: Description is a non-virtual base class property whose
        // backing field cannot be made volatile from a derived type. On ARM64, the
        // relaxed store below may not be immediately visible to all cores, meaning
        // a concurrent reader could briefly see the old description string after the
        // volatile state swap above has already published the new Inner sampler.
        //
        // ShouldSample is never affected - it reads only from the volatile state field.
        // Description is diagnostic-only; a momentary stale value is benign.
        //
        // The clean fix is to make Sampler.Description virtual so derived types can
        // back it with a volatile field. That change is deferred because making a
        // non-virtual member virtual is a source-breaking change for any subclass that
        // hides Description with its own property (CS0108). Revisit when the next major
        // version allows a deliberate API change.
        this.Description = newState.Description;
    }

    // Immutable value bundle. All fields are readonly so they are fully visible
    // to any thread that reads the volatile state reference.
    private sealed class State
    {
        internal readonly double Ratio;
        internal readonly TraceIdRatioBasedSampler Inner;
        internal readonly string Description;

        internal State(double ratio)
        {
            this.Ratio = ratio;
            this.Inner = new TraceIdRatioBasedSampler(ratio);
            this.Description = this.Inner.Description ?? nameof(ReloadingTraceIdRatioSampler);
        }
    }
}
