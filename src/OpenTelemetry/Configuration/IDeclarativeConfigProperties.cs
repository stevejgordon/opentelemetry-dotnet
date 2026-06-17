// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using OpenTelemetry.Internal;

namespace OpenTelemetry;

/// <summary>
/// A library-neutral, three-state property bag produced from a declarative
/// configuration source and passed to a <see cref="PluginComponentProvider{TComponent}"/>
/// when constructing an SDK component.
/// </summary>
/// <remarks>
/// <para>
/// The three-state semantics mirror the OpenTelemetry specification's
/// <c>ComponentProvider.create(properties)</c> contract:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>Absent</b> - the key was not present in the source document;
///     <see cref="GetKeys"/> does not include it and the <c>TryGet*</c> methods
///     return <see langword="false"/>.
///   </description></item>
///   <item><description>
///     <b>Present-null</b> - the key appeared in the document with a null value;
///     <see cref="GetKeys"/> includes it and the <c>TryGet*</c> methods return
///     <see langword="true"/> with the <c>out</c> value set to
///     <see langword="null"/>.
///   </description></item>
///   <item><description>
///     <b>Present</b> - the key appeared in the document with a non-null value;
///     <see cref="GetKeys"/> includes it and the <c>TryGet*</c> methods return
///     <see langword="true"/> with the <c>out</c> value set to the value.
///   </description></item>
/// </list>
/// <para>
/// The distinction between absent and present-null is required by the spec
/// (absent uses the SDK default; present-null applies <c>nullBehavior</c>).
/// </para>
/// <para>
/// This interface is the <b>construction / selection / structure layer</b> for
/// component configuration. Reloadable scalar values (e.g. a sample rate that
/// can change at runtime) are handled by the separate
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> layer;
/// any factory may resolve that service from the
/// <see cref="IServiceProvider"/> passed to
/// <see cref="PluginComponentProvider{TComponent}.CreateComponent"/>.
/// </para>
/// </remarks>
[Experimental(DiagnosticDefinitions.PluginComponentProviderExperimentalApi, UrlFormat = DiagnosticDefinitions.ExperimentalApiUrlFormat)]
public interface IDeclarativeConfigProperties
{
    /// <summary>
    /// Returns the keys for all present and present-null entries in this bag.
    /// Absent keys are not included.
    /// </summary>
    /// <returns>The set of present keys in this property bag.</returns>
    IEnumerable<string> GetKeys();

    /// <summary>
    /// Tries to read a string value for <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <param name="value">
    /// Set to the string value when the key is present, or <see langword="null"/>
    /// when the key is present-null. Not set when the key is absent.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the key is present or present-null;
    /// <see langword="false"/> if the key is absent.
    /// </returns>
    bool TryGetString(string key, out string? value);

    /// <summary>
    /// Tries to read a boolean value for <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <param name="value">
    /// Set to the boolean value when the key is present, or <see langword="null"/>
    /// when the key is present-null. Not set when the key is absent.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the key is present or present-null;
    /// <see langword="false"/> if the key is absent.
    /// </returns>
    bool TryGetBool(string key, out bool? value);

    /// <summary>
    /// Tries to read a 32-bit integer value for <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <param name="value">
    /// Set to the integer value when the key is present, or <see langword="null"/>
    /// when the key is present-null. Not set when the key is absent.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the key is present or present-null;
    /// <see langword="false"/> if the key is absent.
    /// </returns>
    bool TryGetInt(string key, out int? value);

    /// <summary>
    /// Tries to read a 64-bit integer value for <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <param name="value">
    /// Set to the integer value when the key is present, or <see langword="null"/>
    /// when the key is present-null. Not set when the key is absent.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the key is present or present-null;
    /// <see langword="false"/> if the key is absent.
    /// </returns>
    bool TryGetLong(string key, out long? value);

    /// <summary>
    /// Tries to read a double-precision floating-point value for
    /// <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <param name="value">
    /// Set to the double value when the key is present, or <see langword="null"/>
    /// when the key is present-null. Not set when the key is absent.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the key is present or present-null;
    /// <see langword="false"/> if the key is absent.
    /// </returns>
    bool TryGetDouble(string key, out double? value);

    /// <summary>
    /// Returns the nested property bag for <paramref name="key"/>, or
    /// <see langword="null"/> if the key is absent or has no mapping value.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <returns>
    /// The nested property bag, or <see langword="null"/> if absent or present-null.
    /// </returns>
    IDeclarativeConfigProperties? GetProperties(string key);

    /// <summary>
    /// Returns the list of nested property bags for <paramref name="key"/>, or
    /// <see langword="null"/> if the key is absent or has no sequence value.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <returns>
    /// The list of nested property bags, or <see langword="null"/> if absent or present-null.
    /// </returns>
    IReadOnlyList<IDeclarativeConfigProperties>? GetPropertiesList(string key);
}
