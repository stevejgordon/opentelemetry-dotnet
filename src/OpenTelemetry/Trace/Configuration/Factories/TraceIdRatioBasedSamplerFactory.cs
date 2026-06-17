// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#pragma warning disable OTEL1007 // SamplerPluginProvider is experimental; this internal class is part of that surface

using System.Globalization;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Trace;

internal sealed class TraceIdRatioBasedSamplerFactory : SamplerPluginProvider
{
    internal const string SchemaName = "trace_id_ratio_based";

    public override string Name => SchemaName;

    public override Sampler CreateComponent(IDeclarativeConfigProperties properties, IServiceProvider serviceProvider)
    {
        double ratio = 1.0;

        if (properties.TryGetDouble("ratio", out var ratioDouble) && ratioDouble.HasValue)
        {
            var v = ratioDouble.Value;
            if (!double.IsNaN(v) && !double.IsInfinity(v) && v >= 0.0 && v <= 1.0)
            {
                ratio = v;
            }
            else
            {
                OpenTelemetrySdkEventSource.Log.TraceIdRatioBasedSamplerRatioInvalid(v.ToString(CultureInfo.InvariantCulture));
            }
        }

        return new TraceIdRatioBasedSampler(ratio);
    }
}
