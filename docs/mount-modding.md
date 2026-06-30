# Mount Modding Guide

This guide is the end-to-end path for making a custom mount in Kingdom Two
Crowns with KingdomMod. It covers the full loop:

1. Inspect existing mounts.
2. Pick a base mount to clone.
3. Change stats and behavior.
4. Replace mount sprites or animation frames with your own art.
5. Add the mount to an in-game selector.
6. Test, package, and debug it.

Mounts are game `Steed` objects. A complete custom mount is usually a cloned
existing `Steed` prefab with modified fields and replaced visual assets. This is
safer than trying to build a mount prefab from nothing, because the existing
prefab already has the right colliders, renderers, animator, audio hooks,
network/saving hooks, and mount ability components.

KingdomMod ships no Kingdom Two Crowns art. Custom mount packs must contain only
your own PNGs or other assets you have permission to distribute.

## Quick Checklist

- Run the game with KingdomMod installed.
- Press **F3** after entering a run or opening menus that load mount assets.
- Read `<KTC>/UserData/KingdomMod/dump/steeds.json`.
- Choose a base mount with the closest behavior and animation set.
- Clone that prefab at runtime.
- Change `Steed` fields such as speed, stamina, eating, and deer attraction.
- Replace sprites on the clone before calling `Player.Ride`.
- Swap the player onto the clone with `Player.Ride(instance, replace: true,
  applyToCampaign: true)`.
- Test P1 and P2, save/load, scene transition, stamina, eating, ability, and
  switching away/back.

## Inspect Existing Mounts

The F3 dumper writes loaded mount data to:

```text
<KTC>/UserData/KingdomMod/dump/steeds.json
```

That file shows the `Steed` fields that are safe to start experimenting with:
speed, stamina, eating rules, deer attraction, recoloring flags, and animation
flags.

Not every mount prefab is loaded at the main menu. Enter a run, open relevant
menus, or switch biome/content before dumping if a mount seems missing.

## Choose A Base Mount

Pick the base mount by behavior first, art second.

| Desired custom mount | Good base choice |
|---|---|
| Normal horse with new art | P1/P2 default horse |
| Deer-luring mount | Stag or Reindeer |
| Night-eating predator | Wolf |
| Coin-generating or special economy behavior | Unicorn-style mount |
| Fast stamina-focused mount | Griffin or fast horse |
| Defensive/charge behavior | Warhorse-style mount |
| Fire/attack ability | Lizard-style mount |

Use the closest existing ability path. It is much easier to reskin and tune a
wolf into a custom predator than to add wolf-only night-eating behavior to a
plain horse from scratch.

## Prefabs Versus Live Mounts

The game exposes mount data through `Il2Cpp.Steed`.

Prefab mounts usually have `steed.gameObject.scene.handle == 0`. Live scene
mounts have a nonzero scene handle.

For a mount selector, use prefabs:

```csharp
foreach (var steed in Resources.FindObjectsOfTypeAll<Steed>())
{
    if (steed == null) continue;
    if (steed.gameObject.scene.handle != 0) continue; // skip live scene mounts
    if (steed.steedType == SteedType.INVALID) continue;

    // Add to selector.
}
```

For runtime effects on the currently ridden mount, use live scene instances:

```csharp
static bool IsActiveSceneSteed(Steed steed)
{
    return steed != null
        && steed.gameObject != null
        && steed.gameObject.scene.handle != 0
        && steed.gameObject.activeInHierarchy;
}
```

Do not pass a live scene mount as if it were a prefab to a selector. Instantiate
the prefab, customize the instance, then ride the instance.

## Minimal Custom Mount Flow

```csharp
static void RideCustomMount(Player player, Steed basePrefab)
{
    var instance = UnityEngine.Object.Instantiate(basePrefab);
    instance.name = "My Custom Mount";
    instance.transform.position = player.transform.position;
    instance.gameObject.SetActive(true);

    ApplyStats(instance);
    ApplySprites(instance);

    player.Ride(instance, replace: true, applyToCampaign: true);
}
```

`Player.Ride(Steed, bool, bool)` is the game's own mount-swap path. Use it
instead of trying to parent the player manually.

`replace: true` replaces the current mount. `applyToCampaign: true` asks the
game to persist the chosen mount through its campaign path where applicable.

## Changing Mount Stats

Change fields on the cloned `Steed` before `Player.Ride`:

