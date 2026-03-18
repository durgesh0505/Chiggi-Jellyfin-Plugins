# Chiggi Jellyfin Plugins

A mono-repo containing two Jellyfin plugins targeting Jellyfin **10.11.x** (.NET 9).

## Plugins

### Chiggi Stats
Detailed playback statistics and reporting — accessible directly inside your Jellyfin dashboard.

**Features:**
- Records every playback session to a local SQLite database
- Dashboard page inside Jellyfin: user selector, date range picker, media type filter
- Summary cards: total hours, sessions, movies, episodes
- Watch time by day chart (Chart.js)
- Most watched items table
- Paginated activity log
- Supplements history from Jellyfin's built-in activity log for pre-installation data
- Configurable minimum playback threshold and data retention period

### Trakt (Fixed Fork)
Sync your Jellyfin library to trakt.tv and scrobble your watch status.

This is a fork of the [official Jellyfin Trakt plugin](https://github.com/jellyfin/jellyfin-plugin-trakt) (v27) with the following bugs fixed:

| Issue | Fix |
|-------|-----|
| **#265** `HasFlag` on non-flags enum causes rate-limit and gateway retry logic to never fire | Changed to `==` comparisons |
| **#266** 409 Conflict from scrobble endpoint crashes plugin | 409 treated as "already scrobbling", returns gracefully |
| **#226** Duplicate watch history entries (100× duplicates) | Replaced `List<T>` with `HashSet<T>` (by item Id) in `UserDataPackage` |
| **#258** TV show scrobbling fails with null `Series` reference in Jellyfin 10.11 | Added null guard before accessing `episode.Series` |

## Installation

### Via Jellyfin Plugin Repository (Recommended)

1. Open Jellyfin Dashboard → Plugins → Repositories
2. Click **+** and add: `https://raw.githubusercontent.com/durgesh0505/Chiggi-Jellyfin-Plugins/main/manifest.json`
3. Install plugins from the catalogue

> **Note:** The manifest `sourceUrl` and `checksum` fields are populated automatically when a GitHub Release is published. Until the first release is created, install manually.

### Manual Install

1. Download the `.zip` from [Releases](https://github.com/durgesh0505/Chiggi-Jellyfin-Plugins/releases)
2. Extract the `.dll` into `<jellyfin-data>/plugins/<plugin-name>/`
3. Restart Jellyfin

## Development

**Prerequisites:** .NET 9 SDK

```bash
# Build everything
dotnet build Chiggi.Jellyfin.Plugins.sln

# Build a specific plugin
dotnet build src/Jellyfin.Plugin.ChiggiStats/

# Publish (produces DLL ready for Jellyfin)
dotnet publish src/Jellyfin.Plugin.ChiggiStats/ -c Release -o out/ChiggiStats
```

## Releasing

1. Create a GitHub Release with tag `v1.0.0`
2. The `publish.yaml` workflow builds both plugins, attaches the `.zip` files, and prints the MD5 checksums
3. Update `manifest.json` with the real `sourceUrl` and `checksum` values, then push

## License

GPLv3 — required because these plugins link against Jellyfin which is licensed under GPLv3.
