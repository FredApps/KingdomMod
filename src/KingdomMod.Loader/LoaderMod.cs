// KingdomMod.Loader — the MelonLoader entry mod.  Sits between MelonLoader and
// user mods: bootstraps the SDK, auto-backups saves on first run, exposes the
// in-game console (F1), and warns about cloud-save + multiplayer risks.

using System;
using System.IO;
using Il2Cpp;
using KingdomMod.Loader.Console;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(KingdomMod.Loader.LoaderMod), "KingdomMod.Loader", "0.1.0", "KingdomMod contributors")]
[assembly: MelonGame("noio", "KingdomTwoCrowns")]

namespace KingdomMod.Loader
{
    public sealed class LoaderMod : MelonMod
    {
        internal static LoaderMod Instance { get; private set; }
        private InGameConsole _console;
        private Console.CoinCounterOverlay _coinCounter;
        private Console.MultiplayerWarningPopup _mpWarning;
        private bool _mpWarningPending;
        private Console.BackupNoticePopup _backupNotice;
        private Console.FixAppliedPopup _fixPopup;
        private bool _backupNoticePending;
        private string _newBackupDir;
        private bool _backedUp;
        private MelonPreferences_Category _prefs;
        private MelonPreferences_Entry<bool> _consoleEnabled;
        private MelonPreferences_Entry<bool> _backupOnLaunch;
        private MelonPreferences_Entry<bool> _archiveLogs;
        private MelonPreferences_Entry<int>  _archiveLogKeep;
        private MelonPreferences_Entry<bool> _warnedMultiplayer;
        private MelonPreferences_Entry<string> _lastBackupSig;
        private MelonPreferences_Entry<string> _lastBackupDir;
        private MelonPreferences_Entry<int>  _defaultCoinCheat;
        private MelonPreferences_Entry<bool> _defaultInfStamina;
        private MelonPreferences_Entry<bool> _friendlyInvincibility;
        private MelonPreferences_Entry<bool> _crownPickupFix;
        private MelonPreferences_Entry<bool> _boarVanishFix;
        private MelonPreferences_Entry<int> _itemPowerP1;
        private MelonPreferences_Entry<int> _itemPowerP2;
        private MelonPreferences_Entry<int> _monarchChoiceP1;
        private MelonPreferences_Entry<int> _monarchChoiceP2;
        private MelonPreferences_Entry<int> _originalMonarchP1;
        private MelonPreferences_Entry<int> _originalMonarchP2;

