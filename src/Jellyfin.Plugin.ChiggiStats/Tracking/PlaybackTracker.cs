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

    // Tracks in-progress sessions: sessionId → start metadata
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
        if (args.Item == null || args.Session == null)
        {
            return;
        }

        var mediaType = args.Item.GetType().Name; // Movie, Episode, Audio, etc.
        string? seriesName = null;
        int? seasonNumber = null;
        int? episodeNumber = null;

        if (args.Item is MediaBrowser.Controller.Entities.TV.Episode episode)
        {
            seriesName = episode.Series?.Name;
            seasonNumber = episode.ParentIndexNumber != 0 ? episode.ParentIndexNumber ?? 1 : episode.ParentIndexNumber;
            episodeNumber = episode.IndexNumber;
        }

        foreach (var user in args.Users)
        {
            var sessionKey = $"{args.Session.Id}:{user.Id}";
            _activeSessions[sessionKey] = new ActiveSession
            {
                UserId = user.Id.ToString("N"),
                UserName = user.Username,
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

        foreach (var user in args.Users)
        {
            var sessionKey = $"{args.Session.Id}:{user.Id}";

            if (!_activeSessions.TryRemove(sessionKey, out var session))
            {
                // No start event was captured; skip
                continue;
            }

            var durationTicks = (long)((DateTime.UtcNow - session.StartTime).TotalMilliseconds * 10_000);

            var minimumTicks = (long)config.MinimumPlaybackSeconds * TimeSpan.TicksPerSecond;
            if (durationTicks < minimumTicks)
            {
                _logger.LogDebug(
                    "Ignoring short playback ({DurationSec}s < {MinSec}s) of {Item} by {User}.",
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

                _logger.LogDebug(
                    "Recorded playback: {User} watched {Item} for {Minutes:F1} min (completed={Completed}).",
                    session.UserName,
                    session.ItemName,
                    durationTicks / (double)TimeSpan.TicksPerMinute,
                    args.PlayedToCompletion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record playback event for {Item}.", session.ItemName);
            }
        }
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
