# Feasibility — Kingdom Two Crowns 2.4.0 modding

> **Status: runtime-verified.**  MelonLoader runs the live game, Cpp2IL
> (patched) emits all 100+ dummy DLLs, Il2CppInterop turns them into ~130 usable
> reference assemblies, and `dotnet build KingdomMod.sln` compiles the SDK,
> loader, and all eleven example mods against that surface with 0 errors / 0
> warnings.  MelonLoader loads the loader + every example mod in-game (the F1
> console and per-mod hotkeys are live).

## What we confirmed at build 2.4.0 (revision 23488, 2026-04-28)

| Check | Result |
|---|---|
| Engine | Unity **6000.0.61f1** (Unity 6) |
| Scripting backend | **IL2CPP** |
| `global-metadata.dat` magic / version | `0xFAB11BAF` / **version 31** |
| Metadata encryption | **None** (plaintext) |
| Class surface dumpable | ✅ Il2CppDumper produces 42 MB `dump.cs`, 61 MB `il2cpp.h` |
| Loader bootstraps cleanly | ✅ MelonLoader 0.7.3 (Open-Beta) detects game and runtime |
| Cpp2IL dummy DLLs generated | ✅ 101 assemblies including the previously-crashing `Haglet-Assembly-CSharp-02.dll` (via patched Cpp2IL) |
| Il2CppInterop assemblies generated | ✅ **~130 interop DLLs** in `MelonLoader/Il2CppAssemblies/` |
| SDK + loader + examples build | ✅ 0 errors, 0 warnings against live interop |
| Mods load in-game | ✅ Loader + 11 example mods load under MelonLoader 0.7.3; F1 console + hotkeys live |

## The Cpp2IL hurdle (and how we got past it)

MelonLoader 0.7.3 bundles **Cpp2IL 2022.1.0-pre-release.21**, which crashes on KTC
build 2.4.0 with:

```
Failed to process type AndroidManager+<_InitiateSignIn>d__21_Server
  in Haglet-Assembly-CSharp-02
System.NullReferenceException at
  AsmResolverAssemblyPopulator.CopyPropertiesInType:line 444
```

Root cause: a property whose getter and setter are both stripped from the IL2CPP
metadata, so `Il2CppPropertyDefinition.RawPropertyType` dereferences a null
`Setter!.Parameters![0]`.  This is **Cpp2IL issue
[#471](https://github.com/SamboyCoding/Cpp2IL/issues/471)**; PR
[#548](https://github.com/SamboyCoding/Cpp2IL/pull/548) proposed a fix but it
hasn't shipped in any pre-release tag yet.

**Our solution:** ship a *patched Cpp2IL* with KingdomMod's installer.  Three small
surgical changes (all to upstream third-party code, none touching game content):

1. `LibCpp2IL/Metadata/Il2CppPropertyDefinition.cs`
   — `PropertyType`, `RawPropertyType` and `IsStatic` now return `null` / `false`
   when both accessors are stripped, instead of NREing on `Setter!`.
2. `Cpp2IL.Core/Utils/AsmResolver/AsmResolverAssemblyPopulator.cs`
   — `CopyPropertiesInType` skips properties with no accessors and skips when
   property-type resolution returns null.
3. `Cpp2IL.Core/Utils/AsmResolver/AsmResolverAssemblyPopulator.cs`
   — `PopulateCustomAttributes` skips properties we couldn't materialise above
   instead of NREing on a missing `AsmResolverProperty` extra-data tag.

`tools/install.ps1` installs the patched Cpp2IL build from `build/_tools` and
drops the resulting `Cpp2IL.exe` into
`MelonLoader/Dependencies/Il2CppAssemblyGenerator/Cpp2IL/` — replacing the
bundled version.  The original is preserved as `Cpp2IL.original.exe` so
MelonLoader can be reverted with a one-line restore.

**Performance:** patched Cpp2IL completes in ~60 seconds on the full 70 MB
GameAssembly.  Il2CppInterop's downstream "Finalizing method declarations" is the
slow part (5-15 min on first run); subsequent launches are sub-second cache hits.

## Discovered root namespace + key classes

In the game source, classes live in the global namespace (no `noio.*` prefix).
After Il2CppInterop processes them, those globals all end up under a namespace
literally called **`Il2Cpp`** — so what the source calls `Managers` is referenced
from mods as `Il2Cpp.Managers`.  The SDK hides this for the common cases; only
reach for `Il2Cpp.*` directly when wrapping a type the facade doesn't cover.

The structural backbone of the SDK is:

| Class | Role |
|---|---|
| `Managers` (`SingletonMonoBehaviour<Managers>.Inst`) | Root singleton. Exposes every subsystem as public fields. |
| `Game : Manager` | Run lifecycle. Public static `Action`s: `OnGameStart`, `OnGameEnd`, `OnLose`. Instance: `OnSailAway`. |
| `Director : Manager` | Day/night/season clock. Instance `Action`s: `OnDayFlip`, `OnDayPhaseChange(DayPhase)`, `OnSeasonChange(Season)`, `OnWinterEnd`. |
| `Kingdom : Manager` | Macro state of the realm. |
| `CurrencyManager : Manager` | Maps `CurrencyType` ↔ config / prefabs. `CurrencyMap<T>` is the universal `Coins / Gems / Crown / Skulls` bundle. |
| `Wallet` | Per-player currency. **Static cheats** `Wallet.InfiniteMoney`, `Wallet.DebugDisableTaxes`. |
| `Player` | The monarch. Static cheat `Player.DebugInfiniteStamina`. |
| `Payable` (abstract) | Anything the monarch can pay for (walls, towers, decrees, …). |
| `EnemyManager`, `PayableManager`, `Stats`, `World`, `PrefabManager` | The other Managers fields. |

Mods build on these via [`KingdomMod.Api`](../src/KingdomMod.Api/) (see
[`api-reference.md`](api-reference.md)).
