# Project Plan — Chiggi Jellyfin Plugins

**Repository:** https://github.com/durgesh0505/Chiggi-Jellyfin-Plugins
**Created:** 2026-03-18
**Target Jellyfin:** 10.11.x
**Target .NET:** 9.0

---

## Architecture

Single solution (`Chiggi.Jellyfin.Plugins.sln`) containing two independent plugin projects under `src/`.

```
Chiggi-Jellyfin-Plugins/
├── src/
│   ├── Jellyfin.Plugin.ChiggiStats/   ← New plugin
│   └── Jellyfin.Plugin.Trakt/         ← Fork of jellyfin/jellyfin-plugin-trakt v27
├── .github/workflows/
│   ├── build.yaml                     ← Build + artifact on every push
│   └── publish.yaml                   ← Attach .zip to GitHub Releases
├── Directory.Build.props              ← Shared: net9.0, LangVersion, Nullable
├── Chiggi.Jellyfin.Plugins.sln
├── manifest.json                      ← Jellyfin plugin repository catalog
└── docs/
    ├── PLAN.md                        ← This file
    └── TRAKT_FIXES.md                 ← Detailed Trakt bug fix notes
```

---

## Plugin 1 — Chiggi Stats

### Goal
Give Jellyfin administrators and users a rich, filterable view of their watch history — accessible at `Dashboard → Chiggi Stats` without leaving Jellyfin.

### Data Sources
1. **SQLite (primary):** Every playback stop event is recorded to `<data>/chiggistats.db`
2. **Jellyfin Activity Log (secondary):** Read on demand via `IActivityManager` to show pre-installation history (duration = 0 for these entries, since the activity log does not store it)

### Service Architecture

| Class | Role |
|-------|------|
| `Plugin` | Entry point, registers web pages |
| `PluginServiceRegistrator` | Registers DI services |
| `PlaybackTracker` | `IHostedService` — subscribes to `ISessionManager` events, records events to SQLite |
| `SqliteRepository` | CRUD for `PlaybackEvents` table; provides filtered queries and aggregates |
| `ActivityLogRepository` | Reads Jellyfin activity log via `IActivityManager` |
| `StatsController` | ASP.NET REST controller — `/ChiggiStats/activity`, `/ChiggiStats/summary`, `/ChiggiStats/users` |
| `stats.html` | Embedded dashboard page (Jellyfin plugin page) |
| `config.html` | Embedded settings page |

### API Endpoints

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| GET | `/ChiggiStats/activity` | User | Paginated playback history (filters: userId, startDate, endDate, mediaType, limit, offset) |
| GET | `/ChiggiStats/summary` | User | Aggregated stats + top items + by-day chart data |
| GET | `/ChiggiStats/users` | Admin | List all Jellyfin users |

### Database Schema

```sql
CREATE TABLE PlaybackEvents (
    Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId                TEXT    NOT NULL,
    UserName              TEXT    NOT NULL,
    ItemId                TEXT    NOT NULL,
    ItemName              TEXT    NOT NULL,
    MediaType             TEXT    NOT NULL,   -- Movie | Episode | Audio | ...
    SeriesName            TEXT,
    SeasonNumber          INTEGER,
    EpisodeNumber         INTEGER,
    StartTime             TEXT    NOT NULL,   -- ISO 8601 UTC
    PlaybackDurationTicks INTEGER NOT NULL DEFAULT 0,
    Completed             INTEGER NOT NULL DEFAULT 0,  -- 0/1 bool
    ClientName            TEXT,
    DeviceName            TEXT
);
```

### Dashboard UI (stats.html)
- Filter bar: user, media type, date range, Apply/Reset buttons
- Summary cards: Hours Watched, Sessions, Movies, Episodes
- Bar chart: Watch time by day (Chart.js from CDN)
- Most Watched table (top 10)
- Paginated Recent Activity table

---

## Plugin 2 — Trakt (Fixed Fork)

### Base
Forked from `jellyfin/jellyfin-plugin-trakt` at commit corresponding to v27 (Jellyfin 10.11 support, released October 2024).

### Namespace
Original namespace `Trakt` is preserved to avoid widespread file changes.

### Bug Fixes Applied

#### Fix #265 — Rate limit / retry logic never fires
**File:** `Api/TraktApi.cs` → `RetryHttpRequest()`
**Root cause:** `statusCode.HasFlag(HttpStatusCode.TooManyRequests)` uses bitwise flag check on a non-flags enum. `HttpStatusCode` has no `[Flags]` attribute. `HasFlag` evaluates whether all bits of the argument are set in the value — which is coincidentally false for HTTP status codes like 429 (0x1AD) vs 200 (0xC8). The condition never matched, so retries never happened.
**Fix:** Replace all `HasFlag` calls in `RetryHttpRequest` with `==`.

#### Fix #266 — 409 Conflict throws exception, kills scrobble session
**File:** `Api/TraktApi.cs` → `PostToTrakt<T>()`
**Root cause:** When Trakt returns 409 Conflict (meaning "this item is already being scrobbled"), `response.EnsureSuccessStatusCode()` throws an `HttpRequestException`. The exception propagates to `ServerMediator`, which logs it and returns without cleaning up playback state. This caused high CPU usage (background retry loops) and deletion of progress tracking.
**Fix:** Check `response.StatusCode == HttpStatusCode.Conflict` before `EnsureSuccessStatusCode` and return `default(T)` — treating it as a harmless no-op.

#### Fix #226 — 100× duplicate watch history entries
**File:** `Model/UserDataPackage.cs`
**Root cause:** `SeenMovies`, `SeenEpisodes` etc. were `List<T>`. When Jellyfin fires multiple `UserDataSaved` events for a single user action (cascade from library updates), the same item gets added to the list multiple times. The timer callback then syncs all duplicates to Trakt.
**Fix:** Changed backing collections from `List<T>` to `HashSet<T>` using a custom `ItemIdEqualityComparer<T>` (compares by `BaseItem.Id`). Duplicate events for the same item are now silently collapsed.

#### Fix #258 — TV show scrobbling fails in Jellyfin 10.11
**File:** `Api/TraktApi.cs` → `SendEpisodeStatusUpdateAsync()`
**Root cause:** When `episode.Series` is null (can happen in Jellyfin 10.11 during rapid playback start before metadata is fully loaded), accessing `episode.Series.Name` throws a `NullReferenceException`. The exception propagates silently.
**Fix:** Added null check on `episode.Series` before building the scrobble payload. Logs a warning and skips the episode entry for that loop iteration.

### What Was NOT Changed
- OAuth/device authentication flow
- All data contracts (API models)
- `SyncFromTraktTask` / `SyncLibraryTask` scheduled tasks
- `ServerMediator` event wiring
- Configuration page and JS

---

## Roadmap (Future)

### Chiggi Stats
- [ ] Per-user stats page (non-admin users see only their own data)
- [ ] Export to CSV
- [ ] Notification when a user reaches a watch milestone
- [ ] "Currently watching" live section (active sessions)

### Trakt
- [ ] Fix #281 (JsonException with Boolean metadata)
- [ ] Fix #276 (API authorization token persistence)
- [ ] Investigate #273 (API changes tracking)
- [ ] Rate limit: respect `Retry-After` header more aggressively
