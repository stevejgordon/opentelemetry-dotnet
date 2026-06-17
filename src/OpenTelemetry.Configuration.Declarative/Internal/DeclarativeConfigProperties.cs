// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#pragma warning disable OTEL1007 // IDeclarativeConfigProperties is experimental; used internally

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using OpenTelemetry;

namespace OpenTelemetry.Configuration.Declarative;

/// <summary>
/// Dictionary-backed <see cref="IDeclarativeConfigProperties"/> implementation used by the
/// YAML parser to pass per-component configuration to component factories.
/// </summary>
/// <remarks>
/// <para>
/// Values stored in the dictionary:
/// <list type="bullet">
///   <item><description><see langword="null"/> — present-null (key is present; value was null in the source).</description></item>
///   <item><description><see cref="string"/> — present scalar; parsed on demand by each <c>TryGet*</c> method.</description></item>
///   <item><description><see cref="DeclarativeConfigProperties"/> — nested properties.</description></item>
///   <item><description><see cref="IReadOnlyList{IDeclarativeConfigProperties}"/> — list of nested properties.</description></item>
/// </list>
/// Absent keys are not in the dictionary at all.
/// </para>
/// </remarks>
internal sealed class DeclarativeConfigProperties : IDeclarativeConfigProperties
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyEntries =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.Ordinal));

    private readonly IReadOnlyDictionary<string, object?> entries;

    private DeclarativeConfigProperties(IReadOnlyDictionary<string, object?> entries)
    {
        this.entries = entries;
    }

    internal static DeclarativeConfigProperties Empty { get; } = new(EmptyEntries);

    public IEnumerable<string> GetKeys() => this.entries.Keys;

    public bool TryGetString(string key, out string? value)
    {
        if (!this.entries.TryGetValue(key, out var v))
        {
            value = null;
            return false;
        }

        value = v as string; // null for present-null or non-string value
        return true;
    }

    public bool TryGetBool(string key, out bool? value)
    {
        if (!this.entries.TryGetValue(key, out var v))
        {
            value = null;
            return false;
        }

        if (v is null)
        {
            value = null;
            return true;
        }

        if (v is string s && bool.TryParse(s, out var b))
        {
            value = b;
            return true;
        }

        value = null;
        return true;
    }

    public bool TryGetInt(string key, out int? value)
    {
        if (!this.entries.TryGetValue(key, out var v))
        {
            value = null;
            return false;
        }

        if (v is null)
        {
            value = null;
            return true;
        }

        if (v is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
        {
            value = i;
            return true;
        }

        value = null;
        return true;
    }

    public bool TryGetLong(string key, out long? value)
    {
        if (!this.entries.TryGetValue(key, out var v))
        {
            value = null;
            return false;
        }

        if (v is null)
        {
            value = null;
            return true;
        }

        if (v is string s && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
        {
            value = l;
            return true;
        }

        value = null;
        return true;
    }

    public bool TryGetDouble(string key, out double? value)
    {
        if (!this.entries.TryGetValue(key, out var v))
        {
            value = null;
            return false;
        }

        if (v is null)
        {
            value = null;
            return true;
        }

        if (v is string s)
        {
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                value = d;
                return true;
            }

            OpenTelemetryDeclarativeConfigurationEventSource.Log.InvalidDoubleValue(key, s);
            value = null;
            return true;
        }

        value = null;
        return true;
    }

    public IDeclarativeConfigProperties? GetProperties(string key)
    {
        if (!this.entries.TryGetValue(key, out var v))
        {
            return null; // absent
        }

        return v as IDeclarativeConfigProperties; // null for present-null or wrong type
    }

    public IReadOnlyList<IDeclarativeConfigProperties>? GetPropertiesList(string key)
    {
        if (!this.entries.TryGetValue(key, out var v))
        {
            return null;
        }

        return v as IReadOnlyList<IDeclarativeConfigProperties>;
    }

    internal sealed class Builder
    {
        private readonly Dictionary<string, object?> entries = new(StringComparer.Ordinal);

        // null value = present-null for this key
        internal Builder Set(string key, string? value)
        {
            this.entries[key] = value;
            return this;
        }

        internal Builder SetProperties(string key, DeclarativeConfigProperties properties)
        {
            this.entries[key] = properties;
            return this;
        }

        internal Builder SetPropertiesList(string key, IReadOnlyList<IDeclarativeConfigProperties> list)
        {
            this.entries[key] = list;
            return this;
        }

        internal DeclarativeConfigProperties Build() =>
            this.entries.Count == 0
                ? DeclarativeConfigProperties.Empty
                : new DeclarativeConfigProperties(new ReadOnlyDictionary<string, object?>(this.entries));
    }
}
