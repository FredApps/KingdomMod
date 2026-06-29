# KingdomMod

A community modding platform for **Kingdom Two Crowns** on Windows.

KingdomMod provides a MelonLoader-based runtime, a small C# SDK, an in-game F1
console, and example mods for balance tweaks, UI overlays, hotkeys, mounts,
tree behavior, and sprite replacement.

[![Join the KingdomMod Discord](https://img.shields.io/badge/Discord-Join%20the%20modding%20community-5865F2?logo=discord&logoColor=white)](https://discord.gg/VpuCg6Hcrs)

Join the [KingdomMod Discord](https://discord.gg/VpuCg6Hcrs) to share mods,
ask questions, and help grow the Kingdom Two Crowns modding community.

> You must own Kingdom Two Crowns. KingdomMod ships no game code and no game
> assets. Developer reference assemblies are generated locally from your own
> game install and are never redistributed.

## Install

1. Download [`KingdomMod-<version>-x64.msi`](https://github.com/FredApps/KingdomMod/releases) from the latest GitHub Release.
2. Run the MSI.
3. Confirm or browse to your Kingdom Two Crowns folder.
4. Enable the required Windows Defender exclusion checkbox when prompted.
5. Let the installer run its setup pass if interop references must be
   generated. This first pass can take several minutes.
6. Launch the game normally.
7. Press **F1** in-game to open the KingdomMod console.

If Windows SmartScreen blocks the installer, choose **More info** and then
**Run anyway**. This warning appears because KingdomMod's MSI is a new,
community-built installer that is not code-signed with an established publisher
certificate yet; SmartScreen reputation is based on signing and download
history, not just the contents of the installer.

The MSI is intentionally small. It installs MelonLoader if needed, downloads a
pinned setup-time .NET SDK only when no usable SDK is present, downloads pinned
Cpp2IL source, applies KingdomMod's patch locally, runs a setup pass if
references are missing, builds KingdomMod DLLs locally, and copies the loader,
API, and bundled example mods into the game's `Mods` folder. If MelonLoader is
already present, KingdomMod leaves that installation owned by you.

The Defender exclusion is requested because KingdomMod builds modified mod DLLs
locally against your own Kingdom Two Crowns install. Those DLLs cannot be
signed ahead of time, and Windows Defender can quarantine unsigned generated
DLLs before MelonLoader can run them. If Windows policy, third-party antivirus,
or an already-managed Defender setup blocks the automatic exclusion change, the
installer continues and shows manual instructions.

For unattended installs, pass the same consent explicitly:

```powershell
msiexec /i KingdomMod-<version>-x64.msi INSTALLFOLDER="<KTC>" DEFENDEREXCLUSIONACCEPTED=1
```

## Uninstall

Use Windows **Settings -> Apps -> Installed apps -> KingdomMod -> Uninstall**,
or run the MSI again and choose remove.

Uninstall removes KingdomMod-owned files. If the MSI installed MelonLoader and
no unrelated content is present under `Mods`, `Plugins`, or `UserLibs`, it also
removes that owned MelonLoader copy. If MelonLoader existed before KingdomMod,
or other non-KingdomMod content is present there, MelonLoader is left in place.
The MSI also removes its support/cache folders and any Defender exclusion that
KingdomMod added itself. User data, packs, preferences, logs, dumps, and save
backups are preserved.

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
