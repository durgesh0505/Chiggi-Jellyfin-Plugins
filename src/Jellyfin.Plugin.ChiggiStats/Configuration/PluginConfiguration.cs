using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ChiggiStats.Configuration;

/// <summary>
/// Plugin configuration for Chiggi Stats.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether SQLite tracking is enabled.
    /// When disabled the plugin will still read from Jellyfin's activity log.
    /// </summary>
    public bool EnableSqliteTracking { get; set; } = true;

    /// <summary>
    /// Gets or sets the number of days to retain playback data.
    /// 0 means retain forever.
    /// </summary>
    public int DataRetentionDays { get; set; } = 365;

    /// <summary>
    /// Gets or sets a value indicating whether to also read data from
    /// Jellyfin's built-in activity log as a supplemental source.
    /// </summary>
    public bool IncludeActivityLogData { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum playback duration in seconds required
    /// before an event is recorded. Events shorter than this are ignored.
    /// </summary>
    public int MinimumPlaybackSeconds { get; set; } = 30;
}