        public override void OnInitializeMelon()
        {
            Instance = this;

            _prefs = MelonPreferences.CreateCategory("KingdomMod", "KingdomMod platform settings");
            _consoleEnabled    = _prefs.CreateEntry("ConsoleEnabled",    true, "Show the F1 in-game console.");
            _backupOnLaunch    = _prefs.CreateEntry("BackupSavesOnLaunch", true, "Copy save files to <repo>/build/save-backups/ before any modded session.");
            _archiveLogs       = _prefs.CreateEntry("ArchiveMelonLoaderLogs", true, "On each launch, copy MelonLoader\\Latest*.log into UserData\\KingdomMod\\logs\\ for later analysis.");
            _archiveLogKeep    = _prefs.CreateEntry("ArchiveMelonLoaderLogKeep", 30, "Maximum number of archived log files to keep. Oldest are deleted first.");
            _warnedMultiplayer = _prefs.CreateEntry("ShownMultiplayerWarning", false, "Has the user dismissed the multiplayer warning?");
            _lastBackupSig     = _prefs.CreateEntry("LastBackupSignature", "", "Newest mod-DLL timestamp at the last save backup. Saves are only re-backed-up when this changes (i.e. after a mod (re)install), not every launch.");
            _lastBackupDir     = _prefs.CreateEntry("LastBackupDir", "", "Folder the most recent save backup was written to (shown in the first-run notice).");
            _defaultCoinCheat  = _prefs.CreateEntry("DefaultCoinCheat", (int)CoinCheatMode.None,
                "Coin cheat. 0=None, 1=NoTax, 2=Infinite. Applied on launch and updated when you change it via F1, so the last-used state persists.");
            _defaultInfStamina = _prefs.CreateEntry("DefaultInfiniteStamina", false,
                "Infinite Stamina. Applied on launch and updated when you toggle it via F1, so the last-used state persists.");
            _friendlyInvincibility = _prefs.CreateEntry("FriendlyInvincibility", false,
                "Friendly Invincibility. Makes monarchs and friendly units invulnerable while enabled.");
            _crownPickupFix = _prefs.CreateEntry("CrownPickupFix", true,
                "Fixes dropped crowns that can get stuck/unpickable. Waits 10s, then only repairs if a crownless player is close.");
            _boarVanishFix = _prefs.CreateEntry("BoarVanishFix", true,
                "Fixes winter boars that disappear without dying. Preserves reward coins unless the boar returned to its nest.");
            _itemPowerP1 = _prefs.CreateEntry("ItemOfPowerP1", -1,
                "Persisted F1 item of power for Player 1. -1=not managed yet, 0=None, 1=Thor, 2=Hel, 3=Heimdal, 4=Loki.");
            _itemPowerP2 = _prefs.CreateEntry("ItemOfPowerP2", -1,
                "Persisted F1 item of power for Player 2. -1=not managed yet, 0=None, 1=Thor, 2=Hel, 3=Heimdal, 4=Loki.");
            _monarchChoiceP1 = _prefs.CreateEntry("MonarchChoiceP1", 0,
                "Persisted F1 monarch choice for Player 1. 0=Original, 1=Zangetsu, 2=Alfred, 3=Gebel, 4=Miriam.");
            _monarchChoiceP2 = _prefs.CreateEntry("MonarchChoiceP2", 0,
                "Persisted F1 monarch choice for Player 2. 0=Original, 1=Zangetsu, 2=Alfred, 3=Gebel, 4=Miriam.");
            _originalMonarchP1 = _prefs.CreateEntry("OriginalMonarchP1", -1,
                "Captured original non-Dead-Lands monarch model for Player 1.");
            _originalMonarchP2 = _prefs.CreateEntry("OriginalMonarchP2", -1,
                "Captured original non-Dead-Lands monarch model for Player 2.");

            // The console owns F1; register it so it heads the Shortcuts guide.
            Kingdom.Mods.RegisterHotkey("F1", "Toggle this KingdomMod console");

            if (_archiveLogs.Value) TryArchiveMelonLoaderLogs();

            // Apply the persisted cheat state on startup. The F1 toggles write
            // back into these prefs (PersistCoinCheat / PersistInfiniteStamina),
            // so a change made in-session is remembered for next launch.
            try { Kingdom.Economy.CoinCheat       = (CoinCheatMode)_defaultCoinCheat.Value; } catch { }
            try { Kingdom.Players.InfiniteStamina = _defaultInfStamina.Value; } catch { }

            // Apply the loader-side Harmony patches that enforce F1 cheats
            // when the game's own Debug* flags turn out to be vestigial.
            try
            {
                var h = HarmonyHelper.PatchAll(this);
                int count = 0;
                foreach (var m in h.GetPatchedMethods()) { count++; LoggerInstance.Msg($"  • Patched: {m.DeclaringType?.Name}.{m.Name}"); }
                LoggerInstance.Msg($"  • Loader Harmony patches applied: {count}");
            }
            catch (Exception e) { LoggerInstance.Warning($"Harmony PatchAll failed: {e.Message}"); }

            LoggerInstance.Msg("KingdomMod platform initialised.");
            LoggerInstance.Msg($"  • Loader version 0.1.0");
            LoggerInstance.Msg($"  • F1 toggles the in-game console (current: {(_consoleEnabled.Value ? "ON" : "OFF")})");
            LogDiscoveredPacks();

            // First run after a fresh install: surface the desync / cloud-save
            // caution as an in-game popup instead of just a log line. The pref is
            // only set once the player acknowledges the dialog (see
            // OnMultiplayerWarningAcknowledged), so quitting beforehand re-shows
            // it next launch. The popup itself is created lazily once a scene
            // (and thus IMGUI) is ready - see OnSceneWasInitialized.
            if (!_warnedMultiplayer.Value)
            {
                _mpWarningPending = true;
                LoggerInstance.Warning(
                    "Mods can DESYNC co-op partners and may interfere with cloud saves. " +
                    "Treat this session as single-player/offline unless every player runs the same mods.");
            }
        }

        // Called by the F1 console when the user changes a cheat in-session, so
        // the choice is remembered and re-applied on the next launch (the prefs
        // double as "last used = next startup default"). Idempotent + saves.
        internal void PersistCoinCheat(CoinCheatMode mode)
        {
            if (_defaultCoinCheat == null || _defaultCoinCheat.Value == (int)mode) return;
            _defaultCoinCheat.Value = (int)mode;
            MelonPreferences.Save();
        }

