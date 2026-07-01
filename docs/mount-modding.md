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
- Register the mount with `Kingdom.CustomMounts` so the F1 console shows it in
  the separate Custom Mounts menu.
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

## Registering The Mount In F1

Custom mounts should register with `Kingdom.CustomMounts`. The loader renders
registered mounts in F1 -> Custom, uses the same P1/P2 target selector as the
normal mount picker, and calls the game's own `Player.Ride` after your factory
returns a live `Steed`.

Register during `OnInitializeMelon`:

```csharp
Kingdom.CustomMounts.Register(
    "shadow_wolf",
    "Shadow Wolf",
    "Fast night predator cloned from the wolf prefab.",
    (player, log) =>
    {
        var basePrefab = FindBasePrefab(SteedType.P1Wolf);
        if (basePrefab == null)
        {
            log?.Invoke("Shadow Wolf: wolf prefab is not loaded yet.");
            return null;
        }

        var steed = Object.Instantiate(basePrefab);
        steed.name = "Shadow Wolf";
        steed.transform.position = player.transform.position;
        steed.gameObject.SetActive(true);
        ApplyStats(steed);
        ApplySprites(steed);
        return steed;
    },
    "Wolf");
```

The factory returns the customized mount instance; it should not call
`Player.Ride` itself. That keeps all registered custom mounts on the same safe
swap path and lets the loader log the F1 action.

`examples/GloamHart` is the current complete reference. It clones a
Reindeer/Stag-style prefab, applies forest-friendly deer-attracting stats, hides
the clone's **body** renderers (keeping the ruler/crown renderers so the mounted
monarch stays visible), attaches a custom `SpriteRenderer`, and animates 32
original frames from embedded PNG resources.

## Custom Mount Abilities

For custom behavior that does not already exist on the cloned base mount, keep
the first version small and local to the custom mount. `examples/GloamHart`
does this with **Gloam Rush**: the mod tracks live Gloam Hart instances, checks
the rider's mapped gallop/Shift button (`RewiredAxis.Gallop`, with a raw Shift
fallback), temporarily adjusts only that steed's movement/stamina fields, then
restores the captured values when the effect ends or the mount is no longer
ridden. The example also accepts `RewiredAxis.ActivateRulerAbility` as a
secondary input, but the Shift path is the important mount trigger.

Prefer the game's mapped input over raw keyboard checks so controller and P2
bindings can work. If you temporarily mutate live `Steed` fields, capture the
original values first and restore them on every cleanup path: timeout, mount
switch, scene unload, and destroyed/inactive mount. Visual feedback can live in
the same overlay/driver that animates your mount art, as long as it never
touches ruler or crown renderers.

## Embedded Sprites For Bundled Mount Mods

Pack folders are good for user-editable art. For a bundled example or a mod that
should deploy as a single DLL, embed your generated PNGs:

```xml
<ItemGroup>
  <EmbeddedResource Include="assets\my_mount\*.png" />
</ItemGroup>
```

At runtime, read `Assembly.GetExecutingAssembly().GetManifestResourceNames()`,
load each PNG stream into a `Texture2D`, then create `Sprite` objects with the
same rules as pack sprites: point filtering, clamp wrapping, a stable
pixels-per-unit value, and a pivot that keeps the mount grounded.

> **Mark runtime textures and sprites `HideFlags.HideAndDontSave`.** A mod loads
> its sprites in the boot scene, but the mount is created later, after the scene
> load into a run. Unity destroys ordinary runtime-created `Texture2D`/`Sprite`
> objects on that scene transition, so your frame lookup returns `null` and the
> mount renders nothing even though loading logged the right count. Set the flag
> right after creating each object so it survives the whole session:
>
> ```csharp
> texture.hideFlags = HideFlags.HideAndDontSave;
> sprite.hideFlags  = HideFlags.HideAndDontSave;
> ```

Gloam Hart uses 32 frames:

| Animation | Frames |
|---|---:|
| idle | 6 |
| walk | 8 |
| run | 8 |
| eat | 4 |
| rear | 3 |
| tired | 3 |

