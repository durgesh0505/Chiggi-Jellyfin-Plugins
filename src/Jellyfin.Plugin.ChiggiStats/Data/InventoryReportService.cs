using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.ChiggiStats.Data;

/// <summary>
/// Builds admin-only report data for current Jellyfin library inventory and
/// server-wide user and device summaries.
/// </summary>
public sealed class InventoryReportService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly SqliteRepository _sqliteRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="InventoryReportService"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="userManager">The Jellyfin user manager.</param>
    /// <param name="sqliteRepository">The playback repository.</param>
    public InventoryReportService(
        ILibraryManager libraryManager,
        IUserManager userManager,
        SqliteRepository sqliteRepository)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _sqliteRepository = sqliteRepository;
    }

    /// <summary>
    /// Builds the overview metrics for the reporting dashboard.
    /// </summary>
    /// <param name="caller">The current authenticated admin user.</param>
    /// <returns>Overview metrics.</returns>
    public IReadOnlyList<ReportMetric> GetOverviewMetrics(User caller)
    {
        var summary = _sqliteRepository.GetSummary(null, null, null);

        return new List<ReportMetric>
        {
            new()
            {
                Key = "movies",
                Label = "Movies",
                Value = CountItems(caller, BaseItemKind.Movie).ToString(CultureInfo.InvariantCulture),
            },
            new()
            {
                Key = "series",
                Label = "Series",
                Value = CountItems(caller, BaseItemKind.Series).ToString(CultureInfo.InvariantCulture),
            },
            new()
            {
                Key = "episodes",
                Label = "Episodes",
                Value = CountItems(caller, BaseItemKind.Episode).ToString(CultureInfo.InvariantCulture),
            },
            new()
            {
                Key = "music",
                Label = "Music",
                Value = CountItems(caller, BaseItemKind.Audio, BaseItemKind.MusicAlbum).ToString(CultureInfo.InvariantCulture),
            },
            new()
            {
                Key = "boxsets",
                Label = "Box Sets",
                Value = CountItems(caller, BaseItemKind.BoxSet).ToString(CultureInfo.InvariantCulture),
            },
            new()
            {
                Key = "users",
                Label = "Users",
                Value = _userManager.Users.Count().ToString(CultureInfo.InvariantCulture),
            },
            new()
            {
                Key = "devices",
                Label = "Devices",
                Value = _sqliteRepository.GetDistinctDeviceCount().ToString(CultureInfo.InvariantCulture),
            },
            new()
            {
                Key = "playbackSessions",
                Label = "Playback Sessions",
                Value = summary.TotalSessions.ToString(CultureInfo.InvariantCulture),
            },
            new()
            {
                Key = "playbackHours",
                Label = "Playback Hours",
                Value = Math.Round(summary.TotalPlaybackTicks / (double)TimeSpan.TicksPerHour, 1).ToString("0.0", CultureInfo.InvariantCulture),
            },
        };
    }

    /// <summary>
    /// Builds a report table for the requested report type.
    /// </summary>
    /// <param name="caller">The current authenticated admin user.</param>
    /// <param name="reportType">The report type.</param>
    /// <param name="limit">Maximum rows to return.</param>
    /// <param name="offset">Offset for pagination.</param>
    /// <returns>A report table payload.</returns>
    public ReportTableData GetReport(User caller, string reportType, int limit, int offset)
    {
        return reportType.ToUpperInvariant() switch
        {
            "MOVIES" => BuildMediaTable(caller, "movies", "Movies", limit, offset, BaseItemKind.Movie),
            "SERIES" => BuildMediaTable(caller, "series", "Series", limit, offset, BaseItemKind.Series),
            "SEASONS" => BuildMediaTable(caller, "seasons", "Seasons", limit, offset, BaseItemKind.Season),
            "EPISODES" => BuildMediaTable(caller, "episodes", "Episodes", limit, offset, BaseItemKind.Episode),
            "MUSIC" => BuildMediaTable(caller, "music", "Music", limit, offset, BaseItemKind.Audio, BaseItemKind.MusicAlbum),
            "BOXSETS" => BuildMediaTable(caller, "boxsets", "Box Sets", limit, offset, BaseItemKind.BoxSet),
            "USERS" => BuildUsersTable("users", "Users"),
            "DEVICES" => BuildDevicesTable("devices", "Devices", limit, offset),
            _ => throw new ArgumentOutOfRangeException(nameof(reportType), reportType, "Unsupported report type."),
        };
    }

    private ReportTableData BuildMediaTable(User caller, string reportType, string title, int limit, int offset, params BaseItemKind[] itemKinds)
    {
        var allItems = _libraryManager.GetItemList(new InternalItemsQuery(caller)
        {
            IncludeItemTypes = itemKinds,
            IsVirtualItem = false,
        }).ToList();

        var totalCount = allItems.Count;
        var items = allItems
            .OrderBy(item => item.SortName ?? item.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Skip(offset)
            .Take(limit)
            .ToList();

        return new ReportTableData
        {
            ReportType = reportType,
            Title = title,
            TotalCount = totalCount,
            Columns = GetColumnsForReport(reportType),
            Rows = items.Select(item => BuildMediaRow(reportType, item)).ToList(),
        };
    }

    private ReportTableData BuildUsersTable(string reportType, string title)
    {
        var playbackByUser = _sqliteRepository.GetUserPlaybackSummaries()
            .ToDictionary(x => x.UserId, x => x, StringComparer.OrdinalIgnoreCase);

        var rows = _userManager.Users
            .OrderBy(x => x.Username)
            .Select(user =>
            {
                var userId = user.Id.ToString("N");
                playbackByUser.TryGetValue(userId, out var playback);

                return new ReportRowData
                {
                    Cells = new Dictionary<string, string?>
                    {
                        ["name"] = user.Username,
                        ["role"] = user.HasPermission(Jellyfin.Database.Implementations.Enums.PermissionKind.IsAdministrator) ? "Administrator" : "User",
                        ["sessions"] = (playback?.SessionCount ?? 0).ToString(CultureInfo.InvariantCulture),
                        ["hours"] = FormatHours(playback?.TotalTicks ?? 0),
                        ["lastSeen"] = FormatDateTime(playback?.LastSeen),
                    },
                };
            })
            .ToList();

        return new ReportTableData
        {
            ReportType = reportType,
            Title = title,
            TotalCount = rows.Count,
            Columns = new List<ReportColumn>
            {
                new() { Key = "name", Label = "User" },
                new() { Key = "role", Label = "Role" },
                new() { Key = "sessions", Label = "Sessions" },
                new() { Key = "hours", Label = "Hours Watched" },
                new() { Key = "lastSeen", Label = "Last Seen" },
            },
            Rows = rows,
        };
    }

    private ReportTableData BuildDevicesTable(string reportType, string title, int limit, int offset)
    {
        var summaries = _sqliteRepository.GetDevicePlaybackSummaries(limit, offset);
        var totalCount = _sqliteRepository.GetDistinctDeviceCount();

        return new ReportTableData
        {
            ReportType = reportType,
            Title = title,
            TotalCount = totalCount,
            Columns = new List<ReportColumn>
            {
                new() { Key = "device", Label = "Device" },
                new() { Key = "client", Label = "Client" },
                new() { Key = "users", Label = "Users" },
                new() { Key = "sessions", Label = "Sessions" },
                new() { Key = "hours", Label = "Hours Watched" },
                new() { Key = "lastSeen", Label = "Last Seen" },
            },
            Rows = summaries.Select(summary => new ReportRowData
            {
                Cells = new Dictionary<string, string?>
                {
                    ["device"] = summary.DeviceName,
                    ["client"] = summary.ClientName,
                    ["users"] = summary.DistinctUsers.ToString(CultureInfo.InvariantCulture),
                    ["sessions"] = summary.SessionCount.ToString(CultureInfo.InvariantCulture),
                    ["hours"] = FormatHours(summary.TotalTicks),
                    ["lastSeen"] = FormatDateTime(summary.LastSeen),
                },
            }).ToList(),
        };
    }

    private static List<ReportColumn> GetColumnsForReport(string reportType)
    {
        return reportType switch
        {
            "movies" => new List<ReportColumn>
            {
                new() { Key = "name", Label = "Title" },
                new() { Key = "year", Label = "Year" },
                new() { Key = "runtime", Label = "Runtime" },
                new() { Key = "added", Label = "Added" },
                new() { Key = "path", Label = "Path" },
            },
            "series" => new List<ReportColumn>
            {
                new() { Key = "name", Label = "Series" },
                new() { Key = "year", Label = "Year" },
                new() { Key = "status", Label = "Status" },
                new() { Key = "added", Label = "Added" },
                new() { Key = "path", Label = "Path" },
            },
            "seasons" => new List<ReportColumn>
            {
                new() { Key = "series", Label = "Series" },
                new() { Key = "season", Label = "Season" },
                new() { Key = "added", Label = "Added" },
                new() { Key = "path", Label = "Path" },
            },
            "episodes" => new List<ReportColumn>
            {
                new() { Key = "series", Label = "Series" },
                new() { Key = "season", Label = "Season" },
                new() { Key = "episode", Label = "Episode" },
                new() { Key = "name", Label = "Title" },
                new() { Key = "runtime", Label = "Runtime" },
                new() { Key = "added", Label = "Added" },
            },
            "music" => new List<ReportColumn>
            {
                new() { Key = "name", Label = "Title" },
                new() { Key = "artist", Label = "Artist" },
                new() { Key = "album", Label = "Album" },
                new() { Key = "year", Label = "Year" },
                new() { Key = "runtime", Label = "Runtime" },
                new() { Key = "path", Label = "Path" },
            },
            "boxsets" => new List<ReportColumn>
            {
                new() { Key = "name", Label = "Box Set" },
                new() { Key = "added", Label = "Added" },
                new() { Key = "path", Label = "Path" },
            },
            _ => new List<ReportColumn>
            {
                new() { Key = "name", Label = "Name" },
            },
        };
    }

    private static ReportRowData BuildMediaRow(string reportType, BaseItem item)
    {
        var row = new ReportRowData
        {
            Cells = new Dictionary<string, string?>
            {
                ["name"] = item.Name,
                ["year"] = item.ProductionYear?.ToString(CultureInfo.InvariantCulture),
                ["runtime"] = FormatRuntime(item.RunTimeTicks),
                ["added"] = FormatDateTime(item.DateCreated),
                ["path"] = item.Path,
            },
        };

        switch (reportType)
        {
            case "series":
                var series = (Series)item;
                row.Cells["status"] = series.Status.ToString();
                break;
            case "seasons":
                var season = (Season)item;
                row.Cells["series"] = season.Series?.Name;
                row.Cells["season"] = FormatSeasonLabel(season.ParentIndexNumber);
                row.Cells.Remove("name");
                row.Cells.Remove("year");
                row.Cells.Remove("runtime");
                break;
            case "episodes":
                var episode = (Episode)item;
                row.Cells["series"] = episode.Series?.Name;
                row.Cells["season"] = FormatSeasonLabel(episode.ParentIndexNumber);
                row.Cells["episode"] = episode.IndexNumber?.ToString(CultureInfo.InvariantCulture);
                break;
            case "music":
                row.Cells["artist"] = TryGetArtist(item);
                row.Cells["album"] = TryGetAlbum(item);
                break;
            case "boxsets":
                row.Cells.Remove("year");
                row.Cells.Remove("runtime");
                break;
        }

        return row;
    }

    private int CountItems(User caller, params BaseItemKind[] itemKinds)
    {
        return _libraryManager.GetCount(new InternalItemsQuery(caller)
        {
            IncludeItemTypes = itemKinds,
            IsVirtualItem = false,
        });
    }

    private static string? TryGetArtist(BaseItem item)
    {
        return item switch
        {
            Audio audio => audio.AlbumArtists.Count > 0
                ? audio.AlbumArtists[0]
                : audio.Artists.Count > 0 ? audio.Artists[0] : null,
            MusicAlbum album => album.AlbumArtists.Count > 0
                ? album.AlbumArtists[0]
                : album.Artists.Count > 0 ? album.Artists[0] : null,
            _ => null,
        };
    }

    private static string? TryGetAlbum(BaseItem item)
    {
        return item switch
        {
            Audio audio => audio.Album,
            MusicAlbum album => album.Name,
            _ => null,
        };
    }

    private static string FormatHours(long ticks)
    {
        return Math.Round(ticks / (double)TimeSpan.TicksPerHour, 1).ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static string? FormatRuntime(long? runtimeTicks)
    {
        if (!runtimeTicks.HasValue || runtimeTicks.Value <= 0)
        {
            return null;
        }

        var runtime = TimeSpan.FromTicks(runtimeTicks.Value);
        return runtime.TotalHours >= 1
            ? $"{(int)runtime.TotalHours}h {runtime.Minutes}m"
            : $"{runtime.Minutes}m";
    }

    private static string? FormatDateTime(DateTime? value)
    {
        return value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string FormatSeasonLabel(int? seasonNumber)
    {
        return seasonNumber.HasValue
            ? $"Season {seasonNumber.Value.ToString(CultureInfo.InvariantCulture)}"
            : "Season ?";
    }
}

/// <summary>
/// A top-level overview metric.
/// </summary>
public sealed class ReportMetric
{
    /// <summary>
    /// Gets or sets the metric key.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the metric label.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the metric value.
    /// </summary>
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Report table metadata and row payload.
/// </summary>
public sealed class ReportTableData
{
    /// <summary>
    /// Gets or sets the report type.
    /// </summary>
    public string ReportType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the report title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total matching row count.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets the report columns.
    /// </summary>
    public IReadOnlyList<ReportColumn> Columns { get; set; } = Array.Empty<ReportColumn>();

    /// <summary>
    /// Gets or sets the report rows.
    /// </summary>
    public IReadOnlyList<ReportRowData> Rows { get; set; } = Array.Empty<ReportRowData>();
}

/// <summary>
/// A report column descriptor.
/// </summary>
public sealed class ReportColumn
{
    /// <summary>
    /// Gets or sets the column key.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the column label.
    /// </summary>
    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// A single report row payload.
/// </summary>
public sealed class ReportRowData
{
    /// <summary>
    /// Gets or sets the row cell values.
    /// </summary>
    public Dictionary<string, string?> Cells { get; set; } = new();
}
