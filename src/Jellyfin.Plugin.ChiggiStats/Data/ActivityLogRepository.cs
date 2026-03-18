using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ChiggiStats.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ChiggiStats.Data;

/// <summary>
/// Supplemental data source for pre-installation playback history.
/// IActivityManager was removed from the Jellyfin public API in 10.9+.
/// This class is kept as a stub so the rest of the plugin compiles and
/// the DI graph stays consistent; it always returns an empty collection.
/// </summary>
public sealed class ActivityLogRepository
{
    private readonly ILogger<ActivityLogRepository> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityLogRepository"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public ActivityLogRepository(ILogger<ActivityLogRepository> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns an empty list. The Jellyfin activity log API is not available
    /// via a stable public interface in Jellyfin 10.9+.
    /// </summary>
    /// <param name="userId">Unused — filter by user ID.</param>
    /// <param name="startDate">Unused — start of date range.</param>
    /// <param name="endDate">Unused — end of date range.</param>
    /// <param name="limit">Unused — maximum number of results.</param>
    /// <returns>Always an empty list.</returns>
    public IReadOnlyList<PlaybackEvent> GetActivityLogEvents(
        string? userId,
        DateTime? startDate,
        DateTime? endDate,
        int limit = 200)
    {
        _logger.LogDebug("ActivityLogRepository is a stub — no pre-installation history available.");
        return Array.Empty<PlaybackEvent>();
    }
}