The frames are generated by `tools/create-gloam-hart-sprites.py`. The script
keeps dimensions, transparency, palette, and naming consistent, which is much
easier to maintain than hand-renaming a loose pile of PNGs.

## Asset Designer

For full mount sprite sets, use the local browser-based asset designer:

```powershell
tools\asset-designer.ps1
```

On first run it creates `build/asset-designer/`, seeds generated examples, and
tries to extract private reference images from your local Kingdom Two Crowns
install into `build/asset-designer/references/game/`. Those references are for
comparison only. They are game-derived files, are gitignored, and must never be
committed or redistributed.

The designer edits mount design JSON such as
`examples/GloamHart/design/gloam_hart.mount-design.json`. A design controls
frame size, animation frame counts, palette, body proportions, antlers, tail,
glow, motion curves, pivot, and export target. The browser UI previews
animations, shows a frame strip and local references, and exports:

- individual PNG frames
- a sprite sheet
- a preview GIF
- a mount-design JSON copy under the private workspace

`tools/create-gloam-hart-sprites.py` is now a compatibility wrapper around the
same renderer used by the designer, so command-line regeneration and browser
export use the same art recipe.

The same browser tool also has a **Challenges & Islands** tab. That tab reads
the F3 challenge and island dumps, lets you edit safe `ChallengeData` /
`LevelConfig` fields, and exports JSON into the F1 custom challenge selector.
See [Custom Challenges And Islands](custom-challenges.md).

## Overlay Animation Path

Some vanilla mount animation clips assign sprites internally. If you only
replace the currently visible sprite, the animator can switch back to vanilla
art on the next idle/walk/run transition.

For a complete custom mount without editing vanilla animation clips, use the
overlay path:

1. Clone the closest base `Steed` prefab.
2. Disable the clone's **body** `SpriteRenderer`s only — **not every child
   renderer**. See the ruler warning below.
3. Add one child `GameObject` with your own `SpriteRenderer`, and copy the body
   renderer's **material and sorting** onto it. See the render-context warning
   below.
4. Animate that renderer from your own frame list in `OnUpdate`.
5. Leave the cloned `Steed` components, colliders, movement, stamina, deer
   attraction, and ability behavior intact.

This is the path used by `examples/GloamHart`. It keeps the mount mechanically
vanilla-compatible while making the visible mount fully custom.

> **Do not disable every `SpriteRenderer` on the clone.** The `Steed` prefab
> also hosts the mounted **ruler (monarch)** and crown sprites as child
> GameObjects — they live under `riderAnchor`, in the `_riderObjectPairs`
> `Dictionary<MonarchType, GameObject>`, and in the `_crowns` list. A naive
> `steed.GetComponentsInChildren<SpriteRenderer>(true)` loop that sets
> `renderer.enabled = false` on everything will make the monarch **and** their
> crown invisible the moment they mount, because the game renders the rider
> through those same (now-disabled) renderers. Collect the ruler/crown renderers
> first and skip them, disabling only the steed's own body renderers:
>
> ```csharp
> // Renderers the mounted ruler and crowns use — never disable these.
> var keep = new HashSet<System.IntPtr>();
> foreach (var r in CollectRulerRenderers(steed)) // riderAnchor + _riderObjectPairs + _crowns
>     if (r != null && r.Pointer != System.IntPtr.Zero) keep.Add(r.Pointer);
>
> foreach (var renderer in steed.GetComponentsInChildren<SpriteRenderer>(true))
> {
>     if (renderer == null) continue;
>     if (renderer.Pointer != System.IntPtr.Zero && keep.Contains(renderer.Pointer))
>         continue; // ruler/crown renderer — leave enabled
>     renderer.enabled = false; // steed body renderer — hidden behind the overlay
> }
> ```
>
> See `AttachVisual` / `CollectRulerRenderers` in
> [`examples/GloamHart/GloamHartMod.cs`](../examples/GloamHart/GloamHartMod.cs)
> for the full, defensive version.

