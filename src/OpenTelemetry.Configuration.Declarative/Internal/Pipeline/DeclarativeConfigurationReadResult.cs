// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;

namespace OpenTelemetry.Configuration.Declarative;

/// <summary>
/// Holds both outputs produced by a single parse of a declarative configuration file:
/// the typed in-memory model (used by the component builder) and the flat key/value projection
/// (used by the <see cref="IConfigurationProvider"/> layer).
/// </summary>
internal sealed record DeclarativeConfigurationReadResult(
    DeclarativeConfiguration Model,
    ReadOnlyDictionary<string, string?> FlatKeys);
