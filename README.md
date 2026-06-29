# KingdomMod

A community modding platform for **Kingdom Two Crowns** on Windows.

KingdomMod provides a MelonLoader-based runtime, a small C# SDK, an in-game F1
console, and example mods for balance tweaks, UI overlays, hotkeys, mounts,
tree behavior, and sprite replacement.

> You must own Kingdom Two Crowns. KingdomMod ships no game code and no game
> assets. Developer reference assemblies are generated locally from your own
> game install and are never redistributed.

## Install

1. Download `KingdomMod-<version>-x64.msi` from the latest GitHub Release.
2. Run the MSI.
3. Confirm or browse to your Kingdom Two Crowns folder.
4. Let the installer launch the game once if interop references must be
   generated. This first pass can take several minutes.
5. Launch the game normally.
6. Press **F1** in-game to open the KingdomMod console.

The MSI installs MelonLoader if needed, downloads and builds KingdomMod's
patched Cpp2IL from source on your machine, generates local references from
your own game install, extracts a bundled .NET SDK for setup-time compilation,
builds KingdomMod DLLs locally, and copies the loader, API, and bundled example
mods into the game's `Mods` folder. If MelonLoader is already present,
KingdomMod leaves that installation owned by you.

## Uninstall

Use Windows **Settings -> Apps -> Installed apps -> KingdomMod -> Uninstall**,
or run the MSI again and choose remove.

Uninstall removes KingdomMod-owned files. If the MSI installed MelonLoader and
no unrelated mod DLLs are present, it also removes that owned MelonLoader copy.
If MelonLoader existed before KingdomMod, or other non-KingdomMod mods are
present, MelonLoader is left in place.

## What You Can Make

| Tier | Mod type | Example | Difficulty |
|---|---|---|---|
| 1 | Balance / economy | cheaper towers, more starting coins, longer days | Easy |
| 2 | Behaviour / rules | tweak Greed waves, spawn logic, season effects | Moderate |
| 3 | UI / HUD | on-screen stats, debug overlays | Moderate |
| 4 | Reskins and audio | swap sprites, banners, music, SFX | Moderate |
| Tools | Utilities | dev console, cheats, sandbox, save inspector | Easy |
| 5 | New content | new units, upgrades, decrees | Hard |

See [docs/capabilities.md](docs/capabilities.md) for the full capability
breakdown and limits.

## Bundled Example Mods

| Mod | Tier | What it does | Hotkey |
|---|---|---|---|
| [BalanceTweaks](examples/BalanceTweaks) | 1 | Hour-per-second override, starting coin top-up, JSON balance packs | - |
| [BalanceExtras](examples/BalanceExtras) | 1-2 | Income multiplier, starting loadout, sail time, cave timer, lock season, no red moon | - |
| [GameplayTweaks](examples/GameplayTweaks) | 2 | Clamp the day-length floor via Harmony | - |
| [SpeedTweaks](examples/SpeedTweaks) | 2 | Slider for `ClockSpeedModifier` and day curve toggle | - |
| [HudOverlay](examples/HudOverlay) | 3 | Day, phase, season, clock, and next Director events overlay | F2 |
| [SpeedHotkeys](examples/SpeedHotkeys) | 3 | Speed down, reset, speed up, and freeze | F5 / F6 / F7 / F8 |
| [AnyMount](examples/AnyMount) | 5 | Per-player mount selector using the game's own mount swap path | F4 |
| [AnyTrees](examples/AnyTrees) | 5 | Builder cowardice and Guerilla Warfare F1 controls | F1 |
| [ChallengeDumper](examples/ChallengeDumper) | Tools | Dump challenges, steeds, level configs, and biomes to JSON | F3 |
| [SandboxConsole](examples/SandboxConsole) | Tools | Dev console, game-state events, and sandbox hooks | F1 |
| [ReskinPack](examples/ReskinPack) | 4 | Replace `Sprite` / `Texture2D` values from a no-code pack folder | - |

## The F1 Console

The console opens automatically on launch as a full-width bar pinned to the
bottom of the screen. Press **F1** to hide or show it.

It includes:

- Live status: readiness, island, day, season, coins, gems, and day/night.
- Cheats: persisted radio rows for drops, coins, stamina, and gift controls.
- Fixes: loader-owned safety fixes such as crown pickup and boar vanish repair.
- Mod options: controls registered by loaded mods through `Kingdom.Mods`.
- Shortcuts: F-key help generated only from the mods currently loaded.
- Tooltips and log output for quick in-game feedback.

Everything defaults to vanilla behavior unless a mod or cheat is explicitly
enabled. A first-run popup warns about multiplayer desync and cloud-save risk.

## Developer Setup

Normal install and developer setup share the same core model: KingdomMod DLLs
compile on the machine that owns the game, after local references are generated
from that game install. The MSI automates that for players and brings its own
setup-time SDK. Clone the repo only if you want to change KingdomMod, write
mods, or prepare releases:

```powershell
git clone https://github.com/FredApps/KingdomMod.git
cd KingdomMod
powershell -ExecutionPolicy Bypass -File tools\install.ps1
dotnet build KingdomMod.sln -c Release
```

`tools\install.ps1` installs MelonLoader for the local game, backs up saves,
downloads and builds KingdomMod's patched Cpp2IL from source, generates local
`refs/`, and optionally dumps the class surface used by the docs. `refs/` and
generated dumps are ignored and must not be committed.

To smoke-test a local build into `dist/` after changes:

```powershell
powershell -ExecutionPolicy Bypass -File tools\prepare-release.ps1
```

To build the MSI locally:

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-msi.ps1 -Version 0.1.0
```

The GitHub workflow packages source plus installer support into an MSI on `v*`
tags or manual runs. The MSI includes source, MelonLoader, and a pinned .NET
SDK; patched Cpp2IL and KingdomMod DLLs are built on the target machine after
generating local refs from that user's game install.

## Writing Mods

Start with [docs/getting-started.md](docs/getting-started.md), then use
[docs/api-reference.md](docs/api-reference.md) for lifecycle hooks, F1 controls,
Harmony helpers, pack APIs, and member tables.

For art mods, see
[Sprite construction and replacement](docs/api-reference.md#sprite-construction-and-replacement).
KingdomMod can load user-supplied PNGs and construct Unity sprites at runtime,
but you must ship only your own art.

## Safety

- Mods can desync co-op. Treat modded sessions as single-player/offline unless
  every player runs the same mod set.
- Mods may interfere with cloud saves. Backups are recommended.
- Do not redistribute game files, generated interop assemblies, dumps, or
  extracted assets.

## License

KingdomMod source is MIT licensed. Kingdom Two Crowns is property of noio /
Raw Fury and is not covered by that license.
