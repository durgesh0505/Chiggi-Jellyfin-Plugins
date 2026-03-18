# Next Steps — Chiggi Jellyfin Plugins

**Last updated:** 2026-03-18
**Repo:** https://github.com/durgesh0505/Chiggi-Jellyfin-Plugins

---

## Step 1 — Verify the Build (GitHub Actions)

After any push, GitHub Actions automatically builds both plugins.

1. Go to https://github.com/durgesh0505/Chiggi-Jellyfin-Plugins/actions
2. Click the latest **Build** workflow run
3. Confirm both jobs pass (green checkmark)
4. If there are errors: share the log output here and they will be fixed before proceeding

**Expected outcome:** Two `.zip` artifacts downloadable from the workflow run page.

---

## Step 2 — Install Plugins Manually (First Test)

Before creating a release, test the plugins on your Jellyfin server directly.

### How to install manually

1. Download `plugin-zips` artifact from the passing GitHub Actions build
2. Extract each `.zip` — you get a single `.dll` file per plugin
3. On your Jellyfin server, navigate to the plugin data folder:
   - **Linux/Docker:** `/config/data/plugins/` or `~/.local/share/jellyfin/data/plugins/`
   - **Windows:** `%APPDATA%\Jellyfin\data\plugins\`
4. Create a subfolder for each plugin:
   ```
   plugins/
   ├── ChiggiStats_1.0.0.0/
   │   └── Jellyfin.Plugin.ChiggiStats.dll
   │   └── Microsoft.Data.Sqlite.dll   ← also included in publish output
   └── Trakt_27.1.0.0/
       └── Trakt.dll
   ```
5. Restart Jellyfin
6. Go to **Dashboard → Plugins** — both plugins should appear as "Active"

### Verify Chiggi Stats
- Open Dashboard → **Chiggi Stats** (appears in the left sidebar under Server)
- Watch a video for at least 30 seconds, then stop it
- Refresh the Stats page — the session should appear in Recent Activity

### Verify Trakt
- Go to Dashboard → Plugins → Trakt → Settings
- Complete the device authorization (enter code at trakt.tv/activate)
- Watch a movie or episode — it should scrobble to your Trakt profile without 409 errors in the Jellyfin log

---

## Step 3 — Create the First GitHub Release

Once the plugins are confirmed working:

1. Go to https://github.com/durgesh0505/Chiggi-Jellyfin-Plugins/releases/new
2. Set **Tag:** `v1.0.0`
3. Set **Title:** `v1.0.0 — Initial Release`
4. Write release notes (example below)
5. Click **Publish Release**

**Release notes template:**
```
## Chiggi Stats v1.0.0
- First release
- Playback tracking with SQLite storage
- In-Jellyfin dashboard with filters, charts, and activity log

## Trakt v27.1.0 (Fixed Fork)
- Forked from jellyfin/jellyfin-plugin-trakt v27
- Fix: 409 Conflict no longer crashes scrobbling (#266)
- Fix: Rate-limit retries now work correctly (#265)
- Fix: Duplicate watch history entries eliminated (#226)
- Fix: TV show scrobbling works with Jellyfin 10.11 (#258)
```

The `publish.yaml` GitHub Actions workflow will automatically:
- Build both plugins
- Attach `Jellyfin.Plugin.ChiggiStats.zip` and `Jellyfin.Plugin.Trakt.zip` to the release
- Print MD5 checksums in the workflow log

---

## Step 4 — Update manifest.json with Real Checksums

After the release workflow completes:

1. Open the release workflow run in GitHub Actions
2. Find the printed MD5 checksums (output of `md5sum` commands)
3. Edit `manifest.json` in the repo — replace `PLACEHOLDER_MD5` with actual values:

```json
"checksum": "a1b2c3d4e5f6...",   ← MD5 of the .zip file
"sourceUrl": "https://github.com/durgesh0505/Chiggi-Jellyfin-Plugins/releases/download/v1.0.0/Jellyfin.Plugin.ChiggiStats.zip"
```

4. Commit and push:
   ```bash
   git add manifest.json
   git commit -m "chore: update manifest checksums for v1.0.0"
   git push
   ```

---

## Step 5 — Add Plugin Repository to Jellyfin

Once `manifest.json` has real checksums:

1. Open Jellyfin Dashboard → **Plugins** → **Repositories**
2. Click **+** (Add Repository)
3. Enter:
   - **Repository name:** Chiggi Plugins
   - **Repository URL:** `https://raw.githubusercontent.com/durgesh0505/Chiggi-Jellyfin-Plugins/main/manifest.json`
4. Click **Save**
5. Go to the **Catalogue** tab — both plugins should appear
6. Install from the catalogue like any official plugin

---

## Step 6 — Releasing Future Updates

For every subsequent update:

1. Make code changes and push to `main`
2. Verify the build passes
3. Create a new GitHub Release with the next version tag (e.g. `v1.1.0`)
4. The workflow auto-attaches new `.zip` files
5. Add a new entry to `manifest.json` under `"versions"` (keep old entries for users on older Jellyfin):

```json
"versions": [
  {
    "version": "1.1.0.0",
    "changelog": "Description of changes",
    "targetAbi": "10.11.0.0",
    "sourceUrl": "https://github.com/.../releases/download/v1.1.0/Jellyfin.Plugin.ChiggiStats.zip",
    "checksum": "new_md5_here",
    "timestamp": "2026-04-01T00:00:00Z"
  },
  {
    "version": "1.0.0.0",
    ...previous entry kept for rollback...
  }
]
```

---

## Known Issues to Monitor

| Issue | File | Status |
|-------|------|--------|
| `manifest.json` checksums are placeholder | `manifest.json` | Fix after Step 3 |
| `ActivityLogEntryQuery` type name needs verification | `ActivityLogRepository.cs` | Verify in Step 1 build |
| Chart.js loads from CDN — offline servers will show no chart | `Pages/stats.html` | Low priority; fix if needed |
| Trakt `#281` (JsonException Boolean) not yet fixed | `TraktApi.cs` | Future PR |
| Trakt `#276` (token persistence) not yet fixed | `TraktApi.cs` | Future PR |

---

## File Reference

```
Chiggi-Jellyfin-Plugins/
├── src/
│   ├── Jellyfin.Plugin.ChiggiStats/       ← New stats plugin
│   └── Jellyfin.Plugin.Trakt/             ← Fixed Trakt fork (v27.1.0.0)
├── .github/workflows/
│   ├── build.yaml                         ← Runs on every push
│   └── publish.yaml                       ← Runs on GitHub Release
├── manifest.json                          ← Plugin repository catalog
├── docs/
│   ├── NEXT_STEPS.md                      ← This file
│   └── PLAN.md                            ← Architecture and bug-fix details
├── Talk.md                                ← Claude Code ↔ Codex handoff log
└── README.md                              ← User-facing documentation
```
