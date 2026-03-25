# v1.0.16.0 Plan Review — Codex Findings and Decisions

**Date:** 2026-03-25
**Status:** Decisions locked. Not yet implemented. Codebase is still on v1.0.15.0.
**Reviewed by:** Codex (two rounds)
**Resolved by:** Claude Code + User

---

## Finding 1 (High) — Media-type filter scope is inconsistent

### What Codex found

The plan said Summary endpoint would not receive a mediaType filter, but TopItems comes from inside SummaryResponse (StatsController.cs:118). If GetSummary ignores mediaTypes, then selecting "Movies only" on Overview would do nothing to the Trend chart, Top Items list, or any aggregate card — making the checkboxes appear broken on the Overview tab.

### Decision

**Media-type filters affect both Overview and Playback consistently.**

There is no reason to treat them differently. If an admin selects "Movies only", they want to see movie watch time in the trend chart, movie top items, movie-specific completion rate, etc.

### Implementation correction

All three sub-queries inside `StatsController.GetSummary` must pass `mediaTypes`:
- `SqliteRepository.GetSummary(effectiveUserId, startDate, endDate, **mediaTypes**)` — aggregate cards
- `SqliteRepository.GetWatchTimeByDay(effectiveUserId, startDate, endDate, **mediaTypes**)` — trend chart
- `SqliteRepository.GetTopItems(effectiveUserId, startDate, endDate, **mediaTypes**, 10)` — Top Items

This means `GetSummary` and `GetWatchTimeByDay` in `SqliteRepository.cs` each need an `IReadOnlyList<string>? mediaTypes` parameter added. Both already use `BuildWhereClause` / `AddFilterParams`, so the change is adding one parameter to each method signature and passing it through to those helpers — identical to the change already planned for `QueryEvents` and `GetTopItems`.

`StatsController.GetSummary` also gets `[FromQuery] List<string>? mediaTypes` added alongside the existing `userId`, `startDate`, `endDate` params.

**What does NOT change:** The behavior note that "Overview cards always show all-type totals" is removed entirely. It was wrong. Every endpoint and every sub-query gets the same filter.

---

## Finding 2 (Medium) — "None checked = no filter" is a broken contract

### What Codex found

The plan said if all three checkboxes are unchecked, treat it the same as all three checked. Users will interpret "I unchecked everything and the data didn't disappear" as a bug.

### Decision

**Prevent unchecking the last remaining checkbox.**

When only one checkbox is checked, that checkbox becomes `disabled` so the user cannot uncheck it. This eliminates the ambiguous empty-selection state entirely.

Rules:
- 3 checked → all enabled, no filter sent (same as current "All" behavior)
- 2 checked → all enabled, filter sent for the 2 checked types
- 1 checked → that checkbox is disabled (cannot be unchecked), filter sent for that 1 type
- 0 checked → impossible

### Implementation

In `stats.js`, add a `syncCheckboxDisabled(view)` function that runs after every checkbox change:

```
count checked boxes
if count === 1: disable the one checked box
else: enable all boxes
```

Call `syncCheckboxDisabled` on page load (after defaults are set) and after every checkbox change event fires.

---

## Finding 3 (Medium) — Auto-apply on every checkbox change causes request churn

### What Codex found

Switching from "Movies only" to "Episodes only" requires unchecking Movies (fires reload 1) then checking Episodes (fires reload 2). Two full server round-trips, two loading spinners, for a single logical filter change.

### Decision

**Add a 300ms debounce to all filter change handlers.**

A single `debounce(fn, 300)` wrapper is applied to `triggerReload`. Any filter change restarts the 300ms timer. Rapid successive changes (e.g. uncheck Movies → check Episodes within 300ms) collapse into one reload.

300ms is fast enough to feel instant for single changes (date picker, user select) and short enough to coalesce multi-checkbox changes without feeling sluggish.

### Implementation

Add a `debounce` utility function to `stats.js`:

```javascript
debounce: function (fn, delay) {
    let timer;
    return function () {
        clearTimeout(timer);
        timer = setTimeout(fn, delay);
    };
},
```

Assign `ChiggiStatsPage.debouncedReload` once during page init. All filter `change` listeners call `debouncedReload` instead of `triggerReload` directly. The Reset button still calls `triggerReload` directly (no debounce — it is an explicit user action, not rapid input).