```csharp
static void ApplyStats(Steed steed)
{
    steed.walkSpeed = 2.2f;
    steed.runSpeed = 5.0f;
    steed.forestSpeedMultiplier = 1.0f;

    steed.walkStaminaRate = 0.06f;
    steed.runStaminaRate = -0.06f;
    steed.standStaminaRate = 0.22f;
    steed.reserveStamina = 0.35f;
    steed.reserveProbability = 0.75f;

    steed.canEat = true;
    steed.onlyEatsAtNight = false;
    steed.eatDelay = 0.5f;
    steed.eatDuration = 3.0f;
    steed.wellFedDuration = 48.0f;
    steed.tiredDuration = 18.0f;

    steed.attractsDeer = true;
}
```

Keep first experiments conservative. Very high speeds or extreme stamina values
can expose animation, collision, camera, or network assumptions.

## Steed Field Reference

These fields come from the F3 `steeds.json` dump and the generated
`Il2Cpp.Steed` reference. The exact gameplay formula can still be hidden inside
IL2CPP method bodies, but the field purposes are stable enough for modding.

| Field | Meaning |
|---|---|
| `assetName` | Unity object or prefab name, such as `Wolf P1`. Useful for display and debugging. |
| `steedType` | Game enum identity used by save/swap logic, such as `P1Wolf`, `Stag`, or `Unicorn`. For fully custom clones, you usually keep the base type and distinguish the mount by your mod's selector/name. |
| `walkSpeed` | Base walking speed. |
| `runSpeed` | Base gallop/run speed. |
| `forestSpeedMultiplier` | Movement multiplier in forest/rough movement contexts. `0.9` means 90% speed there. |
| `walkStaminaRate` | Stamina change while walking. Positive values recover stamina. |
| `runStaminaRate` | Stamina change while running. Negative values drain stamina. |
| `glideStaminaRate` | Stamina change during glide or special movement states. Often only meaningful for mounts with that ability path. |
| `standStaminaRate` | Stamina recovery while standing still. |
| `reserveStamina` | Backup stamina amount the mount can sometimes use after normal stamina is low or exhausted. |
| `reserveProbability` | Chance to receive/use reserve stamina. `0.7` means 70%. |
| `glidePayThreshold` | Coin/payment threshold tied to glide or special ability behavior. Present on all mounts, but not always relevant. |
| `rearingDuration` | How long the mount rears up. |
| `rearingCooldown` | Cooldown before rearing or an ability-like action can happen again. |
| `canEat` | Whether the mount can graze/eat to recover stamina. |
| `eatDelay` | Delay before eating starts after conditions are met. |
| `eatFullStaminaDelay` | Extra timing around reaching full stamina from eating. |
| `eatDuration` | How long the eating action lasts. |
| `onlyEatsAtNight` | Whether eating is restricted to night. Wolves use this. |
| `wellFedDuration` | How long the well-fed/full-stamina benefit lasts after eating. |
| `tiredDuration` | How long the mount stays tired after exhausting stamina. |
| `eatAmbientThreshold` | Environmental/ambience threshold for eating. `0` means no extra threshold. |
| `attractsDeer` | Whether deer are attracted to/follow this mount. Stag/reindeer use this naturally. |
| `wanderRange` | How far an unridden/idle mount can wander. |
| `recolorToCoatOfArms` | Whether mount sprites get recolored to player/kingdom coat-of-arms colors. Disable this if your custom art should keep exact colors. |
| `hasBirthAnim` | Whether the mount has a birth/spawn animation path. |
| `disableSteedSwitching` | If true, blocks normal switching away from or through this steed. |
| `resumesGallopingAfterUsingAbility` | Whether the mount resumes galloping automatically after its special ability finishes. |
| `forwardAnimsToRuler` | Whether mount animation events are forwarded to the monarch/rider animation system. |

## Custom Mount Art

Most visible mount art is rendered through `SpriteRenderer` components on the
mount object and its children. Some mounts also use additional renderers for
effects, armor, mane/tail pieces, glow overlays, or biome variants.

The `Player` class also has `_steedRenderer` and
`_additionalSteedSpriteRenderers`, but for custom mounts start by changing the
renderers on the cloned `Steed` instance. The ride path copies/uses the mount's
visual setup.

### Pack Layout

Use a pack folder next to your mod DLL:

```text
Mods/
  MyCustomMount.dll
  MyCustomMount/
    pack/
      kingdommod.pack.json
      sprites/
        shadow_wolf_body_idle_0.png
        shadow_wolf_body_walk_0.png
        shadow_wolf_body_walk_1.png
        shadow_wolf_body_run_0.png
        shadow_wolf_body_run_1.png
```

