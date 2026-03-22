# Learnings

## 2026-03-18
Always read `Talk.md` again before concluding current repository state in a shared workspace. A collaborator may have added or changed large parts of the project after an earlier scan, and stale assumptions will produce wrong findings.
When an imported upstream codebase is built with `StyleCop.Analyzers` and `TreatWarningsAsErrors`, first separate true compile failures from analyzer-policy failures. Large error counts can be mostly policy noise rather than functional breakage.
When CS0246 appears for a Jellyfin event-args type, verify namespace imports before blaming package references. A working sibling project that resolves the same type with an extra `using MediaBrowser.Controller.Library;` is strong evidence that the issue is a missing using directive.
When packaging Jellyfin plugins, zip the full `dotnet publish` output rather than only `*.dll`. SQLite and other runtime assets can be required at runtime even when the main plugin assembly builds cleanly.
When a repo contains multiple plugin projects, normalize analyzer policy per project before assuming one successful fix will generalize. A new plugin can still inherit strict global analyzer settings and fail CI long after the imported sibling project has been quieted.
When a Jellyfin API compile error names a missing permission enum or helper, verify the extension namespace before replacing the whole approach. The current admin check comes from `Jellyfin.Data` plus `Jellyfin.Database.Implementations.Enums.PermissionKind`, not from `MediaBrowser.Model.Users`.
Playback analytics and library inventory reporting are separate concerns and must not be forced into one storage model. Playback history belongs in the plugin-owned SQLite database, while inventory reports must come from Jellyfin library APIs or a dedicated snapshot layer.
Jellyfin plugin pages are more reliable when implemented as fragment HTML plus an embedded controller JS resource instead of full HTML documents with inline scripts. The config page name also needs to match the plugin name when Jellyfin resolves the plugin settings route.
When copying a working Jellyfin `InternalItemsQuery.OrderBy` pattern into a new file, copy the enum namespace too. `SortOrder` resolves from `MediaBrowser.Model.Entities`, and leaving that import behind creates a CI-only regression that is trivial but blocks the entire plugin build.
When a new Jellyfin plugin feature only needs deterministic admin-page ordering, do not depend on server-side sort enums unless the exact package surface is verified for that project. The safer fallback is to fetch the items, sort locally, and paginate in memory.
When Jellyfin plugin installation reports a checksum mismatch and the expected value is literally `PLACEHOLDER_MD5`, the failure is in repository metadata, not in the built zip. Fix `manifest.json` first and refresh repository metadata in Jellyfin before debugging plugin code.

## 2026-03-22

Jellyfin serializes all plugin controller responses as PascalCase JSON. Do not use camelCase property names in any frontend JS that consumes plugin API responses. `summary.totalSessions` is always `undefined`; `summary.TotalSessions` is the correct key. Confirm by inspecting the raw Network response in DevTools before writing any JS data-binding code.

When a stats dashboard renders all sections as empty or shows "No data available" with zero counts, the most likely root cause is a property name casing mismatch in the JS, not a backend data problem. Verify actual DB contents with a direct `sqlite3` query before spending time debugging the C# layer.

`DateTime.ToString("O")` formats differently depending on `Kind`. A UTC datetime produces a `Z` suffix (e.g. `2026-03-22T00:00:00.0000000Z`) while an Unspecified datetime omits it (e.g. `2026-03-22T00:00:00.0000000`). SQLite stores and compares timestamps as strings lexicographically, so a bare date from a UI date picker parsed as midnight Unspecified will compare less than a stored UTC timestamp from the same day, silently excluding all same-day rows. Always add one calendar day to an `endDate` filter value when the intent is "include everything up to and including this day".

When diagnosing a Jellyfin plugin that appears to record nothing, log at the very first line of every event handler before any null-checks. Confirming that the event itself fires is the prerequisite before investigating session resolution, user lookup, or database writes.

In Jellyfin 10.11, `PlaybackProgressEventArgs.Users` can be an empty list. A `PlaybackTracker` that only reads from `args.Users` will silently drop every session. Always fall back to `args.Session.UserId` and `args.Session.UserName` when the list is empty.

When the GitHub Actions publish workflow has already run for a release (e.g. `v1.0.16`), it writes real checksums into `manifest.json` and commits them to `main`. A subsequent local commit that references the same version with a placeholder checksum will conflict on `git push`. Always `git pull --rebase` before pushing if a release was published since the last sync.

The GitHub Actions publish workflow is the single source of truth for `manifest.json` checksums. Never manually compute or hard-code MD5 values in `manifest.json`. Set the checksum to `00000000000000000000000000000000` as a placeholder and let the workflow replace it after the release build completes.

`ISessionManager.PlaybackStart` events that record sessions shorter than the configured `MinimumPlaybackSeconds` (default 30 s) are silently dropped at `LogDebug` level. This is expected behavior, not a bug. Short test plays will not appear in the database.

The session key format in `PlaybackTracker` is `"{sessionId}:{userId}"` with `userId` in N-format (no hyphens). A `Guid.ToString()` without a format argument produces the default D-format with hyphens, which will not match a key stored in N-format and will silently orphan stop/pause events.
