// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace OpenTelemetry.Trace;

/// <summary>
/// A <see cref="ParentBasedSampler"/> wrapper that disposes any disposable delegate
/// samplers (e.g. <see cref="ReloadingTraceIdRatioSampler"/>) when the tracer provider
/// is shut down.
/// </summary>
/// <remarks>
/// <see cref="TracerProviderSdk"/> only disposes the top-level sampler. When a
/// <see cref="ReloadingTraceIdRatioSampler"/> is nested as the root of a
/// <see cref="ParentBasedSampler"/>, its <c>IOptionsMonitor</c> subscription
/// would otherwise leak. This wrapper holds the constructed child samplers so it can
/// dispose them transitively, ensuring that reload subscriptions are always torn down.
/// </remarks>
internal sealed class DisposingParentBasedSampler : Sampler, IDisposable
{
    private readonly ParentBasedSampler inner;
    private readonly Sampler?[] delegateSamplers;

    internal DisposingParentBasedSampler(ParentBasedSampler inner, Sampler?[] delegateSamplers)
    {
        this.inner = inner;
        this.delegateSamplers = delegateSamplers;
        this.Description = inner.Description!;
    }

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
        => this.inner.ShouldSample(in samplingParameters);

    public void Dispose()
    {
        foreach (var sampler in this.delegateSamplers)
        {
            (sampler as IDisposable)?.Dispose();
        }
    }
}
