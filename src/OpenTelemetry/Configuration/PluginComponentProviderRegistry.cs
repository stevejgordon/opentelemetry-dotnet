// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#pragma warning disable OTEL1007 // IDeclarativeConfigProperties is experimental; this internal class is part of that surface

using System.Text;

namespace OpenTelemetry;

/// <summary>
/// Immutable registry of <see cref="IPluginComponentProvider"/> instances, indexed by
/// (<see cref="Type"/>, <see cref="string"/> name). Built once from DI's
/// <see cref="IEnumerable{IPluginComponentProvider}"/> at provider-construction time.
/// </summary>
internal sealed class PluginComponentProviderRegistry
{
    private readonly Dictionary<ComponentKey, IPluginComponentProvider> providers;

    public PluginComponentProviderRegistry(IEnumerable<IPluginComponentProvider> providers)
    {
        var dictionary = new Dictionary<ComponentKey, IPluginComponentProvider>();

        foreach (var provider in providers)
        {
            var key = new ComponentKey(provider.ComponentType, provider.Name);
            if (dictionary.TryGetValue(key, out var existing))
            {
                throw new InvalidOperationException(
                    $"Duplicate component factory registration for ({provider.ComponentType.Name}, \"{provider.Name}\"): " +
                    $"'{existing.GetType().FullName}' and '{provider.GetType().FullName}'.");
            }

            dictionary[key] = provider;
        }

        this.providers = dictionary;
    }

    /// <summary>
    /// Creates a component of type <typeparamref name="TComponent"/> with the
    /// given <paramref name="name"/>. Throws on miss with a diagnostic message.
    /// </summary>
    /// <typeparam name="TComponent">The component type to create.</typeparam>
    /// <param name="name">The schema-level name that selects the factory.</param>
    /// <param name="properties">The declarative configuration property bag.</param>
    /// <param name="serviceProvider">The SDK's <see cref="IServiceProvider"/>.</param>
    /// <returns>The constructed component.</returns>
    public TComponent Create<TComponent>(
        string name,
        IDeclarativeConfigProperties properties,
        IServiceProvider serviceProvider)
        where TComponent : class =>
            this.providers.TryGetValue(new ComponentKey(typeof(TComponent), name), out var provider)
                ? (TComponent)provider.Create(properties, serviceProvider)
                : throw new InvalidOperationException(BuildMissMessage<TComponent>(name, this.providers));

    /// <summary>
    /// Tries to create a component of type <typeparamref name="TComponent"/> with
    /// the given <paramref name="name"/>. Returns <see langword="false"/> only when no
    /// factory is registered for the given (<typeparamref name="TComponent"/>, <paramref name="name"/>)
    /// pair. If a factory is found but its
    /// <see cref="PluginComponentProvider{TComponent}.CreateComponent"/> throws, that exception
    /// propagates to the caller unchanged.
    /// </summary>
    /// <typeparam name="TComponent">The component type to create.</typeparam>
    /// <param name="name">The schema-level name that selects the factory.</param>
    /// <param name="properties">The declarative configuration property bag.</param>
    /// <param name="serviceProvider">The SDK's <see cref="IServiceProvider"/>.</param>
    /// <param name="component">The constructed component, or <see langword="null"/> on miss.</param>
    /// <returns><see langword="true"/> if a factory was found and the component was created.</returns>
    public bool TryCreate<TComponent>(
        string name,
        IDeclarativeConfigProperties properties,
        IServiceProvider serviceProvider,
        out TComponent? component)
        where TComponent : class
    {
        if (this.providers.TryGetValue(new ComponentKey(typeof(TComponent), name), out var provider))
        {
            component = (TComponent)provider.Create(properties, serviceProvider);
            return true;
        }

        component = null;
        return false;
    }

#pragma warning disable CA1305 // All interpolated values are type names and string literals; not locale-sensitive
    private static string BuildMissMessage<TComponent>(
        string name,
        Dictionary<ComponentKey, IPluginComponentProvider> providers)
    {
        var typeName = typeof(TComponent).Name;
        var available = providers.Keys
            .Where(k => k.ComponentType == typeof(TComponent))
            .Select(k => k.Name)
            .ToList();

        var sb = new StringBuilder();
        sb.Append($"No {typeName} factory named \"{name}\" is registered.");

        if (available.Count > 0)
        {
            string? suggestion = null;
            var minDist = int.MaxValue;
            foreach (var candidate in available)
            {
                var dist = LevenshteinDistance(name, candidate);
                if (dist < minDist)
                {
                    minDist = dist;
                    suggestion = candidate;
                }
            }

            if (minDist <= 2 && suggestion != null)
            {
                sb.Append($" Did you mean \"{suggestion}\"?");
            }
            else
            {
                sb.Append($" Available: {string.Join(", ", available.Select(n => $"\"{n}\""))}.");
            }
        }
        else
        {
            sb.Append($" No {typeName} factories are registered.");
        }

        return sb.ToString();
    }
#pragma warning restore CA1305

    // O(n*m) edit distance using two rolling rows; error path only so allocation is acceptable.
    private static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }

    private readonly record struct ComponentKey(Type ComponentType, string Name);
}
