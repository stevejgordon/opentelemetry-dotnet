// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;

namespace OpenTelemetry.Trace.Tests;

public sealed class DisposingParentBasedSamplerTests
{
    [Fact]
    public void Dispose_CallsDisposeOnAllIDisposableChildren()
    {
#pragma warning disable CA2000 // Ownership of a and b transfers to wrapper; wrapper.Dispose() handles their cleanup
        var a = new TrackingDisposableSampler();
        var b = new TrackingDisposableSampler();
        var inner = new ParentBasedSampler(AlwaysOnSampler.Instance);
        var wrapper = new DisposingParentBasedSampler(inner, [a, b]);
#pragma warning restore CA2000

        wrapper.Dispose();

        Assert.Equal(1, a.DisposeCount);
        Assert.Equal(1, b.DisposeCount);
    }

    [Fact]
    public void Dispose_IgnoresNullChildren()
    {
        var inner = new ParentBasedSampler(AlwaysOnSampler.Instance);

        // using disposes the wrapper; must not throw NullReferenceException.
        using var wrapper = new DisposingParentBasedSampler(inner, [null, null]);
    }

    [Fact]
    public void Dispose_IgnoresNonDisposableChildren()
    {
        var inner = new ParentBasedSampler(AlwaysOnSampler.Instance);

        // AlwaysOnSampler does not implement IDisposable; using must not throw.
        using var wrapper = new DisposingParentBasedSampler(inner, [AlwaysOnSampler.Instance]);
    }

    [Fact]
    public void Dispose_MixedChildren_OnlyDisposesIDisposableOnes()
    {
#pragma warning disable CA2000 // Ownership of trackable transfers to wrapper
        var trackable = new TrackingDisposableSampler();
        var inner = new ParentBasedSampler(AlwaysOnSampler.Instance);
        var wrapper = new DisposingParentBasedSampler(inner, [AlwaysOnSampler.Instance, trackable, null]);
#pragma warning restore CA2000

        wrapper.Dispose();

        Assert.Equal(1, trackable.DisposeCount);
    }

    [Fact]
    public void ShouldSample_DelegatesToInner()
    {
        var inner = new ParentBasedSampler(AlwaysOnSampler.Instance);
        using var wrapper = new DisposingParentBasedSampler(inner, []);

        var result = wrapper.ShouldSample(MakeParameters());

        Assert.Equal(SamplingDecision.RecordAndSample, result.Decision);
    }

    [Fact]
    public void Description_MatchesInnerAtConstruction()
    {
        // DisposingParentBasedSampler captures the inner description once at construction;
        // it is not dynamically re-read on each call.
        var inner = new ParentBasedSampler(AlwaysOnSampler.Instance);
        using var wrapper = new DisposingParentBasedSampler(inner, []);

        Assert.Equal(inner.Description, wrapper.Description);
    }

    private static SamplingParameters MakeParameters() =>
        new SamplingParameters(
            default,
            ActivityTraceId.CreateRandom(),
            "test-span",
            ActivityKind.Internal);

    private sealed class TrackingDisposableSampler : Sampler, IDisposable
    {
        public int DisposeCount { get; private set; }

        public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
            => new SamplingResult(SamplingDecision.RecordAndSample);

        public void Dispose() => this.DisposeCount++;
    }
}
