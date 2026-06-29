# KingdomMod — Capabilities & Limits

This document answers the question *"what kind of mods can this platform make?"* for
Kingdom Two Crowns (Unity 6 / IL2CPP, build 2.4.0). It is grounded in the build's
technical reality, verified during setup:

- **Engine:** Unity `6000.0.61f1`, scripting backend **IL2CPP**.
- **Code:** AOT‑compiled into `GameAssembly.dll`. `global-metadata.dat` is
  **unencrypted** (magic `0xFAB11BAF`, version 31) → the full class/method surface is
  recoverable, so runtime patching with Harmony works.
- **Native plugins:** Rewired (input), Steamworks.NET, GOG Galaxy, Xbox GDK, and
  **Burst** (`lib_burst_generated.dll`).

Mods are C# DLLs that run under MelonLoader, compiled against the SDK
(`KingdomMod.Api`). The SDK wraps the generated interop so you patch *named* game
methods, not raw offsets.

---

## What you CAN do

### Tier 1 — Balance & economy (Easy)
Change numeric/config values the game reads at runtime.
- Costs/prices (towers, walls, recruits, upgrades), starting/earned coins & gems.
- Unit caps, archer/worker/farmer counts, spawn quantities.
- Timers: day/night length, season length, build/regen times.
- **How:** Harmony postfix on getters, or override fields on the relevant
  `ScriptableObject`/config singletons; or load values from a JSON pack.
- **Example mod:** [`examples/BalanceTweaks`](../examples/BalanceTweaks).

### Tier 2 — Behaviour & rules (Moderate)
Change *what the game does*, not just numbers.
- Greed/enemy wave composition and cadence, portal behaviour, blood‑moon rules.
- Purchase/recruitment logic, AI decisions, season/weather effects.
- **How:** Harmony prefix/postfix/transpiler on the relevant methods, driven through
  SDK events (day start, nightfall, wave spawn, purchase, unit spawn, …).
- **Example mod:** [`examples/GameplayTweaks`](../examples/GameplayTweaks).

### Tier 3 — UI & HUD (Moderate)
- Immediate‑mode overlays (stats, timers, debug read‑outs) via MelonLoader's IMGUI.
- Inject/alter uGUI elements on existing canvases.
- **How:** `OnGUI` in the loader/mod, or find canvases via the SDK and add children.

### Tier 4 — Reskins & audio (Moderate)
- Replace sprites/textures (monarch, mounts, banners, environment), audio clips,
  and music at runtime.
- **How:** the SDK's pack loader swaps `Texture2D`/`Sprite`/`AudioClip` references
  from a pack folder — **no game art is shipped; you supply your own.**
