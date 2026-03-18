using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using Jellyfin.Plugin.ChiggiStats.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ChiggiStats.Data;

/// <summary>
/// Provides SQLite-backed persistent storage for playback events.
/// This is the primary data source — every play/stop event is recorded here.
/// </summary>
public sealed class SqliteRepository : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ILogger<SqliteRepository> _logger;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteRepository"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="appPaths">Jellyfin application paths (used to locate the data directory).</param>
    public SqliteRepository(ILogger<SqliteRepository> logger, IApplicationPaths appPaths)
    {
        _logger = logger;
        var dbPath = System.IO.Path.Combine(appPaths.DataPath, "chiggistats.db");
        _connection = new SqliteConnection($"Data Source={dbPath};Cache=Shared;");
        _connection.Open();
        InitializeDatabase();
        _logger.LogInformation("Chiggi Stats database opened at {Path}", dbPath);
    }

    private void InitializeDatabase()
    {
        Execute("PRAGMA journal_mode=WAL;");
        Execute(@"CREATE TABLE IF NOT EXISTS PlaybackEvents (
            Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
            UserId                TEXT    NOT NULL,
            UserName              TEXT    NOT NULL,
            ItemId                TEXT    NOT NULL,
            ItemName              TEXT    NOT NULL,
            MediaType             TEXT    NOT NULL,
            SeriesName            TEXT,
            SeasonNumber          INTEGER,
            EpisodeNumber         INTEGER,
            StartTime             TEXT    NOT NULL,
            PlaybackDurationTicks INTEGER NOT NULL DEFAULT 0,
            Completed             INTEGER NOT NULL DEFAULT 0,
            ClientName            TEXT,
            DeviceName            TEXT
        );");
        Execute("CREATE INDEX IF NOT EXISTS IX_PE_UserId    ON PlaybackEvents (UserId);");
        Execute("CREATE INDEX IF NOT EXISTS IX_PE_StartTime ON PlaybackEvents (StartTime);");
        Execute("CREATE INDEX IF NOT EXISTS IX_PE_MediaType ON PlaybackEvents (MediaType);");
    }

    private void Execute(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Records a completed playback event.
    /// </summary>
    /// <param name="evt">The event to persist.</param>
    public void RecordEvent(PlaybackEvent evt)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO PlaybackEvents
                (UserId, UserName, ItemId, ItemName, MediaType, SeriesName,
                 SeasonNumber, EpisodeNumber, StartTime, PlaybackDurationTicks,
                 Completed, ClientName, DeviceName)
            VALUES
                ($userId, $userName, $itemId, $itemName, $mediaType, $seriesName,
                 $seasonNumber, $episodeNumber, $startTime, $durationTicks,
                 $completed, $clientName, $deviceName)";

        cmd.Parameters.AddWithValue("$userId", evt.UserId);
        cmd.Parameters.AddWithValue("$userName", evt.UserName);
        cmd.Parameters.AddWithValue("$itemId", evt.ItemId);
        cmd.Parameters.AddWithValue("$itemName", evt.ItemName);
        cmd.Parameters.AddWithValue("$mediaType", evt.MediaType);
        cmd.Parameters.AddWithValue("$seriesName", (object?)evt.SeriesName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$seasonNumber", (object?)evt.SeasonNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$episodeNumber", (object?)evt.EpisodeNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$startTime", evt.StartTime.ToString("O"));
        cmd.Parameters.AddWithValue("$durationTicks", evt.PlaybackDurationTicks);
        cmd.Parameters.AddWithValue("$completed", evt.Completed ? 1 : 0);
        cmd.Parameters.AddWithValue("$clientName", (object?)evt.ClientName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$deviceName", (object?)evt.DeviceName ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Queries playback events with optional filters.
    /// </summary>
    /// <param name="userId">Filter to a specific user ID, or null for all users.</param>
    /// <param name="startDate">Include events on or after this date (UTC).</param>
    /// <param name="endDate">Include events on or before this date (UTC).</param>
    /// <param name="mediaType">Filter by media type (Movie, Episode, Audio), or null for all.</param>
    /// <param name="limit">Maximum number of rows to return.</param>
    /// <param name="offset">Number of rows to skip (for pagination).</param>
    /// <returns>Matching events and total count before pagination.</returns>
    public (IReadOnlyList<PlaybackEvent> Items, int TotalCount) QueryEvents(
        string? userId,
        DateTime? startDate,
        DateTime? endDate,
        string? mediaType,
        int limit,
        int offset)
    {
        var where = BuildWhereClause(userId, startDate, endDate, mediaType);

        using var countCmd = _connection.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM PlaybackEvents{where}";
        AddFilterParams(countCmd, userId, startDate, endDate, mediaType);
        var totalCount = Convert.ToInt32(countCmd.ExecuteScalar());

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT * FROM PlaybackEvents{where} ORDER BY StartTime DESC LIMIT $limit OFFSET $offset";
        AddFilterParams(cmd, userId, startDate, endDate, mediaType);
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$offset", offset);

        var items = new List<PlaybackEvent>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            items.Add(ReadEvent(reader));
        }

        return (items, totalCount);
    }

    /// <summary>
    /// Returns aggregate statistics for the given filters.
    /// </summary>
    public SummaryStats GetSummary(string? userId, DateTime? startDate, DateTime? endDate)
    {
        var where = BuildWhereClause(userId, startDate, endDate, null);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $@"
            SELECT
                COUNT(*)                                            AS TotalSessions,
                COALESCE(SUM(PlaybackDurationTicks), 0)            AS TotalTicks,
                COALESCE(SUM(CASE WHEN MediaType='Movie'   THEN 1 ELSE 0 END), 0) AS MovieCount,
                COALESCE(SUM(CASE WHEN MediaType='Episode' THEN 1 ELSE 0 END), 0) AS EpisodeCount,
                COALESCE(SUM(CASE WHEN MediaType='Audio'   THEN 1 ELSE 0 END), 0) AS AudioCount
            FROM PlaybackEvents{where}";
        AddFilterParams(cmd, userId, startDate, endDate, null);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new SummaryStats
            {
                TotalSessions = reader.GetInt32(0),
                TotalPlaybackTicks = reader.GetInt64(1),
                MovieCount = reader.GetInt32(2),
                EpisodeCount = reader.GetInt32(3),
                AudioCount = reader.GetInt32(4)
            };
        }

        return new SummaryStats();
    }

    /// <summary>
    /// Returns watch time grouped by day (UTC date string → total ticks).
    /// </summary>
    public IReadOnlyList<DailyWatchTime> GetWatchTimeByDay(string? userId, DateTime? startDate, DateTime? endDate)
    {
        var where = BuildWhereClause(userId, startDate, endDate, null);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $@"
            SELECT SUBSTR(StartTime, 1, 10) AS Day, SUM(PlaybackDurationTicks) AS Ticks
            FROM PlaybackEvents{where}
            GROUP BY Day
            ORDER BY Day ASC";
        AddFilterParams(cmd, userId, startDate, endDate, null);

        var result = new List<DailyWatchTime>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new DailyWatchTime
            {
                Date = reader.GetString(0),
                TotalTicks = reader.GetInt64(1)
            });
        }

        return result;
    }

    /// <summary>
    /// Returns the top N most-watched items grouped by ItemId/ItemName.
    /// </summary>
    public IReadOnlyList<TopItem> GetTopItems(string? userId, DateTime? startDate, DateTime? endDate, string? mediaType, int topN)
    {
        var where = BuildWhereClause(userId, startDate, endDate, mediaType);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $@"
            SELECT ItemId, ItemName, SeriesName, MediaType,
                   COUNT(*)                     AS WatchCount,
                   SUM(PlaybackDurationTicks)   AS TotalTicks
            FROM PlaybackEvents{where}
            GROUP BY ItemId
            ORDER BY WatchCount DESC
            LIMIT $topN";
        AddFilterParams(cmd, userId, startDate, endDate, mediaType);
        cmd.Parameters.AddWithValue("$topN", topN);

        var result = new List<TopItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new TopItem
            {
                ItemId = reader.GetString(0),
                ItemName = reader.GetString(1),
                SeriesName = reader.IsDBNull(2) ? null : reader.GetString(2),
                MediaType = reader.GetString(3),
                WatchCount = reader.GetInt32(4),
                TotalTicks = reader.GetInt64(5)
            });
        }

        return result;
    }

    /// <summary>
    /// Returns playback totals grouped by user.
    /// </summary>
    /// <returns>User playback summaries keyed by Jellyfin user ID.</returns>
    public IReadOnlyList<UserPlaybackSummary> GetUserPlaybackSummaries()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT UserId,
                   UserName,
                   COUNT(*) AS SessionCount,
                   COALESCE(SUM(PlaybackDurationTicks), 0) AS TotalTicks,
                   MAX(StartTime) AS LastSeen
            FROM PlaybackEvents
            GROUP BY UserId, UserName
            ORDER BY UserName ASC";

        var result = new List<UserPlaybackSummary>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new UserPlaybackSummary
            {
                UserId = reader.GetString(0),
                UserName = reader.GetString(1),
                SessionCount = reader.GetInt32(2),
                TotalTicks = reader.GetInt64(3),
                LastSeen = reader.IsDBNull(4)
                    ? null
                    : DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
            });
        }

        return result;
    }

    /// <summary>
    /// Returns playback totals grouped by device and client.
    /// </summary>
    /// <param name="limit">Maximum rows to return.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <returns>Device playback summaries.</returns>
    public IReadOnlyList<DevicePlaybackSummary> GetDevicePlaybackSummaries(int limit, int offset)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(DeviceName, 'Unknown') AS DeviceName,
                   COALESCE(ClientName, 'Unknown') AS ClientName,
                   COUNT(DISTINCT UserId) AS DistinctUsers,
                   COUNT(*) AS SessionCount,
                   COALESCE(SUM(PlaybackDurationTicks), 0) AS TotalTicks,
                   MAX(StartTime) AS LastSeen
            FROM PlaybackEvents
            GROUP BY COALESCE(DeviceName, 'Unknown'), COALESCE(ClientName, 'Unknown')
            ORDER BY SessionCount DESC, DeviceName ASC
            LIMIT $limit OFFSET $offset";
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$offset", offset);

        var result = new List<DevicePlaybackSummary>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new DevicePlaybackSummary
            {
                DeviceName = reader.GetString(0),
                ClientName = reader.GetString(1),
                DistinctUsers = reader.GetInt32(2),
                SessionCount = reader.GetInt32(3),
                TotalTicks = reader.GetInt64(4),
                LastSeen = reader.IsDBNull(5)
                    ? null
                    : DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
            });
        }

        return result;
    }

    /// <summary>
    /// Returns the number of distinct tracked devices.
    /// </summary>
    /// <returns>Distinct device count.</returns>
    public int GetDistinctDeviceCount()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM (SELECT DISTINCT COALESCE(DeviceName, 'Unknown'), COALESCE(ClientName, 'Unknown') FROM PlaybackEvents)";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Deletes events older than the configured retention period.
    /// </summary>
    /// <param name="retentionDays">Days to retain. 0 means keep forever.</param>
    public void PurgeOldEvents(int retentionDays)
    {
        if (retentionDays <= 0)
        {
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays).ToString("O");
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM PlaybackEvents WHERE StartTime < $cutoff";
        cmd.Parameters.AddWithValue("$cutoff", cutoff);
        var deleted = cmd.ExecuteNonQuery();
        if (deleted > 0)
        {
            _logger.LogInformation("Purged {Count} playback events older than {Days} days.", deleted, retentionDays);
        }
    }

    private static string BuildWhereClause(string? userId, DateTime? startDate, DateTime? endDate, string? mediaType)
    {
        var conditions = new List<string>();
        if (!string.IsNullOrEmpty(userId))
        {
            conditions.Add("UserId = $userId");
        }

        if (startDate.HasValue)
        {
            conditions.Add("StartTime >= $startDate");
        }

        if (endDate.HasValue)
        {
            conditions.Add("StartTime <= $endDate");
        }

        if (!string.IsNullOrEmpty(mediaType))
        {
            conditions.Add("MediaType = $mediaType");
        }

        return conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty;
    }

    private static void AddFilterParams(SqliteCommand cmd, string? userId, DateTime? startDate, DateTime? endDate, string? mediaType)
    {
        if (!string.IsNullOrEmpty(userId))
        {
            cmd.Parameters.AddWithValue("$userId", userId);
        }

        if (startDate.HasValue)
        {
            cmd.Parameters.AddWithValue("$startDate", startDate.Value.ToString("O"));
        }

        if (endDate.HasValue)
        {
            cmd.Parameters.AddWithValue("$endDate", endDate.Value.ToString("O"));
        }

        if (!string.IsNullOrEmpty(mediaType))
        {
            cmd.Parameters.AddWithValue("$mediaType", mediaType);
        }
    }

    private static PlaybackEvent ReadEvent(IDataReader reader)
    {
        return new PlaybackEvent
        {
            Id = reader.GetInt64(0),
            UserId = reader.GetString(1),
            UserName = reader.GetString(2),
            ItemId = reader.GetString(3),
            ItemName = reader.GetString(4),
            MediaType = reader.GetString(5),
            SeriesName = reader.IsDBNull(6) ? null : reader.GetString(6),
            SeasonNumber = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            EpisodeNumber = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            StartTime = DateTime.Parse(reader.GetString(9), null, System.Globalization.DateTimeStyles.RoundtripKind),
            PlaybackDurationTicks = reader.GetInt64(10),
            Completed = reader.GetInt32(11) == 1,
            ClientName = reader.IsDBNull(12) ? null : reader.GetString(12),
            DeviceName = reader.IsDBNull(13) ? null : reader.GetString(13)
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _connection.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>Aggregated summary statistics.</summary>
public class SummaryStats
{
    /// <summary>Gets or sets the total number of playback sessions.</summary>
    public int TotalSessions { get; set; }

    /// <summary>Gets or sets the total playback duration in ticks.</summary>
    public long TotalPlaybackTicks { get; set; }

    /// <summary>Gets or sets the number of movie sessions.</summary>
    public int MovieCount { get; set; }

    /// <summary>Gets or sets the number of episode sessions.</summary>
    public int EpisodeCount { get; set; }

    /// <summary>Gets or sets the number of audio sessions.</summary>
    public int AudioCount { get; set; }
}

/// <summary>Watch time aggregated by calendar day.</summary>
public class DailyWatchTime
{
    /// <summary>Gets or sets the date (ISO 8601 date, e.g. "2025-03-15").</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Gets or sets the total ticks watched on this day.</summary>
    public long TotalTicks { get; set; }
}

/// <summary>A single item in the most-watched list.</summary>
public class TopItem
{
    /// <summary>Gets or sets the Jellyfin item ID.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Gets or sets the item display name.</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>Gets or sets the series name for episodes.</summary>
    public string? SeriesName { get; set; }

    /// <summary>Gets or sets the media type.</summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>Gets or sets how many times this item was watched.</summary>
    public int WatchCount { get; set; }

    /// <summary>Gets or sets total ticks watched for this item.</summary>
    public long TotalTicks { get; set; }
}

/// <summary>User playback totals.</summary>
public class UserPlaybackSummary
{
    /// <summary>Gets or sets the Jellyfin user ID.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Gets or sets the user name.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of playback sessions.</summary>
    public int SessionCount { get; set; }

    /// <summary>Gets or sets the total watched ticks.</summary>
    public long TotalTicks { get; set; }

    /// <summary>Gets or sets the last seen playback timestamp.</summary>
    public DateTime? LastSeen { get; set; }
}

/// <summary>Device playback totals.</summary>
public class DevicePlaybackSummary
{
    /// <summary>Gets or sets the device name.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Gets or sets the client name.</summary>
    public string ClientName { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of distinct users seen on this device.</summary>
    public int DistinctUsers { get; set; }

    /// <summary>Gets or sets the number of playback sessions.</summary>
    public int SessionCount { get; set; }

    /// <summary>Gets or sets the total watched ticks.</summary>
    public long TotalTicks { get; set; }

    /// <summary>Gets or sets the last seen playback timestamp.</summary>
    public DateTime? LastSeen { get; set; }
}
