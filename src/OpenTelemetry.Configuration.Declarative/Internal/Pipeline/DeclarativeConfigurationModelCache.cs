// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace OpenTelemetry.Configuration.Declarative;

/// <summary>
/// Write-once cache that shares a single parse of the YAML file between the
/// <see cref="DeclarativeConfigurationProvider"/> (IConfiguration pipeline) and the
/// <see cref="DeclarativeComponentBuilder"/> (SDK build pipeline) without reading the file twice.
/// </summary>
/// <remarks>
/// Lifetime: created in <c>AddDeclarativeConfigurationOverlay</c>, registered in DI as a singleton,
/// and also stored in the <see cref="DeclarativeConfigurationSource"/> so that the provider can
/// populate it during <c>IConfigurationProvider.Load()</c>.
/// </remarks>
internal sealed class DeclarativeConfigurationModelCache
{
    private readonly Lazy<DeclarativeConfigurationReadResult> result;

    internal DeclarativeConfigurationModelCache(FilePath filePath)
    {
        this.FilePath = filePath;
        this.result = new Lazy<DeclarativeConfigurationReadResult>(
            () => DeclarativeConfigurationReader.ReadFull(filePath),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal FilePath FilePath { get; }

    /// <summary>
    /// Loads the file, caches the result, and returns it.
    /// </summary>
    /// <returns>The parsed <see cref="DeclarativeConfigurationReadResult"/>.</returns>
    internal DeclarativeConfigurationReadResult LoadFromProvider() => this.result.Value;

    /// <summary>
    /// Returns the cached result, or <see langword="null"/> if the file has not yet been loaded
    /// or if loading failed. Called by <see cref="DeclarativeComponentBuilder"/>.
    /// </summary>
    /// <returns>The cached <see cref="DeclarativeConfigurationReadResult"/>, or <see langword="null"/> if not yet loaded.</returns>
    internal DeclarativeConfigurationReadResult? TryGetResult() =>
        this.result.IsValueCreated ? this.result.Value : null;
}
