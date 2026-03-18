# Chiggi Stats Reporting Task

## Status

Created on 2026-03-18. This file is the active implementation checklist for the `Chiggi Stats` reporting redesign and must be updated as work progresses.
Implementation is in progress. The first backend and UI foundation pass is complete, `1b19998` proved that the prior namespace-only SortOrder patch was insufficient, and the approved local-sort fallback is now committed and pushed as `5f70b9b`, but local .NET build validation is still blocked in this environment.

## Objective

Turn `Chiggi Stats` from a single playback dashboard into an admin-only reporting plugin that combines:

- Playback analytics backed by the plugin's SQLite history
- Server-wide library and media inventory reports backed by Jellyfin library APIs

## User-Approved Scope

- Inventory reporting is admin-only
- Export is out of scope
- Report groups required in the first pass:
  - Movies
  - Series
  - Seasons
  - Episodes
  - Music
  - Box Sets
  - Users
  - Devices

## Current Verified State

- [stats.html](/mnt/c/users/durge/documents/claudecode/chiggi-jellyfin-plugins/src/Jellyfin.Plugin.ChiggiStats/Pages/stats.html) is now a fragment-based report shell with tabs for overview, playback, inventory reports, and settings
- [stats.js](/mnt/c/users/durge/documents/claudecode/chiggi-jellyfin-plugins/src/Jellyfin.Plugin.ChiggiStats/Pages/stats.js) is now the page controller and loads overview, playback, and generic report-table data without external CDN dependencies
- [config.html](/mnt/c/users/durge/documents/claudecode/chiggi-jellyfin-plugins/src/Jellyfin.Plugin.ChiggiStats/Configuration/config.html) and [config.js](/mnt/c/users/durge/documents/claudecode/chiggi-jellyfin-plugins/src/Jellyfin.Plugin.ChiggiStats/Configuration/config.js) now follow Jellyfin's fragment/controller pattern and no longer expose the non-functional activity-log setting
- [StatsController.cs](/mnt/c/users/durge/documents/claudecode/chiggi-jellyfin-plugins/src/Jellyfin.Plugin.ChiggiStats/Api/StatsController.cs) now exposes `reports/overview` and `reports/table` in addition to the playback endpoints
- [InventoryReportService.cs](/mnt/c/users/durge/documents/claudecode/chiggi-jellyfin-plugins/src/Jellyfin.Plugin.ChiggiStats/Data/InventoryReportService.cs) now provides admin-only library, user, and device report data
- [SqliteRepository.cs](/mnt/c/users/durge/documents/claudecode/chiggi-jellyfin-plugins/src/Jellyfin.Plugin.ChiggiStats/Data/SqliteRepository.cs) now includes grouped playback summaries for users and devices

## Design Constraints

- Playback reports and inventory reports must stay separated at the service and endpoint level
- Playback history remains user-scoped for non-admin users and admin-readable for elevated users
- Inventory reporting must be admin-only and server-wide
- Inventory reports are current-state reports unless a future snapshot layer is added
- The misleading activity-log fallback should be removed or clearly disabled in the UI

## First-Pass Implementation Plan

- [x] Add a dedicated inventory reporting service that queries Jellyfin library data
- [x] Extend dependency registration for the new report service and required Jellyfin services
- [x] Add admin-only inventory endpoints for each approved report group
- [x] Expand playback-report endpoints beyond the current dashboard summary shape where needed
- [x] Replace the current single-page dashboard with a report-oriented admin UI
- [x] Split the UI into report sections or tabs for Overview, Playback, Movies, Series, Seasons, Episodes, Music, Box Sets, Users, and Devices
- [x] Remove or clearly disable the non-functional activity-log fallback setting
- [x] Update `Talk.md` after every concrete implementation step
- [x] Update this file as each task moves from pending to completed

## Remaining Work

- [ ] Confirm that GitHub Actions passes for commit `5f70b9b`
- [ ] Run a real `.NET` build and fix any Jellyfin API mismatches that static inspection did not catch
- [ ] Validate the settings-page route and report-tab loading in a real Jellyfin server session
- [ ] Decide whether to delete the unused `ActivityLogRepository` fallback path from the API layer or leave it as dormant compatibility code
- [ ] Review report columns against the official `Reports` plugin after the first manual UI test

## Risks And Gotchas

- Inventory reports built from live library queries will cost more request time on large libraries than the current page
- Device reporting will come from tracked playback sessions, not from a standalone Jellyfin inventory source
- User reporting must clearly distinguish server-user metadata from playback-by-user analytics
- The current plugin page structure is too small for this scope and will require a UI rewrite rather than small edits

## Exact Next Action

Run a real build or server test next. The current highest-risk unknown is compile and runtime compatibility with the Jellyfin server APIs because this environment still does not provide local `.NET` tooling.
