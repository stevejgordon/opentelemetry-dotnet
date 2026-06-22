// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#pragma warning disable OTEL1006 // IDeclarativeConfigProperties is experimental
#pragma warning disable OTEL1007 // SamplerPluginProvider is experimental

using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace OpenTelemetry.Trace.Tests;

public sealed class SamplerFactoryTests
{
    // -------------------------------------------------------------------------
    // AlwaysOnSamplerFactory
    // -------------------------------------------------------------------------

    [Fact]
    public void AlwaysOnSamplerFactory_Name_IsSchemaName()
        => Assert.Equal("always_on", new AlwaysOnSamplerFactory().Name);

    [Fact]
    public void AlwaysOnSamplerFactory_CreateComponent_ReturnsSameInstance()
    {
        var factory = new AlwaysOnSamplerFactory();

        var a = factory.CreateComponent(EmptyDeclarativeConfigProperties.Instance, NullServiceProvider.Instance);
        var b = factory.CreateComponent(EmptyDeclarativeConfigProperties.Instance, NullServiceProvider.Instance);

        Assert.IsType<AlwaysOnSampler>(a);
        Assert.Same(a, b);
    }

    // -------------------------------------------------------------------------
    // AlwaysOffSamplerFactory
    // -------------------------------------------------------------------------

    [Fact]
    public void AlwaysOffSamplerFactory_Name_IsSchemaName()
        => Assert.Equal("always_off", new AlwaysOffSamplerFactory().Name);

    [Fact]
    public void AlwaysOffSamplerFactory_CreateComponent_ReturnsSameInstance()
    {
        var factory = new AlwaysOffSamplerFactory();

        var a = factory.CreateComponent(EmptyDeclarativeConfigProperties.Instance, NullServiceProvider.Instance);
        var b = factory.CreateComponent(EmptyDeclarativeConfigProperties.Instance, NullServiceProvider.Instance);

        Assert.IsType<AlwaysOffSampler>(a);
        Assert.Same(a, b);
    }

    // -------------------------------------------------------------------------
    // TraceIdRatioBasedSamplerFactory
    // -------------------------------------------------------------------------

    [Fact]
    public void TraceIdRatioBasedSamplerFactory_Name_IsSchemaName()
        => Assert.Equal("trace_id_ratio_based", new TraceIdRatioBasedSamplerFactory().Name);

    [Fact]
    public void TraceIdRatioBasedSamplerFactory_CreateComponent_ReturnsReloadingSampler()
    {
        using var sp = BuildTraceIdRatioServiceProvider();
        var factory = new TraceIdRatioBasedSamplerFactory();

        using var sampler = Assert.IsType<ReloadingTraceIdRatioSampler>(
            factory.CreateComponent(EmptyDeclarativeConfigProperties.Instance, sp));

        Assert.NotNull(sampler);
    }

