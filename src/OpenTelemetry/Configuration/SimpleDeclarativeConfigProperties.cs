// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#pragma warning disable OTEL1007 // IDeclarativeConfigProperties is experimental; used internally

using System.Globalization;

namespace OpenTelemetry;

/// <summary>
/// Minimal dictionary-backed <see cref="IDeclarativeConfigProperties"/> for the env-var sampler
/// adapter only. All values stored in this bag are pre-validated by the caller before insertion;
/// parse-failure paths in the typed accessors (<c>TryGetBool</c>, <c>TryGetDouble</c>, etc.) are
/// therefore unreachable in practice and no diagnostic events are emitted for them. Property lists
/// (<see cref="GetPropertiesList"/>) are not supported; callers that need those must use
/// <c>DeclarativeConfigProperties</c> instead.
/// </summary>
internal sealed class SimpleDeclarativeConfigProperties : IDeclarativeConfigProperties
{
    private readonly Dictionary<string, object?> map;

    private SimpleDeclarativeConfigProperties(Dictionary<string, object?> map)
    {
        this.map = map;
    }

    public IEnumerable<string> GetKeys() => this.map.Keys;

    public bool TryGetString(string key, out string? value)
    {
        if (!this.map.TryGetValue(key, out var v))
        {
            value = null;
            return false;
        }

        value = v as string;
        return true;
    }

    public bool TryGetBool(string key, out bool? value)
    {
        if (!this.map.TryGetValue(key, out var v))
        {
            value = null;
            return false;
        }

        value = v is string s && bool.TryParse(s, out var b) ? b : null;
        return true;
    }

    public bool TryGetInt(string key, out int? value)
    {
        if (!this.map.TryGetValue(key, out var v))
        {
            value = null;
            return false;
        }

        value = v is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
        return true;
    }

    public bool TryGetLong(string key, out long? value)
    {
        if (!this.map.TryGetValue(key, out var v))
        {
            value = null;
            return false;
        }

        value = v is string s && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : null;
        return true;
    }

    public bool TryGetDouble(string key, out double? value)
    {
        if (!this.map.TryGetValue(key, out var v))
        {
            value = null;
            return false;
        }

        value = v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        return true;
    }

    public IDeclarativeConfigProperties? GetProperties(string key)
    {
        if (!this.map.TryGetValue(key, out var v))
        {
            return null;
        }

        return v as IDeclarativeConfigProperties;
    }

    public IReadOnlyList<IDeclarativeConfigProperties>? GetPropertiesList(string key) => null;

    internal static IDeclarativeConfigProperties WithStringEntry(string key, string? value)
        => new SimpleDeclarativeConfigProperties(
            new Dictionary<string, object?>(StringComparer.Ordinal) { [key] = value });

    internal static IDeclarativeConfigProperties WithNestedBag(string key, IDeclarativeConfigProperties bag)
        => new SimpleDeclarativeConfigProperties(
            new Dictionary<string, object?>(StringComparer.Ordinal) { [key] = bag });
}
