// EconomyApi — wallets, currency, taxes.

using System;
using System.Collections.Generic;
using KingdomMod.Internal;

namespace KingdomMod
{
    /// <summary>Tri-state coin cheat. Exactly one mode is active at a time.</summary>
    public enum CoinCheatMode
    {
        /// <summary>Vanilla wallet behavior. Bag visible, taxes paid, coins spent normally.</summary>
        None = 0,
        /// <summary>Player wallet stops paying taxes / shedding overflow coins. Bag hidden, coin counter shown above monarch.</summary>
        NoTax = 1,
        /// <summary>Player wallet never decreases. Bag hidden, ∞ shown above monarch.</summary>
        Infinite = 2,
    }

    /// <summary>Currency, wallets, taxes, and built-in money cheats.</summary>
    public sealed class EconomyApi
    {
        internal static EconomyApi Instance { get; } = new EconomyApi();
        private EconomyApi() { }

        // ---- Coin cheat mode ------------------------------------------------
        // Single source of truth. Setting CoinCheat flips the two underlying
        // static Wallet flags atomically and mutually exclusively. The loader's
        // F1 console binds to this; the cheat-enforcement patches still read
        // Wallet.DebugDisableTaxes / Wallet.InfiniteMoney directly because
        // those are the cross-mod observable contract.

        /// <summary>Current coin cheat mode. Setter mutates Wallet.DebugDisableTaxes / Wallet.InfiniteMoney to match.</summary>
        public CoinCheatMode CoinCheat
        {
            get
            {
                if (Il2Cpp.Wallet.InfiniteMoney) return CoinCheatMode.Infinite;
                if (Il2Cpp.Wallet.DebugDisableTaxes) return CoinCheatMode.NoTax;
                return CoinCheatMode.None;
            }
            set
            {
                Il2Cpp.Wallet.InfiniteMoney      = (value == CoinCheatMode.Infinite);
                Il2Cpp.Wallet.DebugDisableTaxes  = (value == CoinCheatMode.NoTax);
            }
        }

        /// <summary>True when Infinite mode is active.</summary>
        public bool InfiniteMoney => Il2Cpp.Wallet.InfiniteMoney;

        /// <summary>True when NoTax mode is active.</summary>
        public bool DisableTaxes => Il2Cpp.Wallet.DebugDisableTaxes;

        // ---- Wallet operations ----------------------------------------------

        /// <summary>All wallets currently in the scene (one per active player AND every NPC).
        /// Walks the scene each call — expensive. Use <see cref="LocalWallet"/> for per-frame reads.</summary>
        public IEnumerable<Il2Cpp.Wallet> Wallets
        {
            get
            {
                foreach (var w in UnityEngine.Object.FindObjectsByType<Il2Cpp.Wallet>(
                             UnityEngine.FindObjectsSortMode.None))
                    yield return w;
            }
        }

        // ---- LocalWallet — cached, player-scoped, per-frame safe ------------
        // FindObjectsByType is expensive on IL2CPP (scans every GameObject),
        // and the scene also contains NPC wallets (Squires, Merchants...) —
        // grabbing the *first* hit drifted into an NPC's stash, so the overlay
        // appeared frozen while the monarch's actual coin count changed.
        // Cache the resolved player wallet; only re-scan when it's destroyed
        // (Unity-null) or hasn't been set yet (scene just loaded).

        private Il2Cpp.Wallet _cachedLocalWallet;

        /// <summary>The local monarch's wallet, or null if no Player is in the scene yet.
        /// Cached — the underlying scene scan only re-runs when the cached wallet is destroyed.</summary>
        public Il2Cpp.Wallet LocalWallet
        {
            get
            {
                // Unity's overloaded == checks the C++-side destroyed flag.
                if (_cachedLocalWallet != null) return _cachedLocalWallet;
                _cachedLocalWallet = ResolvePlayerWallet();
                return _cachedLocalWallet;
            }
        }

        /// <summary>Invalidate the cached <see cref="LocalWallet"/> reference (e.g. after a scene reload).</summary>
        public void InvalidateWalletCache() => _cachedLocalWallet = null;

        private static Il2Cpp.Wallet ResolvePlayerWallet()
        {
            foreach (var p in UnityEngine.Object.FindObjectsByType<Il2Cpp.Player>(
                         UnityEngine.FindObjectsSortMode.None))
            {
                if (p != null && p.wallet != null) return p.wallet;
            }
            return null;
        }

        /// <summary>Coins in the local wallet (read or set).</summary>
        public int Coins
        {
            get => LocalWallet?.Coins ?? 0;
            set { var w = LocalWallet; if (w != null) w.Coins = value; }
        }

        /// <summary>Gems in the local wallet (read or set).</summary>
        public int Gems
        {
            get => LocalWallet?.Gems ?? 0;
            set { var w = LocalWallet; if (w != null) w.Gems = value; }
        }

        /// <summary>Add (or subtract, if negative) coins to the local wallet.</summary>
        public void GiveCoins(int amount)
        {
            var w = LocalWallet;
            if (w != null) w.Coins = Math.Max(0, w.Coins + amount);
        }

        /// <summary>Add (or subtract) gems.</summary>
        public void GiveGems(int amount)
        {
            var w = LocalWallet;
            if (w != null) w.Gems = Math.Max(0, w.Gems + amount);
        }
    }
}
