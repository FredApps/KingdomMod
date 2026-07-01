# Custom Challenges And Islands

KingdomMod can load custom challenge recipes from JSON and apply them in F1.
This is not a full terrain painter yet. The first supported path is safer:
start from an existing `ChallengeData` and one or more existing `LevelConfig`
island templates, then override well-understood fields such as starting coins,
gems, beggars, cave timer, island width, cliffs, rivers, and season timing.

The runtime clones the game's loaded challenge and island assets and applies
your JSON values to those clones. That keeps challenge launch, save handling,
biome selection, and most special rules on the game's own code path.

## Workflow

1. Launch the game once with the **Game Data Dumper** example enabled.
2. Open the challenge menu or enter a run so the game loads challenge assets.
3. Press **F3** to dump runtime data.
4. Start the local designer:

   ```powershell
   tools\asset-designer.ps1
   ```

5. Open the **Challenges & Islands** tab.
6. Choose a base challenge and base island config from the dumped templates.
7. Adjust the island fields.
8. Click **Export To F1**.
9. In game, open **F1 -> Challenges**, click **Refresh**, then select your
   custom challenge.
10. Start a challenge through the game's normal challenge menu.

The designer writes JSON to:

```text
<KTC>/UserData/KingdomMod/custom-challenges/
```

The F1 selector reads that folder. It also creates
`sample.custom-challenge.json` if the folder is empty, so you always have a
starter file to edit.

## JSON Shape

```json
{
  "schema": 1,
  "id": "custom.my-island",
  "name": "My Custom Island",
  "description": "Short text shown in the F1 tooltip.",
  "baseChallenge": "Daily Challenge Island(Clone)",
  "baseChallengeId": 9,
  "baseChallengeType": "DailyChallengeIsland",
  "baseLevelConfig": "Daily_Challenge_OakAndBirch_LandConfig(Clone)",
  "challengeSeed": 12345,
  "isMultiplayer": true,
  "includeHermits": true,
  "zombieMode": false,
  "forceSelectBiomeIndex": -1,
  "startingCurrencyBagType": "Bag",
  "islands": [
    {
      "name": "Island 1",
      "baseLevelConfig": "Daily_Challenge_OakAndBirch_LandConfig(Clone)",
      "startingCoins": 10,
      "startingBeggars": 2,
      "startingPeasants": 0,
      "startingGems": 2,
      "freeBoatParts": 20,
      "incomeMultiplier": 1.25,
      "caveEscapeTimer": 25,
      "minLevelWidth": 560,
      "gemCount": 4,
      "seasonChangeDays": 3,
      "twoCliffs": true,
      "caveless": false,
      "riverless": false,
      "randomizeCliffSide": false,
      "sideDistributionBias": 5
    }
  ]
}
```

## Supported Fields

Challenge-level fields:

| Field | Meaning |
|---|---|
| `baseChallenge` / `baseChallengeId` / `baseChallengeType` | The loaded vanilla challenge to clone. |
| `challengeSeed` | Seed passed through to the cloned challenge. |
| `isMultiplayer` | Whether the cloned challenge is treated as multiplayer-capable. |
| `includeHermits` | Whether hermits are included in the challenge setup. |
| `zombieMode` | Reuses the game's zombie-mode challenge flag. |
| `forceSelectBiomeIndex` | `-1` leaves the base behavior alone; otherwise asks the challenge to force a biome index. |
| `startingCurrencyBagType` | Usually `Bag`; other known values are `Hermes` and `EggBasket`. |

Island-level fields:

| Field | Meaning |
|---|---|
| `baseLevelConfig` | The existing island/level config to clone. |
| `startingCoins`, `startingBeggars`, `startingPeasants`, `startingGems` | Starting resources and population. |
| `startingCoinsContinueOverride` | Coin override after continuing, `-1` for vanilla behavior. |
| `incomeMultiplier` | Chest and merchant income multiplier. |
| `freeBoatParts` | Boat parts already supplied. |
| `caveEscapeTimer` | Cave escape timer in seconds. |
| `minLevelWidth` | Minimum generated island width. |
| `gemCount` | Gems available on the island. |
| `seasonChangeDays` | Days per season transition. |
| `twoCliffs`, `caveless`, `riverless`, `randomizeCliffSide` | Major layout flags copied from `LevelConfig`. |
| `sideDistributionBias` | Left/right placement bias, 1 left through 9 right. |

## Limits

- Do not redistribute F3 dumps or extracted game assets. They are local
  references from your own game install.
- The selector applies a challenge override; it does not currently create a
  brand-new vanilla challenge menu card with custom art and localization.
- Arbitrary block-by-block island painting is not implemented yet. Use existing
  `LevelConfig` templates and the supported fields above.
- Challenge-special behavior still comes from the base challenge. If you clone
  Dire Island, for example, you inherit Dire Island's special challenge code
  unless you explicitly change supported flags.
