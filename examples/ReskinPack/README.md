# ReskinPack

ReskinPack loads replacement PNGs and applies them to matching `SpriteRenderer`
frames, including animated monarchs. Vanilla animation still chooses the frame;
ReskinPack substitutes the matching image in `LateUpdate` and again immediately
before camera rendering, to cover later sprite assignments by game components.

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

The PNG filename without its extension matches the runtime sprite name.
Matching ignores case and surrounding whitespace, and falls back to removing
trailing Unity `(Clone)` suffixes. An explicitly named clone replacement takes
precedence over that fallback. Subfolders under `sprites/` are searched too;
filenames must be unique across them. The example filenames apply if the active monarch
actually uses those frames. Monarch, gender, biome, mounted state, and animation
can select different sprites.

Use the individual sprite's name, not its containing atlas texture's name.
For example, `sactx-2-2048x2048-Uncompressed-rulers-5468bcf2` is an atlas name;
ReskinPack does not slice or replace an atlas. Each PNG contains one complete
frame, with dimensions and transparent padding matching the original frame.
Supply your own artwork.

Sharing a texture PathID is normal: several frames can reference different
regions of the same atlas. Do not rename all frames to the shared texture name.
`player_armour_king_walk` is an animation name, while `_0` through `_7` are eight
individual sprite frames. Biome suffixes are meaningful and are not removed
during matching.

The mod copies the original frame's pixels-per-unit and normalized pivot when
constructing a replacement. Mismatched PNG dimensions produce a warning because
they can change world size and alignment. Every animation frame you want to
change needs its own PNG; frames without a matching PNG stay vanilla.

## Animated and newly spawned objects

The mod checks cached renderers every `LateUpdate` and immediately before the
game's `KingdomRenderPipeline.DrawCamera` method using a Harmony prefix.
Kingdom uses a custom render pipeline, so this does not rely on Unity's
built-in `Camera.onPreRender` or `MonoBehaviour.OnPreRender` callbacks.
The camera callback also works if a replacement is overwritten after the mod's
`LateUpdate`. It discovers renderers every half-second and immediately
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
- Startup explicitly prints `ReskinPack 0.1.2` and the loaded DLL path, helping
  identify an old or duplicate installation. This is the example mod's version,
  not the KingdomMod release version.
- Summaries include discovered renderer counts and whether `LateUpdate` and the
  camera callback have run. They are emitted from `Update`, so a missing render
  callback does not hide the diagnostic summary.

## Runtime sprite diagnostics

`UserData/KingdomMod/logs/reskin-latest.jsonl` is overwritten at mod startup and
records loaded PNG paths, observed runtime sprite names, suggested filenames,
renderer hierarchy, texture name, dimensions, pivot, pixels-per-unit, shader,
alpha, and matches. Observations include unmatched sprites so you can identify
the actual monarch/biome frames instead of guessing from exported assets.

The file is buffered and flushed every second and on shutdown. Observations are
deduplicated by scene, renderer and sprite name, with a cap of 8,192 records plus
a final limit marker. It does not contain extracted images. Logging failures do
not stop replacement. When reporting a problem, include this file, the complete
`MelonLoader/Latest.log`, your pack, and the campaign/monarch used.

If a loaded name never matches while you play the relevant animation, verify the
active ruler's runtime sprite names rather than assuming every extracted sprite
is used by that ruler. UI `Image`s, tilemaps, material-only textures, and custom
rendering systems are outside this example's `SpriteRenderer` support.

See [Sprite construction and replacement](../../docs/api-reference.md#sprite-construction-and-replacement)
for the pack API and [Mount modding](../../docs/mount-modding.md) for custom mounts.
