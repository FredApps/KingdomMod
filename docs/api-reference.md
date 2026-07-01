# KingdomMod.Api - new modder guide and reference

This page has two parts:

1. A beginner path for writing a small mod from scratch.
2. A reference for the `KingdomMod.Api` members you will use most often.

All mods get to the SDK through the static `Kingdom` facade. It is a lightweight
shim over the game's own singletons. See [`feasibility.md`](feasibility.md) for
the underlying class surface and how we discovered it.

> **Naming convention note.** Il2CppInterop places every game type that lived in
> the global namespace into a namespace literally called `Il2Cpp`. What the game
> source calls `Managers` is `Il2Cpp.Managers` in mod code. The SDK hides this
> for common cases; only reach for `Il2Cpp.*` directly when the facade does not
> wrap the type you need.

> **Event subscription tip.** Il2Cpp event accessors do not always accept raw C#
> lambdas. Prefer the SDK wrappers such as `Kingdom.Game.OnGameStart += ...` over
> touching raw `Il2Cpp` events directly. The facade does the delegate bridging.

## Mental model

A KingdomMod mod is a normal C# class library DLL loaded by MelonLoader when the
game starts. Your mod usually does four things:

1. Declares MelonLoader metadata with `[assembly: MelonInfo(...)]` and
   `[assembly: MelonGame("noio", "KingdomTwoCrowns")]`.
2. Implements a `MelonMod` class.
3. Uses `OnInitializeMelon`, `OnUpdate`, `OnGUI`, and SDK events to run code.
4. Reads or writes game state through `Kingdom.*`, or uses Harmony patches for
   game methods the SDK does not wrap yet.

Use the SDK first. Reach for direct `Il2Cpp.*` types and Harmony patches only
when the facade does not expose what you need.

For mount-specific work, see [`mount-modding.md`](mount-modding.md). It covers
`Steed` fields, prefab discovery, active mount changes, and the safe
`Player.Ride` swap path.

## Smallest complete mod

Create a folder such as `examples/HelloDay/`, then add these two files.

`HelloDay.csproj`:

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

`HelloDayMod.cs`:

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

Copy `examples/HelloDay/bin/Release/KingdomMod.Examples.HelloDay.dll` into
`<KTC>\Mods\`, launch the game, then check `<KTC>\MelonLoader\Latest.log`.

## Where code should run

| Hook | Use it for | Beginner rule |
|---|---|---|
| `OnInitializeMelon()` | Preferences, event subscriptions, Harmony patches, F1 console controls | Register things here; do not assume a save is loaded yet. |
| `OnUpdate()` | Keybinds, polling, small per-frame checks | Guard with `Kingdom.IsReady` before touching live game objects. |
| `OnGUI()` | IMGUI overlays/debug panels | Snapshot data defensively; scene transitions can happen mid-draw. |
| SDK events | Day changes, season changes, run start/end | Prefer these over polling when an event exists. |
| Harmony patches | Changing game methods the SDK does not wrap | Patch by type/name, keep patches small, and default to vanilla behavior when your feature is off. |

`Kingdom.IsReady` means the core game managers exist. It is false at the main
menu and during some load/transition moments. Many SDK reads return safe
defaults while not ready, but writes and direct `Il2Cpp.*` access should still
be guarded.

```csharp
public override void OnUpdate()
{
    if (!Kingdom.IsReady) return;

    if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F9))
        Kingdom.Economy.GiveCoins(5);
}
```

## Add a setting to the F1 console

The F1 console can display mod-owned controls. The registry does not store your
state; it calls your getters and setters each frame.

```csharp
using KingdomMod;
using MelonLoader;

public sealed class RichModeMod : MelonMod
{
    private static MelonPreferences_Entry<bool> _enabled;

    public static bool Enabled
    {
        get => _enabled?.Value ?? false;
        set
        {
            if (_enabled == null) return;
            _enabled.Value = value;
            MelonPreferences.Save();
        }
    }

    public override void OnInitializeMelon()
    {
        var cat = MelonPreferences.CreateCategory("KingdomMod.RichMode", "Rich Mode");
        _enabled = cat.CreateEntry("Enabled", false, "Give coins at dawn.");

        Kingdom.Mods.RegisterToggle("Rich Mode", () => Enabled, v => Enabled = v);
        Kingdom.Mods.RegisterHotkey("F9", "Give 5 coins");
    }

