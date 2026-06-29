# Getting Started With KingdomMod

## Player Install

Prerequisites:

- A legitimate copy of **Kingdom Two Crowns 2.4.0+** installed on Windows.
- Windows 10/11 x64.
- The latest `KingdomMod-<version>-x64.msi` from the GitHub Releases page.

Install:

1. Run the MSI.
2. Confirm or browse to your Kingdom Two Crowns folder.
3. Finish the installer.
4. Launch Kingdom Two Crowns normally.
5. Press **F1** to open the KingdomMod console.

The installer copies KingdomMod into `<KTC>\Mods`. If MelonLoader is not already
installed, the MSI installs the bundled MelonLoader runtime into the game
folder. If MelonLoader is already present, the installer leaves it alone.

## Uninstall

Use Windows **Settings -> Apps -> Installed apps -> KingdomMod -> Uninstall**,
or run the same MSI and choose remove.

Uninstall removes KingdomMod-owned files. If the MSI installed MelonLoader and
there are no unrelated mod DLLs in `<KTC>\Mods`, it removes that owned
MelonLoader copy too. If MelonLoader was already installed, or if other mods are
present, MelonLoader stays in place.

## In-Game Hotkeys

| Key | Mod | Action |
|---|---|---|
| **F1** | Loader console | Open or close the KingdomMod console |
| **F2** | HudOverlay | Toggle the day / phase / season / next-events overlay |
| **F3** | ChallengeDumper | Dump game data to `UserData/KingdomMod/dump/*.json` |
| **F4** | AnyMount | Open the per-player mount selector |
| **F5 / F6 / F7** | SpeedHotkeys | Slow / reset / speed up `ClockSpeedModifier` |
| **F8** | SpeedHotkeys | Toggle freeze, remembering the last non-zero speed |

The F1 console also shows a Shortcuts section generated from the mods currently
loaded. If a key is missing there, the corresponding mod DLL did not load.

## Was It Loaded?

Open `<KTC>\MelonLoader\Latest.log` and look for:

```text
KingdomMod platform initialised.
Loader version
F1 toggles the in-game console
```

You should also see one initialized line per bundled example mod. If a mod is
missing, check the log immediately above the missing mod name for the binding or
load error.

## Installing A Data Pack

Data packs live beside their mod DLLs and contain only JSON plus user-supplied
art/audio. Copy the contents of `packs/ExampleBalancePack/` to:

```text
<KTC>/Mods/BalanceTweaks/pack/
```

The loader discovers `Mods/*/pack`, reads `kingdommod.pack.json`, and exposes
`balance.json`, `sprites/`, and `audio/` through `Kingdom.Packs`.

For sprite packs and reskins, read
[Sprite construction and replacement](api-reference.md#sprite-construction-and-replacement).
The short rule: name your replacement PNG after the in-game sprite name, keep
the original dimensions and transparent padding when possible, and ship only
your own artwork.

## Developer Setup

Developer setup is only needed if you want to build KingdomMod or write mods.
Players should use the MSI.

Prerequisites:

- A legitimate copy of **Kingdom Two Crowns 2.4.0+**.
- Windows 10/11 x64.
- .NET 8 SDK or newer.
- About 2 GB free disk for one-time interop generation.

Clone and generate local references:

```powershell
git clone https://github.com/FredApps/KingdomMod.git
cd KingdomMod
powershell -ExecutionPolicy Bypass -File tools\install.ps1
```

That script:

1. Locates your Kingdom Two Crowns install.
2. Backs up saves to `build/save-backups/<timestamp>/`.
3. Downloads MelonLoader.
4. Builds and installs KingdomMod's patched Cpp2IL.
5. Runs the game once so MelonLoader generates local interop assemblies.
6. Copies those assemblies into `refs/` so mods can compile.

Build everything:

```powershell
dotnet build KingdomMod.sln -c Release
```

Deploy your current local build into the game while developing:

```powershell
powershell -ExecutionPolicy Bypass -File tools\install-mods.ps1
```

Useful flags:

- `-GameDir <path>`: skip auto-detection and use this game folder.
- `-SkipExamples`: install only the loader and API.
- `-NoBuild`: fail instead of building when DLLs are missing.
- `-NoDefenderExclusion`: skip the Defender exclusion prompt.

## Writing Your First Mod

Create `examples/HelloDay/HelloDay.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>KingdomMod.Examples.HelloDay</AssemblyName>
    <RootNamespace>KingdomMod.Examples.HelloDay</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\KingdomMod.Api\KingdomMod.Api.csproj" />
  </ItemGroup>
</Project>
```

Create `examples/HelloDay/HelloDayMod.cs`:

```csharp
using KingdomMod;
using MelonLoader;

[assembly: MelonInfo(typeof(KingdomMod.Examples.HelloDay.HelloDayMod),
    "Hello Day", "0.1.0", "Your name")]
[assembly: MelonGame("noio", "KingdomTwoCrowns")]

namespace KingdomMod.Examples.HelloDay
{
    public sealed class HelloDayMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            Kingdom.Time.OnDayChanged += OnDayChanged;
            LoggerInstance.Msg("Hello Day loaded.");
        }

        private void OnDayChanged()
        {
            LoggerInstance.Msg($"Good morning, day {Kingdom.Time.DaysInReign}.");
        }
    }
}
```

Build it:

```powershell
dotnet build examples\HelloDay\HelloDay.csproj -c Release
```

Copy the resulting DLL into `<KTC>\Mods\`, launch the game, and check
`<KTC>\MelonLoader\Latest.log` for `Hello Day loaded.`

After that, use [api-reference.md](api-reference.md) for lifecycle hooks, F1
console controls, Harmony patches, pack APIs, and SDK member tables.

## Troubleshooting

- **RemoteAPI 502 / 526 in the log:** harmless after interop is generated.
  `tools/install-mods.ps1` sets MelonLoader offline generation once the cache
  exists so future launches skip the flaky lookup.
- **`UnityDependencies_<ver>.zip does not Exist!`:** the first interop
  generation must run online. Run `tools/install.ps1` once.
- **`.NET 8 runtime missing`:** install the .NET 8 SDK/runtime, then rerun
  developer setup.
- **A mod's F-key does nothing:** open F1 and check Shortcuts. If the mod is not
  listed, its DLL did not load; check `MelonLoader/Latest.log`.

## After A Game Update

```powershell
powershell -ExecutionPolicy Bypass -File tools\update-after-patch.ps1
```

This clears the interop cache, regenerates references against the new game
build, and re-dumps the class surface. Rebuild your mods afterwards.

## Release Maintainer Notes

Refresh the committable release payload locally:

```powershell
powershell -ExecutionPolicy Bypass -File tools\prepare-release.ps1
```

Build an MSI locally:

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-msi.ps1 -Version 0.1.0
```

GitHub Actions packages the committed `dist/KingdomMod*.dll` files into an MSI
on `v*` tags and manual workflow runs. It intentionally does not generate
`refs/` or compile against game-derived files.

## Safety Notes

- Mods can desync co-op and may interfere with PlayFab cloud saves.
- Treat modded sessions as single-player/offline unless every player uses the
  same mod set.
- Do not redistribute game files, generated interop assemblies, dumps, or
  extracted assets.