Example `kingdommod.pack.json`:

```json
{
  "name": "MyCustomMount",
  "version": "0.1.0",
  "author": "Your name"
}
```

The file names are your mod's lookup keys. They do not need to match the game's
original sprite names unless you are using broad ReskinPack-style replacement.

### Load Sprites

```csharp
private static readonly Dictionary<string, Sprite> Sprites = new();

static void LoadMountSprites()
{
    foreach (var pack in Kingdom.Packs.DiscoverPacks(MelonEnvironment.ModsDirectory))
    {
        if (pack.Name != "MyCustomMount" || !pack.HasSprites) continue;

        foreach (var path in Directory.EnumerateFiles(pack.SpritesDirectory, "*.png"))
        {
            var key = Path.GetFileNameWithoutExtension(path);
            var texture = Kingdom.Packs.LoadTexture(path);
            var sprite = Kingdom.Packs.MakeSprite(
                texture,
                pixelsPerUnit: 16f,
                pivot: new Vector2(0.5f, 0.5f));

            if (sprite != null)
                Sprites[key] = sprite;
        }
    }
}
```

`LoadTexture` preserves the pixel-art look with point filtering and clamp wrap
mode. `MakeSprite` builds a Unity `Sprite` from your PNG.

### Preserve Scale And Alignment

When replacing an existing frame, copy the original sprite's important settings:

```csharp
static Sprite MakeReplacementLike(Sprite original, Texture2D texture)
{
    var ppu = original != null ? original.pixelsPerUnit : 16f;
    var pivotPixels = original != null
        ? original.pivot
        : new Vector2(texture.width * 0.5f, texture.height * 0.5f);
    var pivot = new Vector2(
        pivotPixels.x / texture.width,
        pivotPixels.y / texture.height);

    return Kingdom.Packs.MakeSprite(texture, ppu, pivot);
}
```

If the mount floats, sinks, or slides, check these first:

- PNG width and height.
- Transparent padding around the drawing.
- `pixelsPerUnit`.
- Pivot.
- Whether the renderer is flipped or offset by a child transform.

### Replace The Current Visible Sprites

This changes every renderer currently visible on the clone. It is good enough
for a static reskin or a first prototype:

```csharp
static void ApplySprites(Steed steed)
{
    foreach (var renderer in steed.GetComponentsInChildren<SpriteRenderer>(true))
    {
        if (renderer == null || renderer.sprite == null) continue;

        var oldName = renderer.sprite.name;
        if (Sprites.TryGetValue(oldName, out var replacement))
        {
            replacement.name = oldName;
            renderer.sprite = replacement;
        }
    }
}
```

This approach depends on your PNG names matching the current sprite names. To
discover those names, log the renderers on a cloned mount:

```csharp
static void LogMountRenderers(Steed steed, MelonLogger.Instance logger)
{
    foreach (var renderer in steed.GetComponentsInChildren<SpriteRenderer>(true))
    {
        var spriteName = renderer.sprite != null ? renderer.sprite.name : "(none)";
        logger.Msg($"{BuildPath(renderer.transform)} -> {spriteName}");
    }
}

static string BuildPath(Transform t)
{
    var parts = new List<string>();
    while (t != null)
    {
        parts.Add(t.name);
        t = t.parent;
    }
    parts.Reverse();
    return string.Join("/", parts);
}
```

## Replacing Animation Frames

For a complete custom mount, replacing only the currently visible sprite is not
enough. When the animator changes from idle to walk/run/eat/tired, it may assign
different sprites from animation clips or sprite libraries.

Use one of these strategies:

### Strategy A: Name-Matched Global Replacement

This is easiest and matches `examples/ReskinPack`:

1. Dump/log every sprite name used by the base mount.
2. Create a replacement PNG for each frame with the same name.
3. Load those PNGs into a dictionary.
4. On scene load and after mount instantiation, scan `SpriteRenderer`s and
   replace matching sprite names.
5. Patch or rescan after state changes if vanilla reassigns sprites later.

This works best for replacing the look of an existing mount everywhere.

### Strategy B: Targeted Clone Replacement

This is better for adding one custom mount without changing all wolves/horses:

1. Clone the base prefab.
2. Traverse the clone's renderers and animator-related children.
3. Replace sprites only under that clone.
4. Reapply after `Player.Ride` and after likely animation changes.

If animation clips directly reference sprites, the renderer may get reset by the
animator on the next animation frame. In that case, add a lightweight postfix to
mount animation methods such as `Steed.SetAnimation(int)` or reapply during a
short startup window after riding.

