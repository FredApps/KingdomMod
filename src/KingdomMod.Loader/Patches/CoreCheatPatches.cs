// CoreCheatPatches — Harmony patches that *enforce* the cheats the F1 console
// exposes. Some of the game's own "Debug*" flags appear vestigial on this
// build (the field exists but no game code reads it). We read them ourselves
// in a Prefix and short out the relevant code path.
//
// "No taxes" — the monarch shedding coins while walking with an over-capacity
// wallet is the CurrencyBag drip path, not Wallet.PayTaxes. The flow is:
//
//   CurrencyBag.DripCurrency (coroutine)  spawns visual BagCurrency objects
//     → BagCurrency falls under gravity, hits a trigger
//     → BagCurrency.OnTriggerEnter2D → CurrencyBag.CurrencyFell(BagCurrency)
//     → Player.CoinFellFromBag(CurrencyType)  — gameplay-side drop
//
// `Wallet.TryToPayTaxes(Collider2D)` / `Wallet.PayTaxes()` are the NPC tax
// delivery path — workers/farmers cross the monarch's wallet trigger to pay
// in. Patching them was wrong; it just blocked income.
//
// Patches:
//   * CurrencyBag.DripCurrency  — cosmetic: stops new BagCurrency from
//     spawning, so no visual drip.
//   * Player.CoinFellFromBag    — gameplay: belt-and-braces in case any
//     already-airborne BagCurrency lands while DripCurrency was running
//     before the toggle flipped on.
//   * Wallet.RemoveCurrency     — "Infinite money" enforcement. The static
//     Wallet.InfiniteMoney flag also turned out to be vestigial on this
//     build; we scope to player wallets so NPC stashes (Squires, Merchants)
//     still spend their own coins/items normally.

