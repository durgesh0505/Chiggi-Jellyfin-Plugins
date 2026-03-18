using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Mime;
using Jellyfin.Plugin.ChiggiStats.Data;
using Jellyfin.Plugin.ChiggiStats.Models;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ChiggiStats.Api;

/// <summary>
/// REST API controller for Chiggi Stats.
/// All endpoints require an authenticated Jellyfin session.
/// </summary>
[ApiController]
[Route("ChiggiStats")]
[Produces(MediaTypeNames.Application.Json)]
public class StatsController : ControllerBase
{
    private readonly SqliteRepository _sqlite;
    private readonly ActivityLogRepository _activityLog;
    private readonly IUserManager _userManager;
    private readonly ILogger<StatsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatsController"/> class.
    /// </summary>
    /// <param name="sqlite">The SQLite repository.</param>
    /// <param name="activityLog">The activity log repository.</param>
    /// <param name="userManager">The Jellyfin user manager.</param>
    /// <param name="logger">The logger.</param>
    public StatsController(
        SqliteRepository sqlite,
        ActivityLogRepository activityLog,
        IUserManager userManager,
        ILogger<StatsController> logger)
    {
        _sqlite = sqlite;
        _activityLog = activityLog;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Returns a paginated list of playback events with optional filters.
    /// Regular users only see their own events; admins can view any user.
    /// </summary>
    /// <param name="userId">Filter by user ID (admin only for other users).</param>
    /// <param name="startDate">Include events on or after this UTC date.</param>
    /// <param name="endDate">Include events on or before this UTC date.</param>
    /// <param name="mediaType">Filter by media type: Movie, Episode, or Audio.</param>
    /// <param name="limit">Page size (default 50, max 500).</param>
    /// <param name="offset">Number of records to skip (default 0).</param>
    /// <returns>Paginated activity list.</returns>
    [HttpGet("activity")]
    [Authorize(Policy = "DefaultAuthorization")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<ActivityResponse> GetActivity(
        [FromQuery] string? userId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] string? mediaType,
        [FromQuery][Range(1, 500)] int limit = 50,
        [FromQuery][Range(0, int.MaxValue)] int offset = 0)
    {
        var effectiveUserId = ResolveUserId(userId);
        if (effectiveUserId == null)
        {
            return Unauthorized();
        }

        var config = Plugin.Instance?.Configuration;
        var (items, totalCount) = _sqlite.QueryEvents(effectiveUserId, startDate, endDate, mediaType, limit, offset);

        var responseItems = items.Select(MapToDto).ToList();

        // Supplement with activity log data on first page if enabled and SQLite is empty
        if (config?.IncludeActivityLogData == true && offset == 0 && items.Count == 0)
        {
            var logEvents = _activityLog.GetActivityLogEvents(effectiveUserId, startDate, endDate);
            responseItems.AddRange(logEvents.Select(MapToDto));
            totalCount = responseItems.Count;
        }

        return Ok(new ActivityResponse
        {
            TotalCount = totalCount,
            Items = responseItems
        });
    }

    /// <summary>
    /// Returns aggregated statistics for the given filters.
    /// </summary>
    /// <param name="userId">Filter by user ID (admin only for other users).</param>
    /// <param name="startDate">Start of date range (UTC).</param>
    /// <param name="endDate">End of date range (UTC).</param>
    /// <returns>Summary statistics.</returns>
    [HttpGet("summary")]
    [Authorize(Policy = "DefaultAuthorization")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<SummaryResponse> GetSummary(
        [FromQuery] string? userId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        var effectiveUserId = ResolveUserId(userId);
        if (effectiveUserId == null)
        {
            return Unauthorized();
        }

        var stats = _sqlite.GetSummary(effectiveUserId, startDate, endDate);
        var byDay = _sqlite.GetWatchTimeByDay(effectiveUserId, startDate, endDate);
        var topItems = _sqlite.GetTopItems(effectiveUserId, startDate, endDate, null, 10);

        return Ok(new SummaryResponse
        {
            TotalSessions = stats.TotalSessions,
            TotalWatchTimeTicks = stats.TotalPlaybackTicks,
            TotalWatchTimeHours = Math.Round(stats.TotalPlaybackTicks / (double)TimeSpan.TicksPerHour, 1),
            MovieCount = stats.MovieCount,
            EpisodeCount = stats.EpisodeCount,
            AudioCount = stats.AudioCount,
            WatchTimeByDay = byDay.Select(d => new DailyDto { Date = d.Date, Minutes = (int)(d.TotalTicks / TimeSpan.TicksPerMinute) }).ToList(),
            TopItems = topItems.Select(t => new TopItemDto
            {
                ItemId = t.ItemId,
                ItemName = t.ItemName,
                SeriesName = t.SeriesName,
                MediaType = t.MediaType,
                WatchCount = t.WatchCount,
                TotalMinutes = (int)(t.TotalTicks / TimeSpan.TicksPerMinute)
            }).ToList()
        });
    }

    /// <summary>
    /// Returns a list of users who have playback history (admin only).
    /// </summary>
    /// <returns>List of users.</returns>
    [HttpGet("users")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<IReadOnlyList<UserDto>> GetUsers()
    {
        var users = _userManager.Users
            .Select(u => new UserDto { UserId = u.Id.ToString("N"), UserName = u.Username })
            .OrderBy(u => u.UserName)
            .ToList();

        return Ok(users);
    }

    // Resolves the effective user ID. Non-admins are locked to their own ID.
    private string? ResolveUserId(string? requestedUserId)
    {
        // Get current authenticated user from claims
        var claimUserId = User.FindFirst("uid")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

        if (string.IsNullOrEmpty(claimUserId))
        {
            return requestedUserId; // fallback: trust the provided ID
        }

        // Check if the authenticated user is an admin
        if (Guid.TryParse(claimUserId, out var callerGuid))
        {
            var caller = _userManager.GetUserById(callerGuid);
            if (caller?.HasPermission(MediaBrowser.Model.Users.PermissionKind.IsAdministrator) == true)
            {
                return requestedUserId; // admins can query any user
            }
        }

        // Non-admins can only see their own data
        return string.IsNullOrEmpty(requestedUserId) ? claimUserId : claimUserId;
    }

    private static PlaybackEventDto MapToDto(PlaybackEvent evt)
    {
        return new PlaybackEventDto
        {
            Id = evt.Id,
            UserId = evt.UserId,
            UserName = evt.UserName,
            ItemId = evt.ItemId,
            ItemName = evt.ItemName,
            MediaType = evt.MediaType,
            SeriesName = evt.SeriesName,
            SeasonNumber = evt.SeasonNumber,
            EpisodeNumber = evt.EpisodeNumber,
            StartTime = evt.StartTime,
            DurationMinutes = Math.Round(evt.PlaybackDurationTicks / (double)TimeSpan.TicksPerMinute, 1),
            Completed = evt.Completed,
            ClientName = evt.ClientName,
            DeviceName = evt.DeviceName
        };
    }
}

/// <summary>Paginated list of playback events.</summary>
public class ActivityResponse
{
    /// <summary>Gets or sets the total number of matching records (before pagination).</summary>
    public int TotalCount { get; set; }

    /// <summary>Gets or sets the current page of events.</summary>
    public List<PlaybackEventDto> Items { get; set; } = new();
}

/// <summary>A single playback event as returned by the API.</summary>
public class PlaybackEventDto
{
    /// <summary>Gets or sets the database record ID.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the Jellyfin user ID.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Gets or sets the user display name.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the Jellyfin item ID.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Gets or sets the item display name.</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>Gets or sets the media type.</summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>Gets or sets the series name (episodes only).</summary>
    public string? SeriesName { get; set; }

    /// <summary>Gets or sets the season number (episodes only).</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>Gets or sets the episode number (episodes only).</summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>Gets or sets when the session started.</summary>
    public DateTime StartTime { get; set; }

    /// <summary>Gets or sets the duration watched in minutes.</summary>
    public double DurationMinutes { get; set; }

    /// <summary>Gets or sets a value indicating whether the item was watched to completion.</summary>
    public bool Completed { get; set; }

    /// <summary>Gets or sets the client name.</summary>
    public string? ClientName { get; set; }

    /// <summary>Gets or sets the device name.</summary>
    public string? DeviceName { get; set; }
}

/// <summary>Aggregated stats summary.</summary>
public class SummaryResponse
{
    /// <summary>Gets or sets total number of sessions.</summary>
    public int TotalSessions { get; set; }

    /// <summary>Gets or sets total watch time in ticks.</summary>
    public long TotalWatchTimeTicks { get; set; }

    /// <summary>Gets or sets total watch time in hours.</summary>
    public double TotalWatchTimeHours { get; set; }

    /// <summary>Gets or sets number of movie sessions.</summary>
    public int MovieCount { get; set; }

    /// <summary>Gets or sets number of episode sessions.</summary>
    public int EpisodeCount { get; set; }

    /// <summary>Gets or sets number of audio sessions.</summary>
    public int AudioCount { get; set; }

    /// <summary>Gets or sets watch time by day for the chart.</summary>
    public List<DailyDto> WatchTimeByDay { get; set; } = new();

    /// <summary>Gets or sets the top 10 most-watched items.</summary>
    public List<TopItemDto> TopItems { get; set; } = new();
}

/// <summary>Watch time for a single day.</summary>
public class DailyDto
{
    /// <summary>Gets or sets the date string (YYYY-MM-DD).</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Gets or sets total minutes watched.</summary>
    public int Minutes { get; set; }
}

/// <summary>A top-watched item.</summary>
public class TopItemDto
{
    /// <summary>Gets or sets the Jellyfin item ID.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Gets or sets the item name.</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>Gets or sets the series name for episodes.</summary>
    public string? SeriesName { get; set; }

    /// <summary>Gets or sets the media type.</summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>Gets or sets how many times watched.</summary>
    public int WatchCount { get; set; }

    /// <summary>Gets or sets total minutes watched.</summary>
    public int TotalMinutes { get; set; }
}

/// <summary>A Jellyfin user.</summary>
public class UserDto
{
    /// <summary>Gets or sets the user ID.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Gets or sets the username.</summary>
    public string UserName { get; set; } = string.Empty;
}
