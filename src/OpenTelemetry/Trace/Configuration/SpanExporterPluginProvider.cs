// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Trace;

/// <summary>
/// Base class for SDK extension plugin component providers that create
/// <see cref="BaseExporter{Activity}"/> instances from a declarative
/// configuration property bag.
/// </summary>
/// <remarks>
/// Derive from this class and implement <see cref="PluginComponentProvider{TComponent}.Name"/>
/// and <see cref="PluginComponentProvider{TComponent}.CreateComponent"/> to contribute a
/// custom exporter to the declarative configuration registry. Register the provider
/// with <c>services.AddSpanExporterPluginProvider(provider)</c>.
/// </remarks>
[Experimental(DiagnosticDefinitions.PluginComponentProviderExperimentalApi, UrlFormat = DiagnosticDefinitions.ExperimentalApiUrlFormat)]
public abstract class SpanExporterPluginProvider : PluginComponentProvider<BaseExporter<Activity>>
{
}
