using System;
using Il2Cpp;

namespace KingdomMod.Loader.Console
{
    internal static class PowerSwitcher
    {
        private static readonly MonarchType[] Deadlands =
        {
            MonarchType.Zangetsu,
            MonarchType.Alfred,
            MonarchType.Gebel,
            MonarchType.Miriam
        };

        // Items of power are biome-specific: the Norse Lands set (Thor/Hel/
        // Heimdal/Loki) only loads in the Norse Lands campaign, and the Olympus
        // set (Hephaestus/Hermes/Artemis/Medusa) only loads in the Greece
        // (Call of Olympus) campaign. Europe and Shogun have none.
        private static readonly ItemOfPower.ItemType[] NorselandsItems =
        {
            ItemOfPower.ItemType.ThorItem,
            ItemOfPower.ItemType.HelItem,
            ItemOfPower.ItemType.HeimdalItem,
            ItemOfPower.ItemType.LokiItem
        };

        private static readonly ItemOfPower.ItemType[] GreeceItems =
        {
            ItemOfPower.ItemType.HephaestusHammer,
            ItemOfPower.ItemType.HermesStaff,
            ItemOfPower.ItemType.ArtemisBow,
            ItemOfPower.ItemType.MedusaShield
        };

        private static readonly ItemOfPower.ItemType[] NoItems = Array.Empty<ItemOfPower.ItemType>();

        /// <summary>Items of power that load in the current campaign, or empty.</summary>
        public static ItemOfPower.ItemType[] CurrentBiomeItems()
        {
            if (IsNorselandsBiome()) return NorselandsItems;
            if (IsGreeceBiome()) return GreeceItems;
            return NoItems;
        }

        /// <summary>True when the current campaign has switchable items of power.</summary>
        public static bool BiomeHasItemsOfPower() => CurrentBiomeItems().Length > 0;

        /// <summary>True when <paramref name="item"/> belongs to the current campaign (None always allowed).</summary>
        public static bool ItemBelongsToCurrentBiome(ItemOfPower.ItemType item)
        {
            if (item == ItemOfPower.ItemType.None) return true;
            var items = CurrentBiomeItems();
            for (int i = 0; i < items.Length; i++)
                if (items[i] == item) return true;
            return false;
        }

        public static void ApplyItemPower(Player player, int playerNumber, ItemOfPower.ItemType item, Action<string> log)
        {
            if (player == null)
            {
                log($"Player {playerNumber} not in scene.");
                return;
            }

            if (!BiomeHasItemsOfPower())
            {
                log($"Player {playerNumber}: items of power only load while playing the Norse Lands or Olympus campaign.");
                return;
            }

            if (!ItemBelongsToCurrentBiome(item))
            {
                log($"Player {playerNumber}: {ItemLabel(item)} does not load in this campaign.");
                return;
            }

            try
            {
                player.equippedItemOfPower = item;
                RefreshPlayer(player);
                RuntimeInteractionLogger.Event(RuntimeLogLevel.EventHeavy, "power", "apply_item_power", player,
                    data: RuntimeInteractionLogger.Fields(("playerNumber", playerNumber), ("item", item)));
                log($"Player {playerNumber} item of power: {ItemLabel(item)}.");
            }
            catch (Exception e)
            {
                log($"Item of power switch failed: {e.GetType().Name}: {e.Message}");
            }
        }

        public static void ApplyMonarch(Player player, int playerNumber, int monarchChoice, Action<string> log)
        {
            if (player == null)
            {
                log($"Player {playerNumber} not in scene.");
                return;
            }

            if (monarchChoice == 0)
            {
                var original = LoaderMod.Instance?.GetOriginalMonarch(player.playerId);
                if (original == null)
                {
                    log($"Player {playerNumber}: original monarch has not been captured yet.");
                    return;
                }
                ApplyMonarchType(player, playerNumber, original.Value, "Original", log);
                return;
            }

            var next = ChoiceToDeadlands(monarchChoice);
            if (next == null)
            {
                log($"Player {playerNumber}: unknown monarch choice {monarchChoice}.");
                return;
            }

            if (!IsDeadlandsBiome())
            {
                log($"Player {playerNumber}: Dead Lands monarchs only load while playing the Dead Lands campaign.");
                return;
            }

            if (!IsDeadlands(player.model))
                LoaderMod.Instance?.RememberOriginalMonarch(player.playerId, player.model);

            ApplyMonarchType(player, playerNumber, next.Value, next.Value.ToString(), log);
        }

