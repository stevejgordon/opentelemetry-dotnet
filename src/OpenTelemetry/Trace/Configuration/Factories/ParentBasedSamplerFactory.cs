// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#pragma warning disable OTEL1007 // SamplerPluginProvider is experimental; this internal class is part of that surface

using Microsoft.Extensions.DependencyInjection;

namespace OpenTelemetry.Trace;

internal sealed class ParentBasedSamplerFactory : SamplerPluginProvider
{
    internal const string SchemaName = "parent_based";

    public override string Name => SchemaName;

    public override Sampler CreateComponent(IDeclarativeConfigProperties properties, IServiceProvider serviceProvider)
    {
        var registry = serviceProvider.GetRequiredService<PluginComponentProviderRegistry>();

        Sampler? root = null, remoteParentSampled = null, remoteParentNotSampled = null,
            localParentSampled = null, localParentNotSampled = null;
        try
        {
            root = CreateDelegateSampler("root", properties, registry, serviceProvider) ?? throw new InvalidOperationException(
                    "parent_based sampler requires a 'root' delegate sampler but none was specified in the declarative configuration.");

            remoteParentSampled = CreateDelegateSampler("remote_parent_sampled", properties, registry, serviceProvider);
            remoteParentNotSampled = CreateDelegateSampler("remote_parent_not_sampled", properties, registry, serviceProvider);
            localParentSampled = CreateDelegateSampler("local_parent_sampled", properties, registry, serviceProvider);
            localParentNotSampled = CreateDelegateSampler("local_parent_not_sampled", properties, registry, serviceProvider);

            // DisposingParentBasedSampler wraps the inner sampler and disposes the constructed
            // child samplers (e.g. ReloadingTraceIdRatioSampler) when the tracer provider shuts down.
            // TracerProviderSdk only disposes the top-level sampler, so this wrapper ensures that
            // IOptionsMonitor subscriptions held by nested samplers are always torn down.
            var inner = new ParentBasedSampler(root, remoteParentSampled, remoteParentNotSampled, localParentSampled, localParentNotSampled);
            return new DisposingParentBasedSampler(inner, [root, remoteParentSampled, remoteParentNotSampled, localParentSampled, localParentNotSampled]);
        }
        catch
        {
            // CA1508 false positive: these variables are Sampler? and may be null if the
            // exception was thrown before their assignments in the try block.
#pragma warning disable CA1508
            (root as IDisposable)?.Dispose();
            (remoteParentSampled as IDisposable)?.Dispose();
            (remoteParentNotSampled as IDisposable)?.Dispose();
            (localParentSampled as IDisposable)?.Dispose();
            (localParentNotSampled as IDisposable)?.Dispose();
#pragma warning restore CA1508
            throw;
        }
    }

    // Each delegate key holds an SDK extension plugin node: a single-entry map whose key is the sampler
    // plugin name and whose value is the inner configuration properties for that plugin. The pattern
    // mirrors the YAML structure: e.g. { "root": { "trace_id_ratio_based": { "ratio": 0.25 } } }.
    private static Sampler? CreateDelegateSampler(
        string delegateKey,
        IDeclarativeConfigProperties properties,
        PluginComponentProviderRegistry registry,
        IServiceProvider serviceProvider)
    {
        var pluginProperties = properties.GetProperties(delegateKey);
        if (pluginProperties == null)
        {
            return null;
        }

        string? pluginName = null;
        foreach (var key in pluginProperties.GetKeys())
        {
            if (pluginName != null)
            {
                throw new InvalidOperationException(
                    $"parent_based delegate '{delegateKey}' has multiple entries in the SDK extension plugin node; exactly one is required.");
            }

            pluginName = key;
        }

        if (pluginName == null)
        {
            return null;
        }

        var innerProperties = pluginProperties.GetProperties(pluginName) ?? EmptyDeclarativeConfigProperties.Instance;
        return registry.Create<Sampler>(pluginName, innerProperties, serviceProvider);
    }
}