        internal void PersistInfiniteStamina(bool on)
        {
            if (_defaultInfStamina == null || _defaultInfStamina.Value == on) return;
            _defaultInfStamina.Value = on;
            MelonPreferences.Save();
        }

        internal bool FriendlyInvincibilityEnabled => _friendlyInvincibility != null && _friendlyInvincibility.Value;
        internal bool CrownPickupFixEnabled => _crownPickupFix == null || _crownPickupFix.Value;
        // Disabled for now: the boar-vanish repair isn't reliable yet. Forced off
        // regardless of the persisted pref until a proper fix lands; the F1 row
        // shows it as off. The pref + PersistBoarVanishFix are kept for when it does.
        internal bool BoarVanishFixEnabled => false;

        internal void PersistFriendlyInvincibility(bool on)
        {
            if (_friendlyInvincibility == null || _friendlyInvincibility.Value == on) return;
            _friendlyInvincibility.Value = on;
            MelonPreferences.Save();
        }

        internal void PersistCrownPickupFix(bool on)
        {
            if (_crownPickupFix == null || _crownPickupFix.Value == on) return;
            _crownPickupFix.Value = on;
            MelonPreferences.Save();
        }

        internal void PersistBoarVanishFix(bool on)
        {
            if (_boarVanishFix == null || _boarVanishFix.Value == on) return;
            _boarVanishFix.Value = on;
            MelonPreferences.Save();
        }

        internal ItemOfPower.ItemType GetPersistedItemPower(int playerId)
        {
            var entry = playerId == 1 ? _itemPowerP2 : _itemPowerP1;
            return entry == null ? ItemOfPower.ItemType.None : (ItemOfPower.ItemType)entry.Value;
        }

        internal bool HasPersistedItemPower(int playerId)
        {
            var entry = playerId == 1 ? _itemPowerP2 : _itemPowerP1;
            return entry != null && entry.Value >= 0;
        }

        internal void PersistItemPower(int playerId, ItemOfPower.ItemType item)
        {
            var entry = playerId == 1 ? _itemPowerP2 : _itemPowerP1;
            if (entry == null || entry.Value == (int)item) return;
            entry.Value = (int)item;
            MelonPreferences.Save();
        }

        internal int GetPersistedMonarchChoice(int playerId)
        {
            var entry = playerId == 1 ? _monarchChoiceP2 : _monarchChoiceP1;
            return entry?.Value ?? 0;
        }

        internal void PersistMonarchChoice(int playerId, int choice)
        {
            var entry = playerId == 1 ? _monarchChoiceP2 : _monarchChoiceP1;
            if (entry == null || entry.Value == choice) return;
            entry.Value = choice;
            MelonPreferences.Save();
        }

        internal MonarchType? GetOriginalMonarch(int playerId)
        {
            var entry = playerId == 1 ? _originalMonarchP2 : _originalMonarchP1;
            if (entry == null || entry.Value < 0) return null;
            return (MonarchType)entry.Value;
        }

        internal void RememberOriginalMonarch(int playerId, MonarchType model)
        {
            if (KingdomMod.Loader.Console.PowerSwitcher.IsDeadlands(model)) return;
            var entry = playerId == 1 ? _originalMonarchP2 : _originalMonarchP1;
            if (entry == null || entry.Value >= 0) return;
            entry.Value = (int)model;
            MelonPreferences.Save();
        }

        internal void LogToConsole(string line)
        {
            try { _console?.Log(line); } catch { }
        }

        internal void ReportFixApplied(string title, string message)
        {
            string line = string.IsNullOrWhiteSpace(title) ? message : $"{title}: {message}";
            try { LoggerInstance.Msg("[Fix] " + line); } catch { }
            try { _console?.Log(line); } catch { }
            try { _fixPopup?.Show(title, message); } catch { }
        }

