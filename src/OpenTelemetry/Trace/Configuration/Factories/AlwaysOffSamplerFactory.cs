// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#pragma warning disable OTEL1007 // SamplerPluginProvider is experimental; this internal class is part of that surface

namespace OpenTelemetry.Trace;

internal sealed class AlwaysOffSamplerFactory : SamplerPluginProvider
{
    internal const string SchemaName = "always_off";

    public override string Name => SchemaName;

    public override Sampler CreateComponent(IDeclarativeConfigProperties properties, IServiceProvider serviceProvider)
        => AlwaysOffSampler.Instance;
}
