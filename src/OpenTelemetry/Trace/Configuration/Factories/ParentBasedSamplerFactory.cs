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

        var root = CreateDelegateSampler("root", properties, registry, serviceProvider) ?? throw new InvalidOperationException(
                "parent_based sampler requires a 'root' delegate sampler but none was specified in the declarative configuration.");

        var remoteParentSampled = CreateDelegateSampler("remote_parent_sampled", properties, registry, serviceProvider);
        var remoteParentNotSampled = CreateDelegateSampler("remote_parent_not_sampled", properties, registry, serviceProvider);
        var localParentSampled = CreateDelegateSampler("local_parent_sampled", properties, registry, serviceProvider);
        var localParentNotSampled = CreateDelegateSampler("local_parent_not_sampled", properties, registry, serviceProvider);

        return new ParentBasedSampler(root, remoteParentSampled, remoteParentNotSampled, localParentSampled, localParentNotSampled);
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