    public override void OnUpdate()
    {
        if (!Enabled || !Kingdom.IsReady) return;

        if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F9))
            Kingdom.Economy.GiveCoins(5);
    }
}
```

`RegisterHotkey` only adds a row to the console's Shortcuts guide. Your mod
still handles the input in `OnUpdate`.

## Patch a game method

Use Harmony when you need to alter behavior that the SDK does not expose. Patch
by type and method name, not offsets.

```csharp
using HarmonyLib;
using KingdomMod;
using MelonLoader;

public sealed class LongerDaysMod : MelonMod
{
    public override void OnInitializeMelon()
    {
        HarmonyHelper.PatchAll(this);
    }
}

[HarmonyPatch(typeof(Il2Cpp.Director), nameof(Il2Cpp.Director.OnLevelLoaded))]
internal static class LongerDaysPatch
{
    private static void Postfix(Il2Cpp.Director __instance)
    {
        if (__instance == null) return;
        __instance.secondsPerInGameHour *= 1.25f;
    }
}
```

Patch guidelines:

- Keep the patch narrow and easy to disable.
- Let vanilla run unless you intentionally need a prefix that returns `false`.
- Log a first-hit message while developing, then remove noisy logs.
- For private IL2CPP fields, try generated properties first:
  `__instance._currentLevelConfig`, not `AccessTools.Field(...)`.

## Find the game type or method you need

Start with examples:

| Goal | Good examples |
|---|---|
| Balance and time | [`BalanceTweaks`](../examples/BalanceTweaks), [`BalanceExtras`](../examples/BalanceExtras), [`SpeedTweaks`](../examples/SpeedTweaks) |
| Input and hotkeys | [`SpeedHotkeys`](../examples/SpeedHotkeys) |
| HUD/UI | [`HudOverlay`](../examples/HudOverlay) |
| Harmony patches | [`AnyTrees`](../examples/AnyTrees), [`GameplayTweaks`](../examples/GameplayTweaks) |
| Data and asset packs | [`ReskinPack`](../examples/ReskinPack) |

When examples are not enough, search the generated dump:

```powershell
rg "class Director|OnLevelLoaded|secondsPerInGameHour" docs\_generated\dump\dump.cs
```

The dump is generated from your own game install. It is not redistributed.

## Common beginner mistakes

| Symptom | Likely cause | Fix |
|---|---|---|
| The DLL is in `Mods`, but nothing happens | Missing or wrong `[MelonInfo]` / `[MelonGame]`, or a load error | Open `<KTC>\MelonLoader\Latest.log` and search for your mod name. |
| Code works in-game but throws at the menu | The mod touches game singletons before they exist | Guard with `Kingdom.IsReady`. |
| Event callback never fires | You subscribed to raw `Il2Cpp` events instead of SDK wrappers | Use `Kingdom.Game.*` and `Kingdom.Time.*` events. |
| Harmony patch never runs | Wrong type, method name, overload, or scene never calls it | Search `dump.cs`, compare examples, add a temporary first-hit log. |
| F1 toggle shows but does not persist | The toggle setter only changed a field | Store state in `MelonPreferences` and call `MelonPreferences.Save()`. |
| Mod changes affect co-op strangely | Different clients are running different logic | Treat gameplay mods as single-player/offline unless all players match exactly. |

## API overview

```csharp
using KingdomMod;