> **Give your overlay renderer the game's render context, or it draws
> invisibly.** A bare `gameObject.AddComponent<SpriteRenderer>()` gets Unity's
> default sprite material and the **default sorting layer** at order 0 — the
> wrong layer/material for this game, so the sprite never appears. Resolve the
> steed's own body renderer and copy its context. Resolve it via the typed
> `Steed.SpriteFX` (a `SpriteRendererFX` wrapping the body `SpriteRenderer`),
> **not** by scanning for the first child renderer whose `sprite != null`: the
> body animator has not run its first frame when you clone the prefab, so that
> sprite is usually null and your scan finds nothing.
>
> ```csharp
> // Body renderer, resolved even before the animator assigns a sprite.
> SpriteRenderer body = steed.SpriteFX != null
>     ? steed.SpriteFX.GetComponent<SpriteRenderer>()
>     : null; // fall back to the first non-ruler child renderer if null
>
> var overlay = visualObject.AddComponent<SpriteRenderer>();
> overlay.enabled = true;
> overlay.color = Color.white;
> if (body != null)
> {
>     overlay.sharedMaterial = body.sharedMaterial;   // the scene's sprite shader
>     overlay.sortingLayerID = body.sortingLayerID;   // same sorting layer
>     overlay.sortingOrder   = body.sortingOrder + 1; // just above the hidden body
>     // Parent under body.transform.parent and copy its localPosition/localScale
>     // so the overlay lands exactly where the body did.
> }
> ```

## Complete Skeleton Mod

```csharp
using System.Collections.Generic;
using System.IO;
using Il2Cpp;
using KingdomMod;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

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

            Kingdom.CustomMounts.Register(
                "shadow_wolf",
                "Shadow Wolf",
                "Custom wolf mount cloned from the vanilla wolf path.",
                CreateMount,
                "Wolf");
        }

        private static Steed CreateMount(Player player, System.Action<string> log)
        {
            if (player == null) return null;

            var basePrefab = FindBasePrefab(SteedType.P1Wolf);
            if (basePrefab == null)
            {
                log?.Invoke("Shadow Wolf: base wolf prefab is not loaded yet.");
                return null;
            }

            var steed = Object.Instantiate(basePrefab);
            steed.name = "Shadow Wolf";
            steed.transform.position = player.transform.position;
            steed.gameObject.SetActive(true);

            ApplyStats(steed);
            ApplySprites(steed);

            return steed; // the loader calls Player.Ride for registered custom mounts
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
    }
}
```

For a one-DLL mod with embedded sprites and a custom overlay animator, use
`examples/GloamHart` as the reference instead of the smaller pack-based skeleton
above.

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
| Monarch (and crown) turn invisible once mounted | You disabled every child `SpriteRenderer` on the clone, including the rider/crown renderers the `Steed` hosts | Disable only the steed's body renderers; skip renderers under `riderAnchor`, `_riderObjectPairs`, and `_crowns` (see the overlay-path warning above). |
| Custom overlay sprite is invisible | Your new `SpriteRenderer` got the default material and/or the default sorting layer (the reference body sprite was null at instantiation, so a `sprite != null` scan found nothing) | Resolve the body renderer via `Steed.SpriteFX` and copy `sharedMaterial` + `sortingLayerID` + `sortingOrder` + transform onto the overlay (see the render-context warning above). |
| Frames load at startup but the mount renders nothing later | Runtime `Texture2D`/`Sprite` objects were destroyed on the scene load into the run, so the frame lookup returns `null` | Set `hideFlags = HideFlags.HideAndDontSave` on every generated texture and sprite (see the embedded-sprites note). |
| Mount is far too big or small | Wrong `pixelsPerUnit` for the source frame size | Pick PPU = frameHeightPx / desiredWorldHeight (e.g. 64px art at ~2 units → PPU 32). |
| Mount does not turn with the monarch | You flipped only from the mount's own movement, so it never turns while standing | Mirror the rider's on-screen facing (largest active sprite under `Steed.riderAnchor`, combining `flipX` with transform-scale sign). |
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
