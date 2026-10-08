# ReskinPack

ReskinPack loads replacement PNGs and applies them to matching `SpriteRenderer`
frames, including animated monarchs. Vanilla animation still chooses the frame;
ReskinPack substitutes the matching image in `LateUpdate`.

## Install a sprite pack

Put individual replacement frames here, then restart the game:

```text
Kingdom Two Crowns/
  Mods/
    KingdomMod.Examples.ReskinPack.dll
    ReskinPack/
      pack/
        sprites/
          player_armour_king_walk_0.png
          player_armour_king_walk_1.png
```

The PNG filename without its extension must exactly match the runtime sprite
name, including case. The example filenames only apply if the active monarch
actually uses those frames. Monarch, gender, biome, mounted state, and animation
can select different sprites.

Use the individual sprite's name, not its containing atlas texture's name.
For example, `sactx-2-2048x2048-Uncompressed-rulers-5468bcf2` is an atlas name;
ReskinPack does not slice or replace an atlas. Each PNG contains one complete
frame, with dimensions and transparent padding matching the original frame.
Supply your own artwork.

The mod copies the original frame's pixels-per-unit and normalized pivot when
constructing a replacement. Mismatched PNG dimensions produce a warning because
they can change world size and alignment. Every animation frame you want to
change needs its own PNG; frames without a matching PNG stay vanilla.

## Animated and newly spawned objects

The mod checks cached renderers every `LateUpdate` to reapply replacements after
animation evaluation. It discovers renderers every half-second and immediately
resets discovery on scene initialization. Inactive renderers are discovered too,
but are only changed once active and enabled. This covers monarchs created after
the initial scene load and appearance or mount changes.

Replacement textures and sprites survive scene transitions. Frame variants are
cached by original sprite instance, so frames with the same name but different
pivots or scale get their own correctly constructed replacement.

## Check Latest.log

- `Loaded 4 sprite replacement(s)` means four uniquely named images were loaded;
  it does not mean the game displayed or matched them.
- `Matched sprite 'player_armour_king_walk_0' ...` confirms a replacement was
  assigned to a live renderer. Each name is reported once per game session.
- `Not observed yet ...` lists loaded names that have not matched so far. This
  summary appears five seconds after scene initialization; those frames may
  still match later when you walk or change monarch/scene.
- Decode failures, duplicate names, and mismatched dimensions produce warnings.

If a loaded name never matches while you play the relevant animation, verify the
active ruler's runtime sprite names rather than assuming every extracted sprite
is used by that ruler. UI `Image`s, tilemaps, material-only textures, and custom
rendering systems are outside this example's `SpriteRenderer` support.

See [Sprite construction and replacement](../../docs/api-reference.md#sprite-construction-and-replacement)
for the pack API and [Mount modding](../../docs/mount-modding.md) for custom mounts.
