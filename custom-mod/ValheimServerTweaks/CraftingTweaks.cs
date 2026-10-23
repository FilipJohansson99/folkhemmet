using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimServerTweaks
{
    internal static class CraftingTweaks
    {
        private sealed class FuelState { internal bool Infinite; }
        private sealed class StationState { internal float Range, ExtraRange; }
        private static readonly ConditionalWeakTable<Fireplace, FuelState> FuelDefaults = new ConditionalWeakTable<Fireplace, FuelState>();
        private static readonly ConditionalWeakTable<CraftingStation, StationState> StationDefaults = new ConditionalWeakTable<CraftingStation, StationState>();
        private static readonly Dictionary<Recipe, int> RecipeDefaults = new Dictionary<Recipe, int>();

        internal static void ApplyRecipes()
        {
            if (ObjectDB.instance == null) return;
            foreach (Recipe recipe in ObjectDB.instance.m_recipes)
            {
                if (recipe == null || recipe.m_item == null || recipe.m_item.name != "BronzeNails") continue;
                if (!RecipeDefaults.TryGetValue(recipe, out int original))
                    RecipeDefaults[recipe] = original = recipe.m_amount;
                recipe.m_amount = original * Math.Max(1, ConfigSync.BronzeNailsMultiplier);
            }
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        private static class ObjectDB_Awake
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix() => ApplyRecipes();
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        private static class ObjectDB_CopyOtherDB
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix() => ApplyRecipes();
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class Player_OnSpawned
        {
            private static void Postfix() => ApplyRecipes();
        }

        [HarmonyPatch(typeof(Fireplace), "UpdateFireplace")]
        private static class Fireplace_UpdateFireplace
        {
            private static void Prefix(Fireplace __instance)
            {
                FuelState original = FuelDefaults.GetValue(__instance, fire => new FuelState { Infinite = fire.m_infiniteFuel });
                __instance.m_infiniteFuel = original.Infinite || ConfigSync.InfiniteLightFuel;
            }
        }

        [HarmonyPatch(typeof(Humanoid), "DrainEquipedItemDurability")]
        private static class Humanoid_DrainEquipedItemDurability
        {
            private static bool Prefix(ItemDrop.ItemData item) => !ConfigSync.InfiniteLightFuel ||
                item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Torch;
        }

        // Set the base range and extension bonus before vanilla calculates its cached
        // range. Its circle marker and effect collider then use that same result.
        [HarmonyPatch(typeof(CraftingStation), "GetExtensions")]
        private static class CraftingStation_GetExtensions
        {
            private static void Prefix(CraftingStation __instance)
            {
                if (__instance.m_name != "$piece_workbench") return;
                StationState original = StationDefaults.GetValue(__instance,
                    station => new StationState { Range = station.m_rangeBuild, ExtraRange = station.m_extraRangePerLevel });
                float radiusMultiplier = Mathf.Sqrt(Math.Max(1f, ConfigSync.WorkbenchAreaMultiplier));
                __instance.m_rangeBuild = original.Range * radiusMultiplier;
                __instance.m_extraRangePerLevel = original.ExtraRange * radiusMultiplier;
            }
        }

        [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.Interact))]
        private static class CraftingStation_Interact
        {
            private static void Postfix(CraftingStation __instance, Humanoid user, bool repeat)
            {
                Player player = Player.m_localPlayer;
                InventoryGui gui = InventoryGui.instance;
                if (!ConfigSync.AutoRepair || repeat || player == null || user != player || gui == null ||
                    !__instance.m_canRepair || player.GetCurrentCraftingStation() != __instance ||
                    !__instance.InUseDistance(player) || !__instance.CheckUsable(player, false)) return;

                // Use vanilla's repair method to preserve station requirements, skill
                // gains and other mods' repair hooks. Bound the loop to carried items.
                var worn = new List<ItemDrop.ItemData>();
                player.GetInventory().GetWornItems(worn);
                foreach (ItemDrop.ItemData item in worn)
                {
                    if (item.m_durability < item.GetMaxDurability() &&
                        Traverse.Create(gui).Method("CanRepair", item).GetValue<bool>())
                        Traverse.Create(gui).Method("RepairOneItem").GetValue();
                }
            }
        }
    }
}