        public static void ApplyPersistedPowers()
        {
            var loader = LoaderMod.Instance;
            if (loader == null) return;

            foreach (var p in Kingdom.Players.All)
            {
                if (p == null) continue;
                int playerId = p.playerId;
                if (playerId < 0 || playerId > 1) continue;

                try
                {
                    if (loader.HasPersistedItemPower(playerId))
                    {
                        var item = loader.GetPersistedItemPower(playerId);
                        // Only reapply when the saved item actually loads in the
                        // current campaign, so a Norse item can't bleed into
                        // Olympus (or vice versa).
                        if (ItemBelongsToCurrentBiome(item) && p.equippedItemOfPower != item)
                        {
                            p.equippedItemOfPower = item;
                            RefreshPlayer(p);
                        }
                    }

                    int monarchChoice = loader.GetPersistedMonarchChoice(playerId);
                    if (monarchChoice > 0 && IsDeadlandsBiome())
                    {
                        var next = ChoiceToDeadlands(monarchChoice);
                        if (next != null && p.model != next.Value)
                        {
                            if (!IsDeadlands(p.model))
                                loader.RememberOriginalMonarch(playerId, p.model);
                            ApplyMonarchType(p, playerId + 1, next.Value, next.Value.ToString(), loader.LogToConsole);
                        }
                    }
                    else
                    {
                        var original = loader.GetOriginalMonarch(playerId);
                        if (original != null && p.model != original.Value)
                            ApplyMonarchType(p, playerId + 1, original.Value, "Original", loader.LogToConsole);
                    }
                }
                catch { }
            }
        }

        public static string ItemLabel(ItemOfPower.ItemType item)
        {
            return item switch
            {
                ItemOfPower.ItemType.None => "None",
                ItemOfPower.ItemType.ThorItem => "Thor",
                ItemOfPower.ItemType.HelItem => "Hel",
                ItemOfPower.ItemType.HeimdalItem => "Heimdal",
                ItemOfPower.ItemType.LokiItem => "Loki",
                ItemOfPower.ItemType.HephaestusHammer => "Hephaestus",
                ItemOfPower.ItemType.HermesStaff => "Hermes",
                ItemOfPower.ItemType.ArtemisBow => "Artemis",
                ItemOfPower.ItemType.MedusaShield => "Medusa",
                _ => item.ToString()
            };
        }

        public static MonarchType? ChoiceToDeadlands(int choice)
        {
            int index = choice - 1;
            if (index < 0 || index >= Deadlands.Length) return null;
            return Deadlands[index];
        }

        public static bool IsDeadlands(MonarchType type)
        {
            for (int i = 0; i < Deadlands.Length; i++)
                if (Deadlands[i] == type) return true;
            return false;
        }

        public static bool IsDeadlandsBiome() => IsBiome(BiomeHolder.DeadlandsBiomeIndex);

        public static bool IsNorselandsBiome() => IsBiome(BiomeHolder.NorselandsBiomeIndex);

        public static bool IsGreeceBiome() => IsBiome(BiomeHolder.GreeceBiomeIndex);

        private static bool IsBiome(int biomeIndex)
        {
            try
            {
                var inst = BiomeHolder.Inst;
                return inst != null && inst.BiomeIndex == biomeIndex;
            }
            catch { return false; }
        }

        private static void ApplyMonarchType(Player player, int playerNumber, MonarchType model, string label, Action<string> log)
        {
            try
            {
                player.model = model;
                try { CampaignSaveData.current?.SetPlayerAppearance(model, player.playerId); } catch { }
                RefreshPlayer(player);
                RuntimeInteractionLogger.Event(RuntimeLogLevel.EventHeavy, "power", "apply_monarch", player,
                    data: RuntimeInteractionLogger.Fields(("playerNumber", playerNumber), ("model", model), ("label", label)));
                log($"Player {playerNumber} monarch: {label}.");
            }
            catch (Exception e)
            {
                log($"Monarch switch failed: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void RefreshPlayer(Player player)
        {
            try { player.SetupPlayerModel(); } catch { }
            try { player.UpdateLocalPlayerModel(); } catch { }
        }
    }
}