Example reapply-after-ride patch:

```csharp
[HarmonyPatch(typeof(Player), nameof(Player.Ride),
    new[] { typeof(Steed), typeof(bool), typeof(bool) })]
internal static class PlayerRidePatch
{
    private static void Postfix(Steed steed)
    {
        if (steed == null || steed.name != "My Custom Mount") return;
        MyMountSprites.ApplySprites(steed);
    }
}
```

### Strategy C: Replace Animator Clip Bindings

This is the most complete path, but also the most advanced. Inspect the
`Animator`, `Animation`, or child renderers on the base mount, then replace the
sprites referenced by the clips or sprite libraries with your own sprites.

Use this only when renderer assignment keeps being overwritten and targeted
reapply is not enough. Keep the same frame count, timing, and sprite dimensions
at first. Once the mount works, experiment with new animation timing.

## Renderer And Sprite Discovery

A good custom mount mod should include a debug hotkey or one-time log that lists
the clone's renderers, child paths, and sprite names.

```csharp
static void DumpSteedVisuals(Steed steed, MelonLogger.Instance logger)
{
    foreach (var renderer in steed.GetComponentsInChildren<SpriteRenderer>(true))
    {
        var sprite = renderer.sprite;
        logger.Msg(
            $"renderer={BuildPath(renderer.transform)} " +
            $"sprite={(sprite != null ? sprite.name : "(none)")} " +
            $"ppu={(sprite != null ? sprite.pixelsPerUnit : 0)} " +
            $"size={(sprite != null ? sprite.rect.width + "x" + sprite.rect.height : "-")} " +
            $"pivot={(sprite != null ? sprite.pivot.ToString() : "-")}");
    }
}

static string BuildPath(Transform t)
{
    var parts = new List<string>();
    while (t != null)
    {
        parts.Add(t.name);
        t = t.parent;
    }
    parts.Reverse();
    return string.Join("/", parts);
}
```

Use this output as your art checklist. If the base mount has 30 animation frame
sprites and you replace 3, the mount will visually snap back to vanilla during
unreplaced animations.

## Coat-Of-Arms Recoloring

Some mounts set `recolorToCoatOfArms = true`. That means the game may recolor
parts of the mount based on kingdom/player colors.

For exact custom art colors:

```csharp
steed.recolorToCoatOfArms = false;
```

For banner-colored custom art, leave it true and design the relevant sprite
regions to tolerate recoloring. Test both P1 and P2 because their mount variants
and colors can differ.

## Adding The Mount To F1 Or F4

For a first custom mount, use an F1 button or a mod choice that calls your ride
function. For a full selector, follow `examples/AnyMount`: build a list of
available base prefabs, add your synthetic custom option, and when clicked,
clone/customize/ride it.

Simple F1 choice:

```csharp
Kingdom.Mods.RegisterChoice("Custom mount",
    new[] { "Off", "Shadow Wolf" },
    () => 0,
    idx =>
    {
        if (idx == 1)
            RideShadowWolf(FindPlayer(0));
    },
    "Switch Player 1 to the custom Shadow Wolf mount.");
```

For a real user-facing mod, add a P1/P2 target selector or use the existing F1
mount target pattern from the loader console.

## Complete Skeleton Mod