Kingdom.Game        // GameState facade (lifecycle, land, version)
Kingdom.Time        // TimeApi facade (clock, seasons, day events)
Kingdom.Economy     // EconomyApi facade (wallet, taxes, cheats)
Kingdom.Players     // PlayersApi facade (monarchs)
Kingdom.Enemies     // EnemyApi facade (Greed/blood-moon queries)
Kingdom.Packs       // PackApi facade (no-code texture/audio/JSON loading)
Kingdom.Mods        // ModsApi facade (F1 console controls and hotkey guide)
Kingdom.CustomMounts // CustomMountsApi facade (F1 Custom Mounts registry)
Kingdom.IsReady     // true when the Managers singleton exists
```

## Kingdom.Game - `GameState`

| Member | Type | Notes |
|---|---|---|
| `OnGameStart` | `event Action` | Run/playthrough begins. |
| `OnGameEnd` | `event Action` | Win or loss. |
| `OnLose` | `event Action` | Monarch dies, crown lost. |
| `OnSailAway` | `event Action` | Player triggered sail-away. |
| `CurrentLand` | `int` | Land index, usually 1-5 in vanilla. |
| `InPlayableState` | `bool` | Not menu/loading/credits. |
| `HasLost` | `bool` | Run has been lost. |
| `Version` | `string` | Game version, for example `"2.4.0"`. |
| `IsCoopActive` | `bool` | Local P2 player is joined. |

## Kingdom.Time - `TimeApi`

| Member | Type | Notes |
|---|---|---|
| `OnDayChanged` | `event Action` | Midnight roll-over (`Director.OnDayFlip`). |
| `OnDayPhaseChanged` | `event Action<DayPhase>` | Phase transition such as Dawn, Day, Dusk, Night. |
| `OnSeasonChanged` | `event Action<Season>` | Spring, Summer, Autumn, Winter. |
| `OnWinterEnd` | `event Action` | Winter just ended. |
| `DaysInReign` | `int` | Total days the current monarch has reigned. |
| `IslandDays` | `int` | Days on the current island. |
| `IsNight` / `IsDay` | `bool` | Current day/night state. |
| `IsTimePaused` | `bool` | True during cinematics, pause menus, and similar states. |
| `CurrentSeason` | `Season` | Current season enum. |
| `ClockSpeedMultiplier` | `float` (r/w) | 1 = normal. 0 = freeze. Useful for fast-forward mods. |
| `SecondsPerInGameHour` | `float` (r/w) | Tweak day length directly. |

## Kingdom.Economy - `EconomyApi`

| Member | Type | Notes |
|---|---|---|
| `InfiniteMoney` | `bool` (r/w) | Maps to `Wallet.InfiniteMoney`; the loader enforces it for player wallet currencies such as coins, gems, shades/souls, skulls, merchandise, candles, and eggs. |
| `DisableTaxes` | `bool` (r/w) | Maps to `Wallet.DebugDisableTaxes`; hides the bag and shows coin plus nonzero item counters in the HUD. |
| `Coins` / `Gems` | `int` (r/w) | Local wallet only. |
| `Wallets` | `IEnumerable<Wallet>` | All wallets in the scene. |
| `LocalWallet` | `Wallet` | First/local wallet, or null. |
| `GiveCoins(int)` | method | Adds coins, clamped to 0. |
| `GiveGems(int)` | method | Adds gems, clamped to 0. |

## Kingdom.Players - `PlayersApi`

| Member | Type | Notes |
|---|---|---|
| `All` | `IEnumerable<Player>` | 1 in single-player, 2 in local co-op. |
| `Local` | `Player` | First player, or null. |
| `InfiniteStamina` | `bool` (r/w) | Maps to `Player.DebugInfiniteStamina`. |

## Kingdom.Enemies - `EnemyApi`

| Member | Type | Notes |
|---|---|---|
| `IsBloodMoonTonight` | `bool` | Surface flag. |
| `Raw` | `EnemyManager` | Escape hatch: full game type for power users. |

## Kingdom.Packs - `PackApi`

| Member | Notes |
|---|---|
| `LoadTexture(absolutePath)` | PNG/JPG to `Texture2D` (point-filter, cached). |
| `MakeSprite(tex, pixelsPerUnit=16, pivot=center)` | Build a `Sprite` from a `Texture2D`. |
| `LoadWav(absolutePath)` | 16-bit PCM WAV to `AudioClip` (cached). |
| `DiscoverPacks(rootDirectory)` | Finds direct packs and `*/pack` folders below a root such as `<KTC>/Mods`. |
| `LoadJson<T>(absolutePath)` | Deserializes pack JSON with comments/trailing commas allowed; returns `default` on failure. |
| `TryLoadJson<T>(absolutePath, out value)` | Non-throwing JSON load with success/failure result. |
| `ClearCache()` | Drop cached resources. |

### Pack folder format

```text
Mods/
  MyBalanceMod.dll
  MyBalanceMod/
    pack/
      kingdommod.pack.json
      balance.json
      sprites/
        banner_blue.png
      audio/
        bell.wav
