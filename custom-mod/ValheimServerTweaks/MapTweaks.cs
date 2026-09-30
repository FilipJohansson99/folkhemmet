using HarmonyLib;

namespace ValheimServerTweaks
{
    internal static class MapTweaks
    {
        private static float s_baseExploreRadius = -1f;

        // ---------- Explore radius ----------

        [HarmonyPatch(typeof(Minimap), "Awake")]
        private static class Minimap_Awake
        {
            private static void Postfix(Minimap __instance) => s_baseExploreRadius = __instance.m_exploreRadius;
        }

        [HarmonyPatch(typeof(Minimap), "UpdateExplore")]
        private static class Minimap_UpdateExplore
        {
            private static void Prefix(Minimap __instance)
            {
                if (s_baseExploreRadius <= 0f) s_baseExploreRadius = __instance.m_exploreRadius;
                __instance.m_exploreRadius = s_baseExploreRadius * ConfigSync.ExploreRadiusMultiplier;
            }
        }

        // ---------- Always show players on the map ----------

        /// <summary>Client side: keep our own "visible on map" switch on.</summary>
        internal static void ApplyLocalPublicPosition()
        {
            if (ConfigSync.AlwaysShowPlayersOnMap && ZNet.instance != null)
                ZNet.instance.SetPublicReferencePosition(true);
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.SetPublicReferencePosition))]
        private static class ZNet_SetPublicReferencePosition
        {
            private static void Prefix(ref bool pub)
            {
                if (ConfigSync.AlwaysShowPlayersOnMap) pub = true;
            }
        }

        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class ZNet_Awake
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix() => ApplyLocalPublicPosition();
        }

        /// <summary>
        /// Server side: whatever the client says, publish its position.
        /// This is what actually makes everyone visible, even players without the mod.
        /// </summary>
        [HarmonyPatch(typeof(ZNet), "RPC_ServerSyncedPlayerData")]
        private static class ZNet_RPC_ServerSyncedPlayerData
        {
            private static void Postfix(ZNet __instance, ZRpc rpc)
            {
                if (!__instance.IsServer() || !Plugin.AlwaysShowPlayersOnMap.Value) return;
                foreach (ZNetPeer peer in __instance.GetPeers())
                {
                    if (peer != null && peer.m_rpc == rpc)
                    {
                        peer.m_publicRefPos = true;
                        return;
                    }
                }
            }
        }
    }
}
