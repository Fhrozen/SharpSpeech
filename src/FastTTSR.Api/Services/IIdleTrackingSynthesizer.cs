namespace FastTTSR.Api.Services;

/// <summary>
/// Interface for synthesizers that support idle model monitoring and cleanup.
/// </summary>
public interface IIdleTrackingSynthesizer
{
    /// <summary>
    /// Gets a collection of currently loaded engine keys and their last access times.
    /// </summary>
    IReadOnlyDictionary<string, DateTime> GetLoadedEngines();

    /// <summary>
    /// Attempts to release an idle engine by its cache key.
    /// </summary>
    /// <param name="engineKey">The cache key of the engine to release</param>
    /// <returns>True if the engine was successfully released, false otherwise</returns>
    bool TryReleaseEngine(string engineKey);
}
