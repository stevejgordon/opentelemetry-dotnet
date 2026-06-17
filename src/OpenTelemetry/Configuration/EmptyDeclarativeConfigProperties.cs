// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#pragma warning disable OTEL1007 // IDeclarativeConfigProperties is experimental; this internal class is part of that surface

namespace OpenTelemetry;

/// <summary>
/// An <see cref="IDeclarativeConfigProperties"/> with no entries, used when a component
/// has no configuration properties (e.g. <c>always_on</c> sampler, <c>console</c> exporter).
/// </summary>
internal sealed class EmptyDeclarativeConfigProperties : IDeclarativeConfigProperties
{
    internal static readonly EmptyDeclarativeConfigProperties Instance = new();

    private EmptyDeclarativeConfigProperties()
    {
    }

    public IEnumerable<string> GetKeys() => [];

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

    public IDeclarativeConfigProperties? GetProperties(string key) => null;

    public IReadOnlyList<IDeclarativeConfigProperties>? GetPropertiesList(string key) => null;
}