        private void LogDiscoveredPacks()
        {
            var packs = Kingdom.Packs.DiscoverPacks(MelonEnvironment.ModsDirectory);
            if (packs.Count == 0)
            {
                LoggerInstance.Msg("  • No data packs discovered under Mods/*/pack");
                return;
            }

            LoggerInstance.Msg($"  • Data packs: {packs.Count}");
            foreach (var pack in packs)
            {
                var featureList = new System.Collections.Generic.List<string>();
                if (pack.HasBalance) featureList.Add("balance");
                if (pack.HasSprites) featureList.Add("sprites");
                if (pack.HasAudio) featureList.Add("audio");
                var features = string.Join(", ", featureList);
                if (string.IsNullOrWhiteSpace(features))
                    features = "manifest";

                LoggerInstance.Msg($"    - {pack.Name} {pack.Version} ({features})");
            }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            // The Managers singleton becomes available after the gameplay scene loads.
            // Resetting our cached references means events are re-bound for the new scene.
            KingdomMod.Internal.GameRefs.Invalidate();

            if (!_backedUp && _backupOnLaunch.Value)
            {
                _backedUp = true;
                bool freshBackup = MaybeBackupSaves();
                // A backup only happens once per mod (re)install. When it does and
                // we're NOT already showing the first-run desync popup (which lists
                // the path itself), surface a one-time "save backed up here" notice.
                if (freshBackup && !_mpWarningPending)
                {
                    _backupNoticePending = true;
                    _newBackupDir = _lastBackupDir.Value;
                }
            }

            if (_console == null)
            {
                _console = new InGameConsole();
                // Open the console on game start so players see the controls and
                // the F-key shortcuts guide right away. They can hide it with F1.
                if (_consoleEnabled.Value) _console.Toggle();
            }
            if (_coinCounter == null)
                _coinCounter = new Console.CoinCounterOverlay();
            if (_mpWarning == null)
                _mpWarning = new Console.MultiplayerWarningPopup(OnMultiplayerWarningAcknowledged);
            if (_backupNotice == null)
                _backupNotice = new Console.BackupNoticePopup();
            if (_fixPopup == null)
                _fixPopup = new Console.FixAppliedPopup();

            // Surface the first-run caution now that IMGUI is live.
            if (_mpWarningPending)
            {
                _mpWarningPending = false;
                _mpWarning.Show(_lastBackupDir.Value);
            }
            // Otherwise, if a mod update triggered a fresh backup this launch,
            // tell the player where it went (once per install).
            else if (_backupNoticePending)
            {
                _backupNoticePending = false;
                _backupNotice.Show(_newBackupDir);
            }
        }

        // The player dismissed the first-run multiplayer/cloud-save popup; mark
        // it shown so it doesn't appear again.
        private void OnMultiplayerWarningAcknowledged()
        {
            _warnedMultiplayer.Value = true;
            MelonPreferences.Save();
        }

        public override void OnUpdate()
        {
            if (_consoleEnabled.Value && UnityEngine.Input.GetKeyDown(KeyCode.F1)) _console?.Toggle();
            bool popupVisible = IsModalPopupVisible();
            if (_consoleEnabled.Value) _console?.OnUpdate(!popupVisible);
            KingdomMod.Loader.Patches.BoarVanishFixPatch.Tick();
            KingdomMod.Loader.Patches.FriendlyInvincibility.Tick();
            KingdomMod.Loader.Console.PowerSwitcher.ApplyPersistedPowers();

            // Hide the coin pouch while any coin cheat is active so the
            // visual matches the cheat — the CoinCounterOverlay draws the
            // replacement (number for NoTax, ∞ for Infinite). DebugHideCurrencyBag
            // is the game's own static toggle for this; if it turns out to be
            // vestigial too, we'll need to call HideImmediate per frame instead.
            try
            {
                Il2Cpp.CurrencyBag.DebugHideCurrencyBag =
                    (Kingdom.Economy.CoinCheat != CoinCheatMode.None);
            }
            catch { }
        }

        public override void OnGUI()
        {
            bool popupVisible = IsModalPopupVisible();
            if (popupVisible)
            {
                _coinCounter?.OnGUI();
                _mpWarning?.OnGUI();
                _backupNotice?.OnGUI();
                return;
            }

            if (_consoleEnabled.Value) _console?.OnGUI();
            _coinCounter?.OnGUI();
            // Drawn last so the popups sit on top of everything.
            _mpWarning?.OnGUI();
            _backupNotice?.OnGUI();
            _fixPopup?.OnGUI();
        }

        private bool IsModalPopupVisible()
        {
            return (_mpWarning != null && _mpWarning.Visible)
                   || (_backupNotice != null && _backupNotice.Visible);
        }

        // ---- MelonLoader log archive -------------------------------------------
        // MelonLoader rolls Latest.log → Latest_N.log on startup and overwrites
        // Latest.log for the new session. By the time we run, the rolled files
        // are sitting in MelonLoader\ — we copy any we haven't seen before into
        // UserData\KingdomMod\logs\ so they survive future log rotation and
        // sit alongside the rest of KingdomMod's diagnostic data. The current
        // Latest.log is also re-copied (overwriting) on each call so a crash
        // dump can still be retrieved when the process didn't shut down cleanly.

