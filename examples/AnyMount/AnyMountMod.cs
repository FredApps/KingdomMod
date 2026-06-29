// AnyMount — on-demand mount swap for either player.
//
//   * F4 toggles the picker window at any time during a run.
//   * The KingdomMod loader's F1 console hosts the same picker, so this mod
//     is now strictly a hotkey-accessible companion to the loader UI.
//
// The old run-start popup (and its SteedSpawn Harmony prefix) is gone — it
// fired on every save load and was a paper cut. Mid-game swap goes through
// Player.Ride(steed, replace: true), the game's own mount-swap entry point.

using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using KingdomMod;
using Il2Cpp;

[assembly: MelonInfo(typeof(KingdomMod.Examples.AnyMount.AnyMountMod), "Any Mount", "0.3.0", "KingdomMod contributors")]
[assembly: MelonGame("noio", "KingdomTwoCrowns")]

namespace KingdomMod.Examples.AnyMount
{
    public sealed class AnyMountMod : MelonMod
    {
        private static MelonPreferences_Entry<int> _coinGift;

        private bool _selecting;
        private int  _targetPlayerId;
        private Vector2 _scroll;
        private Rect _window = new Rect(60, 60, 500, 520);
        private readonly List<Steed> _options = new();
        private float _previousTimeScale = 1f;

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("KingdomMod.AnyMount", "Any Mount");
            _coinGift = cat.CreateEntry("CoinGiftAmount", 25,
                "Coins handed out by the 'Give coins to Player 2' button in the selector.");
            Kingdom.Mods.RegisterHotkey("F4", "Open the per-player mount selector (also in F1 -> Mount)");
            LoggerInstance.Msg("AnyMount ready — press F4 to swap a player's mount, or use F1 → Mount.");
        }

        public override void OnUpdate()
        {
            if (_selecting)
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
                    Dismiss();
                return;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.F4))
                Open();
        }

        public override void OnGUI()
        {
            if (!_selecting) return;
            _window = GUILayout.Window(0x4D0507, _window, (GUI.WindowFunction)DrawWindow,
                "Switch mount  (Esc to cancel)");
        }

        private void Open()
        {
            BuildOptions();
            if (_options.Count == 0)
            {
                LoggerInstance.Warning("AnyMount: no steed prefabs available — selector skipped.");
                return;
            }

            _targetPlayerId = 0;
            _selecting = true;
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            LoggerInstance.Msg($"AnyMount: showing {_options.Count} options.");
        }

        private void BuildOptions()
        {
            _options.Clear();
            var seen = new HashSet<string>();
            foreach (var steed in Resources.FindObjectsOfTypeAll<Steed>())
            {
                if (steed == null) continue;
                // Only prefabs — scene instances are the live in-world mounts
                // and passing one to Player.Ride removes the player+mount.
                if (steed.gameObject.scene.handle != 0) continue;
                TryAddOption(steed, seen);
            }

            _options.Sort((a, b) =>
            {
                int byType = a.steedType.CompareTo(b.steedType);
                return byType != 0 ? byType : string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
            });
        }

        private void TryAddOption(Steed steed, HashSet<string> seen)
        {
            if (steed == null) return;
            if (steed.steedType == SteedType.INVALID
                || steed.steedType == SteedType.Trap
                || steed.steedType == SteedType.Barrier) return;
            var key = steed.steedType + "|" + steed.name;
            if (!seen.Add(key)) return;
            _options.Add(steed);
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label("Pick the player slot, then a mount.");
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Target:", GUILayout.Width(60));
            if (GUILayout.Toggle(_targetPlayerId == 0, "Player 1", "Button")) _targetPlayerId = 0;
            if (GUILayout.Toggle(_targetPlayerId == 1, "Player 2", "Button")) _targetPlayerId = 1;
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            if (GUILayout.Button($"Give {_coinGift.Value} coins to Player 2", GUILayout.Height(24)))
                GiveCoinsToPlayer(playerId: 1, amount: _coinGift.Value);

            GUILayout.Space(6);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(380));
            foreach (var steed in _options)
            {
                if (steed == null) continue;
                if (GUILayout.Button($"{steed.steedType}   ({steed.name})", GUILayout.Height(28)))
                {
                    ApplyChoice(steed);
                    break;
                }
            }
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        private void ApplyChoice(Steed steedPrefab)
        {
            var target = FindPlayer(_targetPlayerId);
            if (target == null)
            {
                LoggerInstance.Warning($"AnyMount: Player {_targetPlayerId + 1} not in scene.");
                Dismiss();
                return;
            }

            // Player.Ride needs an instantiated Steed, not a prefab — see the
            // matching note in InGameConsole.RidePlayer. Spawn at the player.
            Steed instance;
            try
            {
                instance = UnityEngine.Object.Instantiate(steedPrefab);
            }
            catch (System.Exception e)
            {
                LoggerInstance.Warning($"AnyMount: Instantiate({steedPrefab.steedType}) failed: {e.Message}");
                Dismiss();
                return;
            }
            instance.name = steedPrefab.name;
            instance.transform.position = target.transform.position;
            instance.gameObject.SetActive(true);

            try
            {
                target.Ride(instance, replace: true, applyToCampaign: true);
                LoggerInstance.Msg($"AnyMount: Player {_targetPlayerId + 1} now riding {steedPrefab.steedType}.");
            }
            catch (System.Exception e)
            {
                LoggerInstance.Warning($"AnyMount: Ride failed: {e.Message}");
                UnityEngine.Object.Destroy(instance.gameObject);
            }
            Dismiss();
        }

        private void GiveCoinsToPlayer(int playerId, int amount)
        {
            if (amount <= 0) return;
            var p = FindPlayer(playerId);
            if (p == null || p.wallet == null)
            {
                LoggerInstance.Warning($"AnyMount: Player {playerId + 1} (or wallet) not in scene.");
                return;
            }
            p.wallet.Coins = System.Math.Max(0, p.wallet.Coins + amount);
            LoggerInstance.Msg($"AnyMount: +{amount} coins to Player {playerId + 1} (now {p.wallet.Coins}).");
        }

        private static Player FindPlayer(int playerId)
        {
            foreach (var p in Kingdom.Players.All)
                if (p != null && p.playerId == playerId) return p;
            return null;
        }

        private void Dismiss()
        {
            _selecting = false;
            Time.timeScale = _previousTimeScale;
        }
    }
}
