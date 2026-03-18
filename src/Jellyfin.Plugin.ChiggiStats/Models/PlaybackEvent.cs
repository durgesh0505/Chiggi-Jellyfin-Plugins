using System;

namespace Jellyfin.Plugin.ChiggiStats.Models;

/// <summary>
/// Represents a single playback event recorded by the Chiggi Stats plugin.
/// </summary>
public class PlaybackEvent
{
    /// <summary>Gets or sets the auto-increment database ID.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the Jellyfin user ID.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name of the user.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the Jellyfin item ID.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Gets or sets the name of the media item.</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>Gets or sets the media type: Movie, Episode, or Audio.</summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>Gets or sets the series name for episodes.</summary>
    public string? SeriesName { get; set; }

    /// <summary>Gets or sets the season number for episodes.</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>Gets or sets the episode number.</summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>Gets or sets when playback started (UTC).</summary>
    public DateTime StartTime { get; set; }

    /// <summary>Gets or sets the total duration played in ticks (10,000 ticks = 1 ms).</summary>
    public long PlaybackDurationTicks { get; set; }

    /// <summary>Gets or sets a value indicating whether the item was watched to completion.</summary>
    public bool Completed { get; set; }

    /// <summary>Gets or sets the client name (e.g. Jellyfin Web, Infuse).</summary>
    public string? ClientName { get; set; }

    /// <summary>Gets or sets the device name.</summary>
    public string? DeviceName { get; set; }
}
