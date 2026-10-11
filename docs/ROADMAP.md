# Pancake Client roadmap

## 0.1 — desktop foundation and initial OBS companion

Desktop inventory, reversible mod switches, profiles, Thunderstore browsing/downloads, content ZIP import, save backups, logs, OBS controls, and experimental companion camera source.

Release gates: Windows build and integration checks; local BONELAB companion compilation and headset testing. The latter requires the owner's installed game and is not yet completed.

## 0.2 — reliable package management

- Package ownership manifests and dependency-aware code-mod installation.
- Updates with backups and rollback, preserving per-mod configuration.
- Integrated mod.io login, browsing, subscriptions, and PCVR downloads.
- Version-pinned export/import packs with source IDs and compatibility information.
- Prevent disabling required dependencies without an explicit plan.

## 0.3 — game library tools

- Live avatar and map inventory from the game warehouse.
- Avatar favorites and BodyLog slot editing.
- Level loading through supported game APIs.
- Avatar preview generation and caching.
- Explicit save/progression tools with tested backups.

## 0.4 — filming tools

- Physical handheld camera and preview screen.
- High-quality OBS capture output.
- Camera/scene presets, smooth follow settings, and collision avoidance.
- Optional Fusion camera synchronization, using Fusion's module system.

## Future polish

Signed installer, opt-in automatic client updates, accessibility pass, better diagnostics, and optional share links for mod-pack manifests.