    [Fact]
    public void TraceIdRatioBasedSamplerFactory_CreateComponent_UsesOptionsMonitorRatioWhenPresent()
    {
        // Options-monitor path (declarative YAML or Configure<SamplerOptions>): SamplerArgument
        // has a value and the factory uses it, ignoring the properties bag.
        var monitor = new StubOptionsMonitor(new SamplerOptions { SamplerArgument = 0.25 });
        using var sp = BuildTraceIdRatioServiceProvider(monitor);
        var factory = new TraceIdRatioBasedSamplerFactory();
        var properties = SimpleDeclarativeConfigProperties.WithStringEntry("ratio", "0.99");

        using var sampler = (ReloadingTraceIdRatioSampler)factory.CreateComponent(properties, sp);

        Assert.Contains("0.25", sampler.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TraceIdRatioBasedSamplerFactory_CreateComponent_FallsBackToPropertiesWhenOptionsAbsent()
    {
        // Fallback path (env-var via SimpleDeclarativeConfigProperties): SamplerArgument is null
        // so the factory reads the ratio from the property bag.
        var monitor = new StubOptionsMonitor(new SamplerOptions { SamplerArgument = null });
        using var sp = BuildTraceIdRatioServiceProvider(monitor);
        var factory = new TraceIdRatioBasedSamplerFactory();
        var properties = SimpleDeclarativeConfigProperties.WithStringEntry("ratio", "0.5");

        using var sampler = (ReloadingTraceIdRatioSampler)factory.CreateComponent(properties, sp);

        Assert.Contains("0.5", sampler.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TraceIdRatioBasedSamplerFactory_CreateComponent_DefaultsToRatio1WhenNeitherPathProvides()
    {
        // When SamplerArgument is absent AND the properties bag has no ratio, the factory defaults to 1.0.
        var monitor = new StubOptionsMonitor(new SamplerOptions { SamplerArgument = null });
        using var sp = BuildTraceIdRatioServiceProvider(monitor);
        var factory = new TraceIdRatioBasedSamplerFactory();

        using var sampler = (ReloadingTraceIdRatioSampler)factory.CreateComponent(
            EmptyDeclarativeConfigProperties.Instance, sp);

        Assert.Equal(
            SamplingDecision.RecordAndSample,
            sampler.ShouldSample(MakeSamplingParameters()).Decision);
    }

    // -------------------------------------------------------------------------
    // ParentBasedSamplerFactory
    // -------------------------------------------------------------------------

    [Fact]
    public void ParentBasedSamplerFactory_Name_IsSchemaName()
        => Assert.Equal("parent_based", new ParentBasedSamplerFactory().Name);

    [Fact]
    public void ParentBasedSamplerFactory_CreateComponent_MissingRoot_Throws()
    {
        using var sp = BuildParentBasedServiceProvider();
        var factory = new ParentBasedSamplerFactory();

        Assert.Throws<InvalidOperationException>(() =>
            factory.CreateComponent(EmptyDeclarativeConfigProperties.Instance, sp));
    }

    [Fact]
    public void ParentBasedSamplerFactory_CreateComponent_RootOnly_ReturnsDisposingWrapper()
    {
        using var sp = BuildParentBasedServiceProvider();
        var factory = new ParentBasedSamplerFactory();
        var properties = MakeSingleDelegateProperties("root", AlwaysOnSamplerFactory.SchemaName);

        var sampler = factory.CreateComponent(properties, sp);
        try
        {
            Assert.IsType<DisposingParentBasedSampler>(sampler);
            Assert.Equal(
                SamplingDecision.RecordAndSample,
                sampler.ShouldSample(MakeSamplingParameters()).Decision);
        }
        finally
        {
            (sampler as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void ParentBasedSamplerFactory_CreateComponent_WrapperDisposesIDisposableChild()
    {
        // When the root is a ReloadingTraceIdRatioSampler (IDisposable), the wrapper must
        // dispose it when the wrapper itself is disposed.
        using var sp = BuildParentBasedServiceProvider();
        var factory = new ParentBasedSamplerFactory();
        var properties = MakeSingleDelegateProperties("root", TraceIdRatioBasedSamplerFactory.SchemaName);

        // The using statement calls Dispose; must not throw.
        using ((IDisposable)factory.CreateComponent(properties, sp))
        {
        }
    }

    [Fact]
    public void ParentBasedSamplerFactory_CreateComponent_MultipleEntriesInDelegatePluginNode_Throws()
    {
        // A delegate plugin node is an SDK extension single-entry map; more than one entry is invalid.
        using var sp = BuildParentBasedServiceProvider();
        var factory = new ParentBasedSamplerFactory();

        var ambiguousPluginNode = new MapConfigProperties(new Dictionary<string, IDeclarativeConfigProperties>
        {
            [AlwaysOnSamplerFactory.SchemaName] = EmptyDeclarativeConfigProperties.Instance,
            [AlwaysOffSamplerFactory.SchemaName] = EmptyDeclarativeConfigProperties.Instance,
        });
        var properties = new MapConfigProperties(new Dictionary<string, IDeclarativeConfigProperties>
        {
            ["root"] = ambiguousPluginNode,
        });

        Assert.Throws<InvalidOperationException>(() => factory.CreateComponent(properties, sp));
    }

    [Fact]
    public void ParentBasedSamplerFactory_CreateComponent_ExceptionInDelegate_PropagatesAndDoesNotHide()
    {
        // If a delegate factory throws, the exception must propagate cleanly.
        // The factory also disposes any IDisposable children already created before the failure.
        using var sp = BuildParentBasedServiceProvider(new ThrowingSamplerFactory());
        var factory = new ParentBasedSamplerFactory();

        // root = trace_id_ratio_based (succeeds, creates a disposable ReloadingTraceIdRatioSampler)
        // remote_parent_sampled = "throwing_sampler" (throws)
        var properties = new MapConfigProperties(new Dictionary<string, IDeclarativeConfigProperties>
        {
            ["root"] = MakePluginNode(TraceIdRatioBasedSamplerFactory.SchemaName),
            ["remote_parent_sampled"] = MakePluginNode(ThrowingSamplerFactory.SchemaNameConst),
        });

        Assert.Throws<InvalidOperationException>(() => factory.CreateComponent(properties, sp));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static SimpleServiceProvider BuildTraceIdRatioServiceProvider(
        StubOptionsMonitor? monitor = null)
    {
        return new SimpleServiceProvider()
            .Register<IOptionsMonitor<SamplerOptions>>(monitor ?? new StubOptionsMonitor(new SamplerOptions()));
    }

    private static SimpleServiceProvider BuildParentBasedServiceProvider(
        params SamplerPluginProvider[] extras)
    {
        IPluginComponentProvider[] providers =
        [
            new AlwaysOnSamplerFactory(),
            new AlwaysOffSamplerFactory(),
            new TraceIdRatioBasedSamplerFactory(),
            .. extras,
        ];

        var registry = new PluginComponentProviderRegistry(providers);

        return new SimpleServiceProvider()
            .Register<IOptionsMonitor<SamplerOptions>>(new StubOptionsMonitor(new SamplerOptions()))
            .Register(registry);
    }

    private static IDeclarativeConfigProperties MakeSingleDelegateProperties(
        string delegateKey, string pluginName)
    {
        var pluginNode = MakePluginNode(pluginName);
        return SimpleDeclarativeConfigProperties.WithNestedBag(delegateKey, pluginNode);
    }

    private static IDeclarativeConfigProperties MakePluginNode(string pluginName)
        => SimpleDeclarativeConfigProperties.WithNestedBag(
            pluginName, EmptyDeclarativeConfigProperties.Instance);

    private static SamplingParameters MakeSamplingParameters() =>
        new SamplingParameters(
            default,
            ActivityTraceId.CreateRandom(),
            "test-span",
            ActivityKind.Internal);

    // IOptionsMonitor stub that returns a fixed SamplerOptions for any name.
    private sealed class StubOptionsMonitor : IOptionsMonitor<SamplerOptions>
    {
        private readonly SamplerOptions value;

        public StubOptionsMonitor(SamplerOptions value)
        {
            this.value = value;
        }

        public SamplerOptions CurrentValue => this.value;

        public SamplerOptions Get(string? name) => this.value;

        public IDisposable? OnChange(Action<SamplerOptions, string?> listener) => null;
    }

    // Minimal IServiceProvider backed by a type-keyed dictionary.
    private sealed class SimpleServiceProvider : IServiceProvider, IDisposable
    {
        private readonly Dictionary<Type, object> services = new();

        public SimpleServiceProvider Register<T>(T instance)
            where T : notnull
        {
            this.services[typeof(T)] = instance;
            return this;
        }

        public object? GetService(Type serviceType)
        {
            return this.services.TryGetValue(serviceType, out var v) ? v : null;
        }

        public void Dispose()
        {
            foreach (var v in this.services.Values)
            {
                (v as IDisposable)?.Dispose();
            }
        }
    }

    // Trivial IServiceProvider for factories that make no service lookups.
    private sealed class NullServiceProvider : IServiceProvider
    {
        internal static readonly NullServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }

    // Multi-key IDeclarativeConfigProperties for test scenarios that need more than one entry.
    private sealed class MapConfigProperties : IDeclarativeConfigProperties
    {
        private readonly Dictionary<string, IDeclarativeConfigProperties> map;

        public MapConfigProperties(Dictionary<string, IDeclarativeConfigProperties> map)
        {
            this.map = map;
        }

        public IEnumerable<string> GetKeys() => this.map.Keys;

        public bool TryGetString(string key, out string? value)
        {
            value = null;
            return false;
        }

        public bool TryGetBool(string key, out bool? value)
        {
            value = null;
            return false;
        }

        public bool TryGetInt(string key, out int? value)
        {
            value = null;
            return false;
        }

        public bool TryGetLong(string key, out long? value)
        {
            value = null;
            return false;
        }

        public bool TryGetDouble(string key, out double? value)
        {
            value = null;
            return false;
        }

        public IDeclarativeConfigProperties? GetProperties(string key)
            => this.map.TryGetValue(key, out var v) ? v : null;

        public IReadOnlyList<IDeclarativeConfigProperties>? GetPropertiesList(string key) => null;
    }

    // Factory stub used to test exception-during-construction handling.
    private sealed class ThrowingSamplerFactory : SamplerPluginProvider
    {
        internal const string SchemaNameConst = "throwing_sampler";

        public override string Name => SchemaNameConst;

        public override Sampler CreateComponent(
            IDeclarativeConfigProperties properties, IServiceProvider serviceProvider)
            => throw new InvalidOperationException("deliberate construction failure");
    }
}
