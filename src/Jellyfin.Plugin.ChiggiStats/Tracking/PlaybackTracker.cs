using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ChiggiStats.Data;
using Jellyfin.Plugin.ChiggiStats.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ChiggiStats.Tracking;

/// <summary>
/// Hosted service that listens to Jellyfin session events and records
/// playback events to the <see cref="SqliteRepository"/>.
/// </summary>
public sealed class PlaybackTracker : IHostedService, IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly SqliteRepository _repository;
    private readonly ILogger<PlaybackTracker> _logger;

    // Tracks in-progress sessions: "sessionId:userGuid" → start metadata
    private readonly ConcurrentDictionary<string, ActiveSession> _activeSessions = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackTracker"/> class.
    /// </summary>
    /// <param name="sessionManager">Jellyfin session manager.</param>
    /// <param name="repository">The SQLite repository.</param>
    /// <param name="logger">The logger.</param>
    public PlaybackTracker(
        ISessionManager sessionManager,
        SqliteRepository repository,
        ILogger<PlaybackTracker> logger)
    {
        _sessionManager = sessionManager;
        _repository = repository;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _logger.LogInformation("Chiggi Stats playback tracking started.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _logger.LogInformation("Chiggi Stats playback tracking stopped.");
        return Task.CompletedTask;
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs args)
    {
        _logger.LogInformation(
            "Chiggi Stats: PlaybackStart fired — Item={Item} Session={Session}.",
            args.Item?.Name ?? "null",
            args.Session?.Id ?? "null");

        if (args.Item == null || args.Session == null)
        {
            _logger.LogWarning("Chiggi Stats: PlaybackStart — item or session is null, skipping.");
            return;
        }

        var mediaType = args.Item.GetType().Name;
        string? seriesName = null;
        int? seasonNumber = null;
        int? episodeNumber = null;

        if (args.Item is MediaBrowser.Controller.Entities.TV.Episode episode)
        {
            seriesName = episode.Series?.Name;
            seasonNumber = episode.ParentIndexNumber != 0 ? episode.ParentIndexNumber ?? 1 : episode.ParentIndexNumber;
            episodeNumber = episode.IndexNumber;
        }

        // In Jellyfin 10.11, args.Users can be empty; the user is on args.Session instead.
        // Build a list of (userId, userName) from whichever source has data.
        var users = ResolveUsers(args.Users, args.Session);

        if (users.Length == 0)
        {
            _logger.LogWarning(
                "Chiggi Stats: skipping PlaybackStart for {Item} — no user on session {SessionId}. Users={UserCount} SessionUserId={SessionUserId}.",
                args.Item.Name,
                args.Session.Id,
                args.Users.Count,
                args.Session.UserId);
            return;
        }

        foreach (var (userId, userName) in users)
        {
            var sessionKey = $"{args.Session.Id}:{userId}";
            _activeSessions[sessionKey] = new ActiveSession
            {
                UserId = userId.ToString("N"),
                UserName = userName,
                ItemId = args.Item.Id.ToString("N"),
                ItemName = args.Item.Name ?? string.Empty,
                MediaType = mediaType,
                SeriesName = seriesName,
                SeasonNumber = seasonNumber,
                EpisodeNumber = episodeNumber,
                StartTime = DateTime.UtcNow,
                ClientName = args.Session.Client,
                DeviceName = args.Session.DeviceName
            };

            _logger.LogInformation(
                "Chiggi Stats: tracking start — {User} playing {Item} ({MediaType}) on {Device}.",
                userName,
                args.Item.Name,
                mediaType,
                args.Session.DeviceName ?? args.Session.Client ?? "unknown");
        }
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs args)
    {
        if (args.Item == null || args.Session == null)
        {
            return;
        }

        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableSqliteTracking)
        {
            return;
        }

        var users = ResolveUsers(args.Users, args.Session);

        if (users.Length == 0)
        {
            _logger.LogDebug(
                "Chiggi Stats: skipping PlaybackStopped for {Item} — no user on session {SessionId}.",
                args.Item.Name,
                args.Session.Id);
            return;
        }

        foreach (var (userId, userName) in users)
        {
            var sessionKey = $"{args.Session.Id}:{userId}";

            if (!_activeSessions.TryRemove(sessionKey, out var session))
            {
                _logger.LogDebug(
                    "Chiggi Stats: no matching start event for session {SessionKey} ({Item}); skipping.",
                    sessionKey,
                    args.Item.Name);
                continue;
            }

            var durationTicks = (long)((DateTime.UtcNow - session.StartTime).TotalMilliseconds * 10_000);

            var minimumTicks = (long)config.MinimumPlaybackSeconds * TimeSpan.TicksPerSecond;
            if (durationTicks < minimumTicks)
            {
                _logger.LogDebug(
                    "Chiggi Stats: ignoring short playback ({DurationSec}s < {MinSec}s) of {Item} by {User}.",
                    durationTicks / TimeSpan.TicksPerSecond,
                    config.MinimumPlaybackSeconds,
                    session.ItemName,
                    session.UserName);
                continue;
            }

            try
            {
                _repository.RecordEvent(new PlaybackEvent
                {
                    UserId = session.UserId,
                    UserName = session.UserName,
                    ItemId = session.ItemId,
                    ItemName = session.ItemName,
                    MediaType = session.MediaType,
                    SeriesName = session.SeriesName,
                    SeasonNumber = session.SeasonNumber,
                    EpisodeNumber = session.EpisodeNumber,
                    StartTime = session.StartTime,
                    PlaybackDurationTicks = durationTicks,
                    Completed = args.PlayedToCompletion,
                    ClientName = session.ClientName,
                    DeviceName = session.DeviceName
                });
                _repository.PurgeOldEvents(config.DataRetentionDays);

                _logger.LogInformation(
                    "Chiggi Stats: recorded — {User} watched {Item} for {Minutes:F1} min (completed={Completed}).",
                    session.UserName,
                    session.ItemName,
                    durationTicks / (double)TimeSpan.TicksPerMinute,
                    args.PlayedToCompletion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chiggi Stats: failed to record playback event for {Item}.", session.ItemName);
            }
        }
    }

    // Builds the user list from args.Users when available; falls back to the session's own
    // UserId/UserName for Jellyfin 10.11+ where args.Users is sometimes empty.
    private static (Guid Id, string Name)[] ResolveUsers(
        System.Collections.Generic.IReadOnlyList<Jellyfin.Database.Implementations.Entities.User> argsUsers,
        SessionInfo session)
    {
        if (argsUsers.Count > 0)
        {
            var result = new (Guid, string)[argsUsers.Count];
            for (var i = 0; i < argsUsers.Count; i++)
            {
                result[i] = (argsUsers[i].Id, argsUsers[i].Username);
            }

            return result;
        }

        if (session.UserId != Guid.Empty)
        {
            return new[] { (session.UserId, session.UserName ?? string.Empty) };
        }

        return Array.Empty<(Guid, string)>();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _activeSessions.Clear();
    }

    private sealed class ActiveSession
    {
        public string UserId { get; set; } = string.Empty;

        public string UserName { get; set; } = string.Empty;

        public string ItemId { get; set; } = string.Empty;

        public string ItemName { get; set; } = string.Empty;

        public string MediaType { get; set; } = string.Empty;

        public string? SeriesName { get; set; }

        public int? SeasonNumber { get; set; }

        public int? EpisodeNumber { get; set; }

        public DateTime StartTime { get; set; }

        public string? ClientName { get; set; }

        public string? DeviceName { get; set; }
    }
}