```

`kingdommod.pack.json` is optional but recommended:

```json
{
  "id": "example.my-pack",
  "name": "My Pack",
  "version": "1.0.0",
  "author": "Your name",
  "description": "Short human-readable description."
}
```

`PackInfo` exposes resolved paths such as `BalancePath`, `SpritesDirectory`, and
`AudioDirectory`, plus `HasBalance`, `HasSprites`, and `HasAudio`.

## Sprite construction and replacement

Sprite replacement is a two-step process:

1. Load a user-supplied PNG/JPG as a `Texture2D`.
2. Construct a Unity `Sprite` from that texture and assign it to a
   `SpriteRenderer`.

KingdomMod does not ship or redistribute game art. Reskin packs must contain
original user-supplied art.

### Pack layout for sprites

Put replacement images under a pack's `sprites/` folder:

```text
Mods/
  ReskinPack.dll
  ReskinPack/
    pack/
      kingdommod.pack.json
      sprites/
        banner_blue.png
        crown_gold.png
```

The example reskin mod uses the PNG filename without extension as the lookup
key. If a renderer's current sprite is named `banner_blue`, then
`sprites/banner_blue.png` replaces it.

### Construct a sprite

```csharp
var texturePath = Path.Combine(pack.SpritesDirectory, "banner_blue.png");
var texture = Kingdom.Packs.LoadTexture(texturePath);
var sprite = Kingdom.Packs.MakeSprite(texture);
```

`LoadTexture` returns a cached `Texture2D` with:

- `TextureFormat.RGBA32`
- no mip chain
- `FilterMode.Point`, which preserves Kingdom Two Crowns' pixel-art look
- `TextureWrapMode.Clamp`
- readable texture data, so Unity can build sprites from it

`MakeSprite` creates a full-rectangle sprite:

```csharp
var sprite = Kingdom.Packs.MakeSprite(
    texture,
    pixelsPerUnit: 16f,
    pivot: new Vector2(0.5f, 0.5f));
```

Important sprite construction choices:

| Setting | What it affects | Default |
|---|---|---|
| `pixelsPerUnit` | World scale. Use the same PPU as the sprite you are replacing when possible. | `16f` |
| `pivot` | Anchor point inside the image. `(0.5, 0.5)` is center. `(0.5, 0)` is bottom-center. | center |
| image width/height | Sprite bounds. Match the original dimensions when you want a drop-in replacement. | source image size |
| transparent pixels | Shape and empty padding. Keep similar padding to the original to avoid visual offsets. | from PNG alpha |

If a replacement looks too large, too small, or offset in-game, check
`pixelsPerUnit`, transparent padding, and pivot first.

### Replace sprites in a scene

The simple replacement path is to scan `SpriteRenderer` components after a scene
loads, then swap renderers whose current sprite name matches one of your pack
images:

```csharp
private readonly Dictionary<string, Sprite> _replacements = new();

public override void OnInitializeMelon()
{
    foreach (var pack in Kingdom.Packs.DiscoverPacks(MelonEnvironment.ModsDirectory))
    {
        if (!pack.HasSprites) continue;

        foreach (var path in Directory.EnumerateFiles(pack.SpritesDirectory, "*.png"))
        {
            var key = Path.GetFileNameWithoutExtension(path);
            var tex = Kingdom.Packs.LoadTexture(path);
            var sprite = Kingdom.Packs.MakeSprite(tex);
            if (sprite != null)
                _replacements[key] = sprite;
        }
    }
}

public override void OnSceneWasInitialized(int buildIndex, string sceneName)
{
    MelonCoroutines.Start(ApplySpritesAfterFrame());
}

