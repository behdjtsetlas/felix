# Felix Dalamud Plugin

Felix is a custom Dalamud plugin that pairs a local FFXIV client with the Felix dashboard command center.

## What this first version does

- pairs a local plugin install to the Felix dashboard with a one-time token
- stores a long-lived device token after pairing
- uploads periodic character snapshots to the dashboard

## Project layout

- `felix-plugin/Felix/`
  - `Felix.csproj`
  - `Felix.json`
  - plugin source files
- `felix-plugin/repo.template.json`
  - starter custom repo entry you can adapt later

## Build output

When you build the plugin in `Debug`, the DLL and plugin manifest are written into:

- `felix-plugin/bin/`

Use that folder as your dev plugin location in XIVLauncher.

## Current limitations

- the snapshot builder currently syncs live player identity and territory basics first
- currencies, collections, and combat parsing are scaffolded in the payload model and dashboard, but the local providers still need to be expanded

## Publishing (Dalamud mainline + dashboard)

See **`distribution/README.md`** for:

- submitting to **goatcorp/DalamudPluginsD17** (official installer / Plogon)
- hosting a **custom repo** + **zip** on the Felix dashboard (`/downloads/dalamud/repo.json`)

Build a release zip with:

`.\felix-plugin\scripts\Package-FelixPlugin.ps1`