- **Example mod:** [`examples/ReskinPack`](../examples/ReskinPack).
- **Sprite details:** see
  [`Sprite construction and replacement`](api-reference.md#sprite-construction-and-replacement)
  for PNG naming, `LoadTexture`, `MakeSprite`, pixels-per-unit, pivots, and
  scene replacement patterns.

### Tooling — Sandbox, cheats, dev console (Easy)
- In‑game console (toggle with F1) to give currency, spawn units, set time scale,
  toggle invulnerability, inspect state.
- **Example mod:** [`examples/SandboxConsole`](../examples/SandboxConsole).

### Tier 5 — New content (Hard)
- New units, upgrades, decrees, mounts: clone an existing prefab, swap art/data,
  register it, and add the driving code.
- Feasible but the most involved path; combines Tier 2 + Tier 4 plus prefab work.

### Worked content mods (mid-tier real examples)
- **AnyMount** — F4 anywhere opens a selector listing every mount prefab loaded
  in the build, with a Player 1 / Player 2 target toggle and a "give coins to
  Player 2" button.  Mid-game switching goes through `Player.Ride(steed,
  replace: true, applyToCampaign: true)` — the game's own mount-swap entry
  point, so dismount, network sync, and campaign-save persistence all happen
  for free.  See [`examples/AnyMount`](../examples/AnyMount).
- **AnyTrees** — two-feature mod for the monarch's builder workflow:
  - *Mark any tree* for chopping, including the forest-edge/deep-forest trees
    the marker normally refuses.  Workers are unchanged: they still only chop
    trees the monarch has paid to mark.  Implemented as a `PayableTree.UpdateSelectableStatus`
    **prefix** (skip-original to win the race against vanilla's RPC-driven
    propagation) plus `PayableTree.CanSelect` and `PayableTree.CanPay`
    postfixes.  An in-mod `WorkableTree.IsMarked()` check leaves vanilla in
    charge of already-marked trees so the coin indicator hides correctly.
  - *Build towers between trees* inside the forest (vanilla locks towers to
    the cleared "buildable region").  Implemented by mutating
    `PayableUpgrade.onlyInBuildableRegion` per-instance, cached by Il2Cpp
    `Pointer` and restored on toggle.  A `PayableUpgrade.IsLocked` postfix is
    the belt-and-braces backup that clears the `InvalidRegion` lock reason if
    the field mutation raced an `Awake`.  Scoped to towers only —
    walls, farms, and workshops keep their vanilla region restriction so
    clearing space stays meaningful.
  - The two features are independent F1 → Mods toggles — **AnyTrees**
    (tree-marking) and **Guerilla Warfare** (towers-in-forest) — each backed
    by its own `MelonPreferences` entry so the F1 state is remembered across
    sessions.  A **Reset (all off)** button in the Mods column flips every
    registered toggle/choice off in one click.
    See [`examples/AnyTrees`](../examples/AnyTrees).
- **SpeedTweaks** — slider-style multiplier on `Director.ClockSpeedModifier`,
  with an "engine maximum" upper bound derived at level-load by sampling
  `Director.rampupCurve`.  A second toggle disables the *progressively
  shortening days* by postfixing `Director.GetWaveRampupMultiplier` to return
  1.0, locking every day to the beginning pace.  See
  [`examples/SpeedTweaks`](../examples/SpeedTweaks).
- **BalanceExtras** — Tier 1–2 bundle: income multiplier, starting
  coins / beggars / peasants / gems / boat parts, sail-in time, cave escape
  timer, season lock (Spring / Summer / Autumn / Winter) and no-red-moon
  toggle.  Each knob is a postfix on a stable Director getter or
  `OnLevelLoaded`, reading the active `LevelConfig` straight off the interop
  property `Director._currentLevelConfig` (see the note below on why that's a
  property, not a field).  All overrides default to "leave alone", so the mod is
  a no-op until you set one.  See [`examples/BalanceExtras`](../examples/BalanceExtras).
- **HudOverlay** — Tier 3 IMGUI overlay: day, phase, season, clock speed, and
  the next N upcoming `Director.events` (waves, portals, season starts).
  **F2** toggles.  All Director access is wrapped behind NRE-safe snapshot
  helpers so the overlay survives scene transitions and main-menu state.
  See [`examples/HudOverlay`](../examples/HudOverlay).
- **SpeedHotkeys** — Tier 3 input.  **F5** slower, **F6** reset to 1.0, **F7**
  faster, **F8** toggle freeze (remembers last non-zero).  Drives
  `Kingdom.Time.ClockSpeedMultiplier` directly with `Kingdom.IsReady` guards
  so main-menu keypresses are no-ops.  See
  [`examples/SpeedHotkeys`](../examples/SpeedHotkeys).
- **ChallengeDumper** (a.k.a. Game Data Dumper) — **F3** writes JSON snapshots
  of every loaded `ChallengeData`, `Steed`, `LevelConfig`, and `BiomeData` SO
  to `<MelonLoader>/UserData/KingdomMod/dump/`.  IL2CPP MonoBehaviour
  serialisation is not statically decodable by free AssetRipper, so this is
  the path to read the live, deserialised values used by the game.  Open the
  challenges menu first so the SOs are referenced into memory.
  See [`examples/ChallengeDumper`](../examples/ChallengeDumper).

---

## What you CANNOT (easily) do

- **Burst‑compiled hot paths** (`lib_burst_generated.dll`) are native machine code,
  not IL — **Harmony cannot patch them.** If a system was Burst‑compiled for
  performance, you can only influence it from its managed callers.
- **Multiplayer netcode.** Co‑op uses Steam/GOG/Xbox P2P. Mods that change game state
  will **desync** unmodded peers. Treat modding as single‑player/offline unless all
  players run identical mods, and even then netcode changes are out of scope.
- **Cloud saves / PlayFab.** Save logic that round‑trips to PlayFab servers can't be
  changed client‑side, and local edits may be rejected or overwritten. Back up first.
- **Original C# source.** IL2CPP is a one‑way compile. You get class/method
  *signatures* (great for patching) but never the authored method bodies.
- **Anti‑tamper.** None observed, but online achievements/leaderboards should be
  treated as off‑limits while modded.

---

## Practical notes for mod authors

- **Patch by name, not offset.** Game updates shift IL2CPP offsets; name/signature
  Harmony patches survive most updates. After an update, re‑run
  `tools/update-after-patch.ps1` to regenerate interop.
- **Resolve singletons safely.** Use the SDK accessors (they null‑check and cache).
- **Keep packs data‑only.** Ship JSON + your own art/audio; never bundle game files.
- **Surface runtime toggles via `Kingdom.Mods`.** The SDK exposes
  `Kingdom.Mods.RegisterToggle(label, get, set, tooltip)` (checkbox),
  `Kingdom.Mods.RegisterChoice(label, options[], getIdx, setIdx, tooltip)` (radio row),
  and `Kingdom.Mods.RegisterHotkey(key, description)` (a row in the console's
  **Shortcuts** guide — documentation only; your mod still handles the input).
  All render automatically in the F1 console — your mod owns the state, the
  registry just stores the callbacks. Idempotent on the label/key so hot reloads
  don't duplicate entries. The console has a **Reset** button (top-right) that
  drives every registered toggle/choice (and the loader cheats) back to vanilla.
  See [`AnyTrees`](../examples/AnyTrees) for a worked example.
- **Always pass a `tooltip` for your options.** `tooltip` is the optional last
  argument on both `RegisterToggle` and `RegisterChoice`; the console shows it in
  the hover-tooltip bar (the `ⓘ` line at the bottom of the F1 panel) when the
  player mouses over your control. Write one clear sentence on what the option
  does — and for a choice, what each side means (e.g. *"Lame = vanilla; Brave =
  mark any tree for chopping"*). It's optional only for source-compatibility;
  treat it as required. Omit it and the console falls back to the bare label,
  which tells the player nothing they can't already read off the checkbox.

  ```csharp
  Kingdom.Mods.RegisterToggle("Guerilla Warfare",
      () => GuerillaWarfare, v => GuerillaWarfare = v,
      "Build towers between the trees inside the forest, not just in the cleared region.");

  Kingdom.Mods.RegisterChoice("Builder cowardice",
      new[] { "Lame", "Brave" },
      () => Brave ? 1 : 0, idx => Brave = (idx == 1),
      "Lame = vanilla (only cleared trees can be marked). Brave = mark ANY tree.");
  ```
- **Defend against scene transitions and main-menu state.** Director getters
  can NRE while a scene is being torn down or rebuilt; `Kingdom.IsReady`
  gates the entry points, and try/catch around Il2Cpp field reads keeps
  exceptions from bubbling into IMGUI / per-frame Harmony postfixes. See
  the snapshot helpers in [`HudOverlay`](../examples/HudOverlay) and the
  guarded entry points in [`SpeedHotkeys`](../examples/SpeedHotkeys).
- **Private game fields are surfaced as *properties*, not fields.** Il2CppInterop
  exposes every IL2CPP instance field (including private ones) as a public
  property on the interop type — the only real managed *field* is the native
  pointer (e.g. `NativeFieldInfoPtr__currentLevelConfig`). So
  `AccessTools.Field(typeof(Director), "_currentLevelConfig")` returns **null**;
  access the generated property directly instead — `__instance._currentLevelConfig`
  — which is compile-time checked and can't silently break. See
  [`BalanceExtras`](../examples/BalanceExtras). (`AccessTools.Property` /
  `AccessTools.PropertyGetter` are the reflection equivalents if you need a
  cached accessor.)

*The concrete class/namespace names backing these capabilities are listed in
[`api-reference.md`](api-reference.md), generated from your local dump.*