private System.Collections.IEnumerator ApplySpritesAfterFrame()
{
    yield return null; // let scene objects finish Awake/OnEnable

    foreach (var renderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
    {
        if (renderer == null || renderer.sprite == null) continue;

        if (_replacements.TryGetValue(renderer.sprite.name, out var replacement))
            renderer.sprite = replacement;
    }
}
```

That is exactly the pattern used by
[`examples/ReskinPack`](../examples/ReskinPack). It is intentionally broad and
beginner-friendly: every visible `SpriteRenderer` with a matching sprite name is
changed.

### When simple replacement is not enough

The renderer scan covers many environmental props, banners, icons, and simple
objects. More advanced cases may need a targeted patch instead:

- A sprite is assigned after your scene scan runs.
- A sprite comes from a UI `Image`, not a `SpriteRenderer`.
- An object uses an animator, material property, tilemap, atlas, or custom draw
  path.
- You only want to replace one instance, not every renderer using that sprite
  name.

For those cases, patch the method that assigns the sprite, or find the specific
component and replace that one field. Keep the pack-loading step the same:
`LoadTexture`, `MakeSprite`, then assign the resulting `Sprite`.

### Sprite replacement checklist

- Name the PNG after the current in-game sprite name.
- Match the original sprite's dimensions and transparent padding first.
- Use `pixelsPerUnit: 16f` unless you know the target sprite uses a different
  scale.
- Use a bottom pivot such as `new Vector2(0.5f, 0f)` for ground-anchored art
  if a centered pivot floats or sinks.
- Reapply replacements after scene loads, because new renderers are created per
  scene.
- Do not redistribute extracted Kingdom Two Crowns sprites; ship only your own
  art.

## Kingdom.Mods - `ModsApi`

| Member | Notes |
|---|---|
| `RegisterToggle(label, get, set)` | Adds a checkbox to the F1 console. The mod owns state. |
| `RegisterChoice(label, options, get, set)` | Adds a radio-style choice row to the F1 console. |
| `RegisterHotkey(key, description)` | Adds a row to the Shortcuts guide. Documentation only. |
| `Toggles` / `Choices` / `Hotkeys` | Read-only lists used by the loader console. |

Registrations are idempotent by label/key, so re-registering replaces the old
entry instead of duplicating rows during hot reloads.

## Kingdom.CustomMounts - `CustomMountsApi`

| Member | Notes |
|---|---|
| `Register(id, label, tooltip, factory, baseMount)` | Adds a custom mount button to F1 -> Custom. |
| `Mounts` | Read-only list used by the loader console. |

The factory receives the selected `Player` and a log callback, then returns a
live scene `Steed` instance. The mod should instantiate a prefab, apply stats
and visuals, set it active near the player, and return it. The loader then calls
`Player.Ride(instance, replace: true, applyToCampaign: true)` so custom mounts
use the game's normal swap path.

```csharp
Kingdom.CustomMounts.Register(
    "shadow_wolf",
    "Shadow Wolf",
    "Fast night predator cloned from the wolf prefab.",
    (player, log) =>
    {
        var steed = Object.Instantiate(FindBasePrefab(SteedType.P1Wolf));
        steed.name = "Shadow Wolf";
        steed.transform.position = player.transform.position;
        steed.gameObject.SetActive(true);
        ApplyStatsAndSprites(steed);
        return steed;
    },
    "Wolf");
```

For a complete custom mount with embedded generated sprites and an overlay
animator, see [`examples/GloamHart`](../examples/GloamHart).

## HarmonyHelper

```csharp
public override void OnInitializeMelon()
{
    HarmonyHelper.PatchAll(this); // wires up every [HarmonyPatch] in your assembly
}
```

## Quick patterns

Subscribe to a daily callback:

```csharp
Kingdom.Time.OnDayChanged += () =>
    LoggerInstance.Msg($"Good morning, day {Kingdom.Time.DaysInReign}!");
```

Toggle the in-game InfiniteMoney cheat:

```csharp
Kingdom.Economy.InfiniteMoney = true;
```

Harmony-patch a game method by name:

```csharp
[HarmonyPatch(typeof(Il2Cpp.Director), nameof(Il2Cpp.Director.OnLevelLoaded))]
internal static class MyPatch
{
    private static void Postfix(Il2Cpp.Director __instance)
    {
        __instance.secondsPerInGameHour *= 1.5f; // 50% longer days
    }
}
```

The full class/method surface is in the Il2CppDumper output at
`docs/_generated/dump/dump.cs`. That file is game-derived and gitignored.