```csharp
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Il2Cpp;
using KingdomMod;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(KingdomMod.Examples.MyCustomMount.MyMountMod),
    "My Custom Mount", "0.1.0", "Your name")]
[assembly: MelonGame("noio", "KingdomTwoCrowns")]

namespace KingdomMod.Examples.MyCustomMount
{
    public sealed class MyMountMod : MelonMod
    {
        private static readonly Dictionary<string, Sprite> Sprites = new();

        public override void OnInitializeMelon()
        {
            LoadSprites();
            HarmonyHelper.PatchAll(this);

            Kingdom.Mods.RegisterChoice("My mount",
                new[] { "Off", "Ride" },
                () => 0,
                idx => { if (idx == 1) RideCustom(FindPlayer(0)); },
                "Ride the custom mount.");
        }

        private static void RideCustom(Player player)
        {
            if (player == null) return;

            var basePrefab = FindBasePrefab(SteedType.P1Wolf);
            if (basePrefab == null) return;

            var steed = Object.Instantiate(basePrefab);
            steed.name = "Shadow Wolf";
            steed.transform.position = player.transform.position;
            steed.gameObject.SetActive(true);

            ApplyStats(steed);
            ApplySprites(steed);

            player.Ride(steed, replace: true, applyToCampaign: true);
        }

        private static void LoadSprites()
        {
            foreach (var pack in Kingdom.Packs.DiscoverPacks(MelonEnvironment.ModsDirectory))
            {
                if (pack.Name != "MyCustomMount" || !pack.HasSprites) continue;

                foreach (var path in Directory.EnumerateFiles(pack.SpritesDirectory, "*.png"))
                {
                    var key = Path.GetFileNameWithoutExtension(path);
                    var texture = Kingdom.Packs.LoadTexture(path);
                    var sprite = Kingdom.Packs.MakeSprite(texture);
                    if (sprite != null)
                        Sprites[key] = sprite;
                }
            }
        }

        private static void ApplyStats(Steed steed)
        {
            steed.walkSpeed = 2.2f;
            steed.runSpeed = 5.0f;
            steed.forestSpeedMultiplier = 1.0f;
            steed.runStaminaRate = -0.06f;
            steed.standStaminaRate = 0.22f;
            steed.attractsDeer = true;
            steed.recolorToCoatOfArms = false;
        }

        private static void ApplySprites(Steed steed)
        {
            foreach (var renderer in steed.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer == null || renderer.sprite == null) continue;
                var oldName = renderer.sprite.name;
                if (!Sprites.TryGetValue(oldName, out var replacement)) continue;

                replacement.name = oldName;
                renderer.sprite = replacement;
            }
        }

        private static Steed FindBasePrefab(SteedType type)
        {
            foreach (var steed in Resources.FindObjectsOfTypeAll<Steed>())
            {
                if (steed == null) continue;
                if (steed.gameObject.scene.handle != 0) continue;
                if (steed.steedType == type) return steed;
            }
            return null;
        }

        private static Player FindPlayer(int playerId)
        {
            foreach (var player in Kingdom.Players.All)
                if (player != null && player.playerId == playerId) return player;
            return null;
        }
    }
}
```

## Testing Checklist

- The custom mount appears near the chosen monarch.
- `Player.Ride` succeeds and the previous mount is replaced cleanly.
- Walk/run speed feels right on open ground and in forest.
- Stamina drains, recovers, eating, tired state, and well-fed state behave as
  intended.
- Ability-specific behavior still works if using wolf/stag/unicorn/lizard/etc.
- Every animation state uses custom art: idle, walk, run, eat, tired, rear,
  special ability, hit/impact if present.
- The mount does not visually snap back to vanilla frames.
- P1 and P2 both work, or the mod clearly says it is P1-only.
- Save/load and island transition do not break the mount.
- Switching away and back does not leave stale modified fields on vanilla
  mounts.
- No extracted game sprites are included in the mod package.

## Common Problems

| Problem | Likely cause | Fix |
|---|---|---|
| Mount vanishes or pulls the original scene mount with it | You used a live scene mount as the source | Use a prefab where `scene.handle == 0`, instantiate it, then ride the clone. |
| Art appears offset | Pivot, transparent padding, or child transform mismatch | Match original frame size/padding and copy the original sprite pivot/PPU. |
| Art is too large or too small | Wrong `pixelsPerUnit` or image dimensions | Use the original sprite's `pixelsPerUnit`; start with matching dimensions. |
| Mount changes back to vanilla while moving | Animator assigned unreplaced frames | Replace all animation frames or reapply in/after animation paths. |
| Colors change unexpectedly | `recolorToCoatOfArms` is true | Set it false for exact colors, or design art for recoloring. |
| Ability does nothing | Base mount does not have that ability path | Clone a mount that already has the desired ability. |
| P2 looks different | P1/P2 use separate prefabs or color setup | Test and customize P1/P2 variants separately. |
| Save/load loses the custom mount | The game persists the base `steedType`, not your synthetic identity | Reapply your custom clone on load or expose the mount as an explicit selector option. |

## Practical Safety Notes

- Clone existing mounts; do not build a `Steed` from an empty `GameObject` unless
  you are ready to recreate many hidden dependencies.
- Keep first custom mounts as reskins/stat variants of a close base mount.
- Keep Harmony patches tiny: check whether your feature applies, change the
  field/sprite, and let vanilla continue.
- Cache original values when mutating live vanilla mounts so toggles can restore
  them.
- Treat multiplayer as unsafe unless every player has the same mount mod and
  asset pack.
- Do not redistribute extracted Kingdom Two Crowns sprites, atlases, prefabs, or
  generated interop assemblies.
