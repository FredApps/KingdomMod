# Gloam Hart

Gloam Hart is a complete custom mount example.

It registers with `Kingdom.CustomMounts`, appears in the F1 `Custom` mount menu,
clones a Reindeer/Stag-style `Steed` prefab, applies custom forest-friendly
deer-attracting stats, hides the clone's **body** renderers (while keeping the
mounted ruler and crown renderers enabled so the monarch stays visible), and
animates original embedded PNG frames through a lightweight overlay
`SpriteRenderer`.

While ridden, Gloam Hart also demonstrates a managed custom mount ability:
press Shift or the game's mapped gallop button to trigger **Gloam Rush**, a
short moonlit speed burst with reduced stamina drain and a glowing body pulse.
The ability stores the mount's original stat values and restores them when the
rush ends, the mount is no longer ridden, or the visual tracker is cleaned up.

The editable design lives in `design/gloam_hart.mount-design.json`. The 32
generated frames live in `assets/gloam_hart/`:

- idle: 6
- walk: 8
- run: 8
- eat: 4
- rear: 3
- tired: 3

Regenerate them directly with:

```powershell
python tools\create-gloam-hart-sprites.py
```

Or launch the local designer:

```powershell
tools\asset-designer.ps1
```

The designer creates a private workspace under `build/asset-designer/`, extracts
local game reference images there when possible, previews animations in a
browser, and exports frames back to this example.

The generated sprites are original KingdomMod example art. They are informed by
the mount dump stats and common Kingdom Two Crowns mount proportions, but they
do not include extracted game sprites.
