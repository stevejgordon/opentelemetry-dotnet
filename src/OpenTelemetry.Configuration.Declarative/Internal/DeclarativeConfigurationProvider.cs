// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

// DeclarativeConfigurationException carries the OTEL1006 experimental attribute.
// Suppress once here rather than at every throw/catch site.
#pragma warning disable OTEL1006

using Microsoft.Extensions.Configuration;

namespace OpenTelemetry.Configuration.Declarative;

/// <summary>
/// An <see cref="IConfigurationProvider"/> that reads OpenTelemetry configuration from a declarative configuration YAML file.
/// The file is parsed and loaded into the provider's data dictionary.
/// </summary>
internal sealed class DeclarativeConfigurationProvider(DeclarativeConfigurationModelCache cache) : ConfigurationProvider
{
    private readonly DeclarativeConfigurationModelCache cache = cache;

    internal FilePath FilePath => this.cache.FilePath;

    /// <inheritdoc/>
    public override void Load()
    {
        var filePath = this.cache.FilePath.DisplayPath;

        try
        {
            var result = this.cache.LoadFromProvider();

            this.Data = result.FlatKeys;

            OpenTelemetryDeclarativeConfigurationEventSource.Log.ConfigurationLoadSucceeded(filePath, result.FlatKeys.Count);

            if (result.FlatKeys.TryGetValue(DeclarativeConfigurationConverter.DisabledKey, out var disabledValue) &&
                bool.TryParse(disabledValue, out var disabled) && disabled)
            {
                OpenTelemetryDeclarativeConfigurationEventSource.Log.SdkDisabledDetected(filePath);
            }
        }
        catch (DeclarativeConfigurationException ex)
        {
            OpenTelemetryDeclarativeConfigurationEventSource.Log.FailedToLoadConfiguration(filePath, ex);
            throw;
        }
        catch (Exception ex)
        {
            OpenTelemetryDeclarativeConfigurationEventSource.Log.FailedToLoadConfiguration(filePath, ex);
            throw new DeclarativeConfigurationException(
                $"Failed to load declarative configuration from '{filePath}': {ex.Message}", ex);
        }
    }
}
