// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace OpenTelemetry.Trace.Tests;

public sealed class ReloadingTraceIdRatioSamplerTests
{
    [Fact]
    public void ShouldSample_Ratio0_ReturnsDropDecision()
    {
        var monitor = new TestOptionsMonitor();
        using var sampler = new ReloadingTraceIdRatioSampler(monitor, "opts", 0.0);

        Assert.Equal(SamplingDecision.Drop, sampler.ShouldSample(MakeParameters()).Decision);
    }

    [Fact]
    public void ShouldSample_Ratio1_ReturnsRecordAndSampledDecision()
    {
        var monitor = new TestOptionsMonitor();
        using var sampler = new ReloadingTraceIdRatioSampler(monitor, "opts", 1.0);

        Assert.Equal(SamplingDecision.RecordAndSample, sampler.ShouldSample(MakeParameters()).Decision);
    }

    [Fact]
    public void OnChange_SameOptionsName_UpdatesRatio()
    {
        var monitor = new TestOptionsMonitor();
        using var sampler = new ReloadingTraceIdRatioSampler(monitor, "opts", 0.0);

        Assert.Equal(SamplingDecision.Drop, sampler.ShouldSample(MakeParameters()).Decision);

        monitor.Raise("opts", new SamplerOptions { SamplerArgument = 1.0 });

        Assert.Equal(SamplingDecision.RecordAndSample, sampler.ShouldSample(MakeParameters()).Decision);
    }

    [Fact]
    public void OnChange_DifferentOptionsName_DoesNotUpdateRatio()
    {
        var monitor = new TestOptionsMonitor();
        using var sampler = new ReloadingTraceIdRatioSampler(monitor, "opts", 0.0);

        monitor.Raise("other", new SamplerOptions { SamplerArgument = 1.0 });

        Assert.Equal(SamplingDecision.Drop, sampler.ShouldSample(MakeParameters()).Decision);
    }

    [Fact]
    public void OnChange_NullSamplerArgument_DoesNotUpdateRatio()
    {
        var monitor = new TestOptionsMonitor();
        using var sampler = new ReloadingTraceIdRatioSampler(monitor, "opts", 0.0);

        monitor.Raise("opts", new SamplerOptions { SamplerArgument = null });

        Assert.Equal(SamplingDecision.Drop, sampler.ShouldSample(MakeParameters()).Decision);
    }

    [Fact]
    public void Dispose_UnsubscribesFromMonitor()
    {
        var monitor = new TestOptionsMonitor();
        var sampler = new ReloadingTraceIdRatioSampler(monitor, "opts", 0.0);

        Assert.Equal(1, monitor.ListenerCount);
        sampler.Dispose();
        Assert.Equal(0, monitor.ListenerCount);
    }

    [Fact]
    public void Description_UpdatesOnReload()
    {
        var monitor = new TestOptionsMonitor();
        using var sampler = new ReloadingTraceIdRatioSampler(monitor, "opts", 0.25);

        Assert.Contains("0.25", sampler.Description, StringComparison.OrdinalIgnoreCase);

        monitor.Raise("opts", new SamplerOptions { SamplerArgument = 0.75 });

        Assert.Contains("0.75", sampler.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Description_UpdatesAfterReload()
    {
        // After a reload completes, both Description and ShouldSample must reflect
        // the new ratio. Description is written after the volatile state swap, so
        // both are always up-to-date once OnOptionsChange returns.
        var monitor = new TestOptionsMonitor();
        using var sampler = new ReloadingTraceIdRatioSampler(monitor, "opts", 0.0);

        monitor.Raise("opts", new SamplerOptions { SamplerArgument = 1.0 });

        Assert.Contains("1", sampler.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SamplingDecision.RecordAndSample, sampler.ShouldSample(MakeParameters()).Decision);
    }

    [Fact]
    public void OnChange_SameRatio_DoesNotReplaceState()
    {
        // When the ratio in the new options matches the current one, no new State
        // is allocated. We verify this indirectly: the Description object reference
        // must be the same instance before and after a no-op reload.
        var monitor = new TestOptionsMonitor();
        using var sampler = new ReloadingTraceIdRatioSampler(monitor, "opts", 0.5);

        var descriptionBefore = sampler.Description;
        monitor.Raise("opts", new SamplerOptions { SamplerArgument = 0.5 });
        var descriptionAfter = sampler.Description;

        Assert.Same(descriptionBefore, descriptionAfter);
    }

    private static SamplingParameters MakeParameters() =>
        new SamplingParameters(
            default,
            ActivityTraceId.CreateRandom(),
            "test-span",
            ActivityKind.Internal);

    private sealed class TestOptionsMonitor : IOptionsMonitor<SamplerOptions>
    {
        private readonly List<Action<SamplerOptions, string?>> listeners = [];

        public SamplerOptions CurrentValue => new SamplerOptions();

        public int ListenerCount => this.listeners.Count;

        public SamplerOptions Get(string? name) => new SamplerOptions();

        public IDisposable? OnChange(Action<SamplerOptions, string?> listener)
        {
            this.listeners.Add(listener);
            return new Unsubscriber(() => this.listeners.Remove(listener));
        }

        public void Raise(string name, SamplerOptions options)
        {
            foreach (var l in this.listeners.ToArray())
            {
                l(options, name);
            }
        }

        private sealed class Unsubscriber(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }
}
