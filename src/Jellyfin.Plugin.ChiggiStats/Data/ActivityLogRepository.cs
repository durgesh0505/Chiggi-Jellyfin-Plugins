using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ChiggiStats.Models;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ChiggiStats.Data;

/// <summary>
/// Reads historical playback data from Jellyfin's built-in activity log.
/// Used as a supplemental data source alongside <see cref="SqliteRepository"/>.
/// The activity log has limited detail (no duration, no client name), but provides
/// history that predates this plugin's installation.
/// </summary>
public sealed class ActivityLogRepository
{
    private readonly IActivityManager _activityManager;
    private readonly ILogger<ActivityLogRepository> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityLogRepository"/> class.
    /// </summary>
    /// <param name="activityManager">Jellyfin activity manager.</param>
    /// <param name="logger">The logger.</param>
    public ActivityLogRepository(IActivityManager activityManager, ILogger<ActivityLogRepository> logger)
    {
        _activityManager = activityManager;
        _logger = logger;
    }

    /// <summary>
    /// Reads "VideoPlayback" entries from Jellyfin's activity log and converts them
    /// to <see cref="PlaybackEvent"/> objects. Duration will be 0 because the activity
    /// log does not record playback duration.
    /// </summary>
    /// <param name="userId">Filter by user ID, or null for all users.</param>
    /// <param name="startDate">Start of date range.</param>
    /// <param name="endDate">End of date range.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <returns>List of playback events derived from activity log entries.</returns>
    public IReadOnlyList<PlaybackEvent> GetActivityLogEvents(
        string? userId,
        DateTime? startDate,
        DateTime? endDate,
        int limit = 200)
    {
        try
        {
            var query = new ActivityLogEntryQuery
            {
                StartIndex = 0,
                Limit = limit,
                MinDate = startDate,
                HasUserId = !string.IsNullOrEmpty(userId)
            };

            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var userGuid))
            {
                query.UserId = userGuid;
            }

            var result = _activityManager.GetPagedResult(query);
            var events = new List<PlaybackEvent>();

            foreach (var entry in result.Items)
            {
                // Only include video playback entries
                if (!string.Equals(entry.Type, "VideoPlayback", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(entry.Type, "VideoPlaybackStopped", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (endDate.HasValue && entry.Date > endDate.Value)
                {
                    continue;
                }

                events.Add(new PlaybackEvent
                {
                    Id = entry.Id,
                    UserId = entry.UserId?.ToString("N") ?? string.Empty,
                    UserName = entry.UserName ?? string.Empty,
                    ItemId = string.Empty,
                    ItemName = entry.ItemName ?? string.Empty,
                    MediaType = "Unknown",
                    StartTime = entry.Date,
                    PlaybackDurationTicks = 0,
                    Completed = string.Equals(entry.Type, "VideoPlaybackStopped", StringComparison.OrdinalIgnoreCase)
                });
            }

            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read Jellyfin activity log.");
            return Array.Empty<PlaybackEvent>();
        }
    }
}
