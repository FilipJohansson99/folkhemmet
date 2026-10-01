using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimServerTweaks
{
    /// <summary>
    /// - Bigger stacks: every stackable item's max stack size x StackSizeMultiplier.
    /// - More resources: stone / wood dropped by rocks, ore deposits, trees, logs, stumps and pickables
    ///   are multiplied. Deconstructing your own buildings is NOT affected (no duplication exploit).
    /// All values come from the server (ConfigSync), so everyone plays by the same rules.
    /// </summary>
    internal static class ItemTweaks
    {
        // Original stack sizes, so re-applying never compounds (prefab data lives for the whole game session).
        private static readonly Dictionary<ItemDrop.ItemData.SharedData, int> s_originalStack =
            new Dictionary<ItemDrop.ItemData.SharedData, int>();

        // ------------------------------------------------------------------ stack sizes

        internal static void ApplyStackSizes()
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null || db.m_items == null) return;
            float mult = Math.Max(0.1f, ConfigSync.StackSizeMultiplier);
            int changed = 0;
            foreach (GameObject prefab in db.m_items)
            {
                if (prefab == null) continue;
                ItemDrop drop = prefab.GetComponent<ItemDrop>();
                if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null) continue;
                ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
                if (!s_originalStack.TryGetValue(shared, out int original))
                {
                    original = shared.m_maxStackSize;
                    s_originalStack[shared] = original;
                }
                if (original <= 1) continue; // never make unstackable things (tools, armor...) stack
                int size = Math.Max(1, Mathf.RoundToInt(original * mult));
                if (shared.m_maxStackSize != size) { shared.m_maxStackSize = size; changed++; }
            }
            if (changed > 0) Plugin.Log.LogInfo($"Stack sizes x{mult} applied to {changed} item(s).");
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        private static class ObjectDB_Awake
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix() => ApplyStackSizes();
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        private static class ObjectDB_CopyOtherDB
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix() => ApplyStackSizes();
        }

        // Mods (Jotunn) can add items late - make sure they're included once the player is in the world.
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class Player_OnSpawned
        {
            private static void Postfix() => ApplyStackSizes();
        }

        // ------------------------------------------------------------------ drops

        private static string s_stoneList, s_woodList;
        private static HashSet<string> s_stone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> s_wood = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static HashSet<string> ParseList(string list)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(list)) return set;
            foreach (string part in list.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = part.Trim();
                if (name.Length > 0) set.Add(name);
            }
            return set;
        }

        /// <summary>Multiplier for a dropped item (by prefab name), 1 = unchanged.</summary>
        internal static float DropMultiplier(string prefabName)
        {
            if (!ReferenceEquals(s_stoneList, ConfigSync.StoneItems)) { s_stoneList = ConfigSync.StoneItems; s_stone = ParseList(s_stoneList); }
            if (!ReferenceEquals(s_woodList, ConfigSync.WoodItems)) { s_woodList = ConfigSync.WoodItems; s_wood = ParseList(s_woodList); }
            if (s_stone.Contains(prefabName)) return ConfigSync.StoneDropMultiplier;
            if (s_wood.Contains(prefabName)) return ConfigSync.WoodDropMultiplier;
            return 1f;
        }

        /// <summary>How many items to give for one: 2.5 gives 2 or 3 (50/50), 3 gives 3.</summary>
        private static int Roll(float mult)
        {
            if (mult <= 0f) return 0;
            int whole = (int)mult;
            float frac = mult - whole;
            return whole + (frac > 0f && UnityEngine.Random.value < frac ? 1 : 0);
        }

        // Rocks, ore deposits, trees, logs, stumps, bushes... all drop through DropTable.GetDropList().
        [HarmonyPatch(typeof(DropTable), nameof(DropTable.GetDropList), new Type[0])]
        private static class DropTable_GetDropList
        {
            private static void Postfix(ref List<GameObject> __result)
            {
                if (__result == null || __result.Count == 0) return;
                List<GameObject> output = null;
                for (int i = 0; i < __result.Count; i++)
                {
                    GameObject item = __result[i];
                    float mult = item != null ? DropMultiplier(item.name) : 1f;
                    if (output == null)
                    {
                        if (mult == 1f) continue;
                        output = new List<GameObject>(__result.Count * 3);
                        for (int j = 0; j < i; j++) output.Add(__result[j]);
                    }
                    int count = mult == 1f ? 1 : Roll(mult);
                    for (int k = 0; k < count; k++) output.Add(item);
                }
                if (output != null) __result = output;
            }
        }

        // Stones and branches lying on the ground.
        [HarmonyPatch(typeof(Pickable), "RPC_Pick")]
        private static class Pickable_RPC_Pick
        {
            private static void Prefix(Pickable __instance, out int __state)
            {
                __state = -1;
                if (__instance.m_itemPrefab == null) return;
                float mult = DropMultiplier(__instance.m_itemPrefab.name);
                if (mult == 1f) return;
                __state = __instance.m_amount;
                __instance.m_amount = Math.Max(0, Roll(mult * __instance.m_amount));
            }

            private static void Postfix(Pickable __instance, int __state)
            {
                if (__state >= 0) __instance.m_amount = __state;
            }
        }
    }
}