using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace KingdomMod.Loader.Patches
{
    /// <summary>
    /// Helper: is this Wallet owned by a player (monarch), or is it some
    /// NPC's stash? Walks the transform parent chain looking for a Player
    /// component. Cached per-pointer for 2 s so hot paths like RemoveCurrency
    /// don't pay the lookup cost every call.
    /// </summary>
    internal static class WalletScope
    {
        private static readonly Dictionary<System.IntPtr, (bool isPlayer, float expiry)> _cache = new();
        private const float CacheLifetimeSeconds = 2f;

        public static bool IsPlayerWallet(Wallet w)
        {
            if (w == null) return false;
            var p = w.Pointer;
            if (p == System.IntPtr.Zero) return false;

            float now = Time.unscaledTime;
            if (_cache.TryGetValue(p, out var entry) && now < entry.expiry)
                return entry.isPlayer;

            bool isPlayer = false;
            try { isPlayer = w.GetComponentInParent<Player>() != null; }
            catch { }
            _cache[p] = (isPlayer, now + CacheLifetimeSeconds);
            return isPlayer;
        }

        public static bool IsInfiniteProtectedCurrency(CurrencyType type)
        {
            // Crown is a gameplay/loss state, not a spendable item counter.
            return type != CurrencyType.Crown;
        }
    }

    /// <summary>
    /// Cosmetic pinch point. The coroutine wrapper is replaced with an empty
    /// enumerator while DebugDisableTaxes is on, so the bag never spawns the
    /// falling BagCurrency sprites in the first place. Only the player has a
    /// CurrencyBag, so no scoping needed.
    /// </summary>
    /// <summary>
    /// The DripCurrency wrapper returns an Il2CppInterop-wrapped enumerator
    /// whose type we can't easily construct. Instead we patch the underlying
    /// state machine's MoveNext directly: returning false on first tick makes
    /// the coroutine end before spawning any BagCurrency, so the visual drip
    /// is suppressed entirely.
    /// </summary>
    [HarmonyPatch]
    internal static class CurrencyBagDripCurrencyPatch
    {
        private static bool _logged;

        // Resolves the compiler-generated coroutine state machine at runtime.
        // Il2CppInterop renames `<DripCurrency>d__68` (raw metadata) to a
        // valid C# identifier by stripping the angle brackets — we have to
        // search by sanitized name. Inner class lookup via reflection so we
        // don't take a hard compile-time dependency on the mangled name.
        private static MethodBase TargetMethod()
        {
            foreach (var t in typeof(CurrencyBag).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (t.Name.Contains("DripCurrency") && t.Name.Contains("d__"))
                    return AccessTools.Method(t, "MoveNext");
            }
            return null;
        }

        private static bool Prefix(ref bool __result)
        {
            if (!Wallet.DebugDisableTaxes) return true;
            __result = false;
            if (!_logged) { _logged = true; MelonLogger.Msg("[KingdomMod.Loader] No-taxes suppressed CurrencyBag.DripCurrency."); }
            return false;
        }
    }

    /// <summary>
    /// Gameplay pinch point. Player-only by definition. Returning false
    /// consumes the BagCurrency-fell event without removing currency from the
    /// wallet or spawning a ground coin — covers BagCurrency that was already
    /// airborne when the toggle flipped on.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.CoinFellFromBag))]
    internal static class PlayerCoinFellFromBagPatch
    {
        private static bool _logged;

        private static bool Prefix()
        {
            if (!Wallet.DebugDisableTaxes) return true;
            if (!_logged) { _logged = true; MelonLogger.Msg("[KingdomMod.Loader] No-taxes blocked Player.CoinFellFromBag."); }
            return false;
        }
    }

    /// <summary>
    /// "Infinite money" enforcement. Wallet.InfiniteMoney is a static bool the
    /// game declares but never reads on this build (same vestigial pattern as
    /// DebugDisableTaxes). We read it ourselves: when on, skip the player
    /// wallet's debit path. NPC wallets (Squires, Merchants paying their own
    /// stash) fall through to the original implementation so their economies
    /// keep functioning. This applies to every wallet-backed CurrencyType, not
    /// only coins; reward/increase paths still run through SetCurrency/FastSet.
    /// </summary>
    [HarmonyPatch(typeof(Wallet), nameof(Wallet.RemoveCurrency))]
    internal static class WalletRemoveCurrencyPatch
    {
        private static bool _logged;
        private static bool Prefix(Wallet __instance, CurrencyType currencyType)
        {
            if (!Wallet.InfiniteMoney) return true;
            if (!WalletScope.IsInfiniteProtectedCurrency(currencyType)) return true;
            if (!WalletScope.IsPlayerWallet(__instance)) return true;
            if (!_logged) { _logged = true; MelonLogger.Msg("[KingdomMod.Loader] Infinite-money blocked Wallet.RemoveCurrency."); }
            return false;
        }
    }

    /// <summary>
    /// SetCurrency assigns an absolute value — block only when it's a debit
    /// (new value &lt; current) so increases (rewards, save load) still apply.
    /// </summary>
    [HarmonyPatch(typeof(Wallet), nameof(Wallet.SetCurrency))]
    internal static class WalletSetCurrencyPatch
    {
        private static bool _logged;
        private static bool Prefix(Wallet __instance, CurrencyType currencyType, int value)
        {
            if (!Wallet.InfiniteMoney) return true;
            if (!WalletScope.IsInfiniteProtectedCurrency(currencyType)) return true;
            if (!WalletScope.IsPlayerWallet(__instance)) return true;
            if (value >= __instance.GetCurrency(currencyType)) return true;
            if (!_logged) { _logged = true; MelonLogger.Msg("[KingdomMod.Loader] Infinite-money blocked Wallet.SetCurrency debit."); }
            return false;
        }
    }

    /// <summary>
    /// FastSetCurrency takes an INCREMENT (positive = add, negative = debit).
    /// Block only negative increments.
    /// </summary>
    [HarmonyPatch(typeof(Wallet), nameof(Wallet.FastSetCurrency))]
    internal static class WalletFastSetCurrencyPatch
    {
        private static bool _logged;
        private static bool Prefix(Wallet __instance, CurrencyType currencyType, int incCurrency)
        {
            if (!Wallet.InfiniteMoney) return true;
            if (!WalletScope.IsInfiniteProtectedCurrency(currencyType)) return true;
            if (incCurrency >= 0) return true;
            if (!WalletScope.IsPlayerWallet(__instance)) return true;
            if (!_logged) { _logged = true; MelonLogger.Msg("[KingdomMod.Loader] Infinite-money blocked Wallet.FastSetCurrency debit."); }
            return false;
        }
    }
}