        private void TryArchiveMelonLoaderLogs()
        {
            try
            {
                var src = Path.Combine(MelonEnvironment.MelonBaseDirectory, "MelonLoader");
                if (!Directory.Exists(src)) return;

                var dest = Path.Combine(MelonEnvironment.UserDataDirectory, "KingdomMod", "logs");
                Directory.CreateDirectory(dest);

                int copied = 0;
                foreach (var file in Directory.EnumerateFiles(src, "Latest*.log", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file);
                    var stamp = File.GetLastWriteTime(file).ToString("yyyyMMdd-HHmmss");
                    // Naming: <yyyyMMdd-HHmmss>-<original-name>. The timestamp
                    // is the source file's mtime so we don't re-copy unchanged
                    // rolled logs across sessions.
                    var target = Path.Combine(dest, $"{stamp}-{name}");
                    if (File.Exists(target)) continue;
                    File.Copy(file, target, overwrite: false);
                    copied++;
                }

                PruneOldLogs(dest, _archiveLogKeep.Value);

                if (copied > 0)
                    LoggerInstance.Msg($"  • Archived {copied} MelonLoader log file(s) to {dest}");
            }
            catch (Exception e)
            {
                LoggerInstance.Warning($"Log archive failed: {e.Message}");
            }
        }

        private static void PruneOldLogs(string dir, int keep)
        {
            if (keep <= 0) return;
            var files = new DirectoryInfo(dir).GetFiles("*.log");
            if (files.Length <= keep) return;
            Array.Sort(files, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
            for (int i = keep; i < files.Length; i++)
            {
                try { files[i].Delete(); } catch { }
            }
        }

        // ---- Save backup --------------------------------------------------------

        // Back up the saves only once per mod (re)install rather than on every
        // launch, so the backup folder doesn't accumulate an identical copy each
        // time the game starts. The "install" is identified by the newest mod-DLL
        // write time in <KTC>\Mods — re-running install-mods.ps1 rewrites those
        // DLLs, bumping the signature and triggering exactly one fresh backup.
        // Returns true if a fresh backup was actually taken this launch (i.e. the
        // mod set changed since the last backup), false if it was skipped.
        private bool MaybeBackupSaves()
        {
            string sig = ComputeModsSignature();
            if (!string.IsNullOrEmpty(sig) && sig == _lastBackupSig.Value
                && !string.IsNullOrEmpty(_lastBackupDir.Value) && Directory.Exists(_lastBackupDir.Value))
            {
                LoggerInstance.Msg($"  • Save backup skipped — already backed up for this mod install ({_lastBackupDir.Value}).");
                return false;
            }

            var dest = TryBackupSaves();
            if (dest != null)
            {
                _lastBackupSig.Value = sig;
                _lastBackupDir.Value = dest;
                MelonPreferences.Save();
                return true;
            }
            return false;
        }

        // Signature of the current mod set: the newest DLL last-write time in the
        // Mods directory (ticks). Empty string if it can't be read, which forces
        // a conservative backup rather than silently skipping.
        private static string ComputeModsSignature()
        {
            try
            {
                var dir = MelonEnvironment.ModsDirectory;
                if (!Directory.Exists(dir)) return "";
                long latest = 0;
                foreach (var f in Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    long t = File.GetLastWriteTimeUtc(f).Ticks;
                    if (t > latest) latest = t;
                }
                return latest.ToString();
            }
            catch { return ""; }
        }

        // Returns the destination folder on success, or null if nothing was
        // backed up (no save folder) or the copy failed.
        private string TryBackupSaves()
        {
            try
            {
                var src = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData", "LocalLow", "noio", "KingdomTwoCrowns");
                if (!Directory.Exists(src)) return null;

                var dest = Path.GetFullPath(Path.Combine(
                    MelonEnvironment.ModsDirectory, "..", "UserData", "KingdomMod-SaveBackups",
                    DateTime.Now.ToString("yyyyMMdd-HHmmss")));
                Directory.CreateDirectory(dest);

                foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(src, f);
                    var to = Path.Combine(dest, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Copy(f, to, overwrite: true);
                }
                LoggerInstance.Msg($"Saves backed up to {dest}");
                return dest;
            }
            catch (Exception e)
            {
                LoggerInstance.Warning($"Save backup failed: {e.Message}");
                return null;
            }
        }
    }
}
