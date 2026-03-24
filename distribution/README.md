# Publishing Felix for Dalamud

There are two distribution paths:

## 1. Official mainline listing (D17 / Plogon)

Binaries for the default Dalamud installer are built by **Plogon** from a **public Git commit**, not from a zip you host yourself.

1. Finish [plugin metadata](https://dalamud.dev/plugin-development/plugin-metadata) in `Felix/Felix.json`.
2. Add `images/icon.png` (square, 64×64–512×512) next to your manifest (see example tree below).
3. Copy `distribution/dalamud-d17/manifest.toml.example` to `manifest.toml`, fill in `repository`, `commit`, `owners`, and `project_path` (use `felix-plugin/Felix` if the plugin lives in this monorepo).
4. Open **one PR per plugin** to [DalamudPluginsD17](https://github.com/goatcorp/DalamudPluginsD17) — new plugins go under `testing/live/YourPluginName/`.

Expected folder inside D17:

```text
Felix/
  manifest.toml
  images/
    icon.png
```

Updates are new PRs that only bump `commit` (and optionally `changelog`) in `manifest.toml`.

## 2. Custom plugin repo + dashboard download

Players can install from a **custom repository URL** that points at a JSON manifest, plus a **zip** per release.

### Build and zip

From Windows (with Dalamud dev kit / XIVLauncher hooks available to the SDK):

```powershell
.\felix-plugin\scripts\Package-FelixPlugin.ps1
```

Optional: copy straight into the dashboard static tree:

```powershell
.\felix-plugin\scripts\Package-FelixPlugin.ps1 -CopyZipToDashboard "cap-bot\cap\felixthebot-dashboard\public\downloads\dalamud\Felix\latest.zip"
```

### Host files on the dashboard

- **Zip:** `public/downloads/dalamud/Felix/latest.zip` (same layout as the script: `Felix.dll` + `Felix.json` at archive root).
- **Repo JSON:** served dynamically at `/downloads/dalamud/repo.json` (see dashboard `server.js`). Dalamud loads this URL when users add your **custom repo**.

### Environment variables (dashboard)

| Variable | Purpose |
|----------|---------|
| `FELIX_DALAMUD_DOWNLOAD_BASE` | Optional absolute base URL for zip + repo links (e.g. `https://felixthebot.com`). If unset, links use the incoming request host. |
| `FELIX_PLUGIN_ASSEMBLY_VERSION` | Version string embedded in `repo.json` (default `0.1.0.0`). Should match `Felix.json` after each release. |
| `FELIX_PLUGIN_DALAMUD_API_LEVEL` | API level in `repo.json` (default `14`). |

### Player steps (custom repo)

1. In-game: `/xlplugins` → Settings → **Custom Plugin Repositories** → Add URL:  
   `https://YOUR_DOMAIN/downloads/dalamud/repo.json`
2. Save, search **Felix**, install.
3. Alternatively, download **Felix plugin (.zip)** from Command Center sidebar and install manually if your launcher supports it.

The Command Center sidebar shows these URLs when users are logged in.

### Reference: static repo entry

See `felix-plugin/repo.template.json` for the JSON shape Dalamud expects (the live endpoint mirrors this).