---

## Open Question 1 — GROUP_CONCAT(DISTINCT, separator) SQLite compatibility

### What Codex raised

`GROUP_CONCAT(DISTINCT expr, separator)` with both DISTINCT and a custom separator in the same call requires SQLite 3.44.0 (November 2023). `Microsoft.Data.Sqlite` bundles its own SQLite native library. The exact version depends on the NuGet package version in use and may be older than 3.44.

### Decision

**Use `GROUP_CONCAT(DISTINCT UserName)` with the default comma separator only.**

This syntax works in all SQLite 3.x versions. The C# side does not need to post-process the string — a comma-separated list of user names is readable as-is in the table cell. Jellyfin usernames do not contain commas in practice (the UI rejects them), so the output is unambiguous.

The display separator is handled in C#: in `InventoryReportService.cs`, apply `.Replace(",", " · ")` on `summary.UserNames` before writing it into the row cell. This makes the separator a C# concern, not a SQL concern, and works regardless of SQLite version. It is also safe because Jellyfin validates that usernames contain only alphanumeric characters and a small set of allowed symbols — commas are not permitted by the Jellyfin UI, so the split on comma is unambiguous in practice. The `.Replace` also eliminates any concern about future edge cases without requiring a SQL change.

---

## Open Question 2 — Is InventoryReportService stale architecture?

### What Codex raised

The service was designed for library inventory reports (Movies, Series, etc.) but now only serves the Users and Devices tabs after the inventory tabs were removed in v1.0.14.0.

### Decision

**Defer renaming to a later cleanup pass.**

The service still has a valid purpose (building the Users and Devices report tables). Renaming it to `UsageReportService` or similar is correct but carries zero functional impact and introduces a rename-only commit with risk of merge conflicts. It will be done as a standalone cleanup commit after v1.0.16.0 is stable.

---

## Revised File Impact for v1.0.16.0

| File | Changes |
|---|---|
| `stats.html` | Remove Apply button; replace mediaType select with 3 checkboxes; wrap tables in overflow div; horizontal-scroll tab bar; `@media (max-width: 640px)` CSS rules |
| `stats.js` | Add `debounce` utility; add `triggerReload` helper; replace Apply listener with debounced change listeners on all controls; update `getPlaybackFilters` to return `mediaTypes` array; update `buildUrl` to handle array params; add `syncCheckboxDisabled`; update Reset to re-check all boxes and call `triggerReload` directly |
| `StatsController.cs` | Add `[FromQuery] List<string>? mediaTypes` to both `GetActivity` and `GetSummary` endpoints; pass `mediaTypes` to all three sub-queries in `GetSummary` |
| `SqliteRepository.cs` | Change `string? mediaType` to `IReadOnlyList<string>? mediaTypes` in `QueryEvents`, `GetTopItems`, `GetSummary`, and `GetWatchTimeByDay`; update `BuildWhereClause` to emit `MediaType IN ($mt0, ...)` with dynamic params; update `AddFilterParams` to add `$mt0, $mt1...`; add `UserNames` string to `DevicePlaybackSummary`; update `GetDevicePlaybackSummaries` SQL to include `GROUP_CONCAT(DISTINCT UserName)` |
| `InventoryReportService.cs` | Add `usernames` column ("Who Used It") to `BuildDevicesTable` columns list; add `["usernames"] = summary.UserNames` to row cells |
| `Jellyfin.Plugin.ChiggiStats.csproj` | Bump to v1.0.16.0 |
| `manifest.json` | Add v1.0.16.0 entry pointing to v1.0.20 release |

---

## Implementation Order

1. `SqliteRepository.cs` — data layer first: add `mediaTypes` to all four query methods, update `BuildWhereClause` / `AddFilterParams`, add `UserNames` to device query
2. `InventoryReportService.cs` — add UserNames column to Devices table
3. `StatsController.cs` — wire `mediaTypes` param into both endpoints and all sub-queries
4. `stats.html` — checkboxes, remove Apply, table wrappers, mobile CSS
5. `stats.js` — debounce, triggerReload, getPlaybackFilters, buildUrl, syncCheckboxDisabled, Reset update

All in one commit. One release trigger (v1.0.20).
