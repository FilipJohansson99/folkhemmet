using System;
using HarmonyLib;

namespace ValheimServerTweaks
{
    /// <summary>
    /// Pushes the server's map settings to clients so the server owner is in control.
    /// Values in use ("effective") are the local config until a server sends its own.
    /// </summary>
    internal static class ConfigSync
    {
        private const string RpcName = "ValheimServerTweaks_Config";
        private const int PackageVersion = 3;

        internal static float ExploreRadiusMultiplier { get; private set; } = 1f;
        internal static bool AlwaysShowPlayersOnMap { get; private set; }
        internal static bool ProtectBuildingsFromEnemies { get; private set; }
        internal static bool ProtectShipsAndCarts { get; private set; }
        internal static float StackSizeMultiplier { get; private set; } = 1f;
        internal static float StoneDropMultiplier { get; private set; } = 1f;
        internal static float WoodDropMultiplier { get; private set; } = 1f;
        internal static string StoneItems { get; private set; } = "";
        internal static string WoodItems { get; private set; } = "";
        internal static bool ReceivedFromServer { get; private set; }

        internal static void Init() => ResetToLocal();

        internal static void ResetToLocal()
        {
            ExploreRadiusMultiplier = Plugin.ExploreRadiusMultiplier.Value;
            AlwaysShowPlayersOnMap = Plugin.AlwaysShowPlayersOnMap.Value;
            ProtectBuildingsFromEnemies = Plugin.ProtectBuildingsFromEnemies.Value;
            ProtectShipsAndCarts = Plugin.ProtectShipsAndCarts.Value;
            StackSizeMultiplier = Plugin.StackSizeMultiplier.Value;
            StoneDropMultiplier = Plugin.StoneDropMultiplier.Value;
            WoodDropMultiplier = Plugin.WoodDropMultiplier.Value;
            StoneItems = Plugin.StoneItems.Value ?? "";
            WoodItems = Plugin.WoodItems.Value ?? "";
            ReceivedFromServer = false;
            ItemTweaks.ApplyStackSizes();
        }

        internal static void OnLocalConfigChanged()
        {
            // A client connected to a server keeps using the server's values.
            if (ReceivedFromServer) return;
            ResetToLocal();

            // The server re-sends the new values to everybody.
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                {
                    if (peer != null && peer.IsReady()) Send(peer);
                }
            }
            MapTweaks.ApplyLocalPublicPosition();
        }

        private static ZPackage BuildPackage()
        {
            var pkg = new ZPackage();
            pkg.Write(PackageVersion);
            pkg.Write(ExploreRadiusMultiplier);
            pkg.Write(AlwaysShowPlayersOnMap);
            pkg.Write(ProtectBuildingsFromEnemies);
            pkg.Write(ProtectShipsAndCarts);
            pkg.Write(StackSizeMultiplier);
            pkg.Write(StoneDropMultiplier);
            pkg.Write(WoodDropMultiplier);
            pkg.Write(StoneItems);
            pkg.Write(WoodItems);
            return pkg;
        }

        private static void Send(ZNetPeer peer)
        {
            try
            {
                peer.m_rpc.Invoke(RpcName, BuildPackage());
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not send config to {peer.m_playerName}: {e.Message}");
            }
        }

        private static void OnReceive(ZRpc rpc, ZPackage pkg)
        {
            try
            {
                int version = pkg.ReadInt();
                if (version < 1) return;
                ExploreRadiusMultiplier = pkg.ReadSingle();
                AlwaysShowPlayersOnMap = pkg.ReadBool();
                if (version >= 2)
                {
                    ProtectBuildingsFromEnemies = pkg.ReadBool();
                    ProtectShipsAndCarts = pkg.ReadBool();
                }
                if (version >= 3)
                {
                    StackSizeMultiplier = pkg.ReadSingle();
                    StoneDropMultiplier = pkg.ReadSingle();
                    WoodDropMultiplier = pkg.ReadSingle();
                    StoneItems = pkg.ReadString();
                    WoodItems = pkg.ReadString();
                }
                else
                {
                    // Older server build: it doesn't know these features, so play vanilla for them.
                    StackSizeMultiplier = 1f;
                    StoneDropMultiplier = 1f;
                    WoodDropMultiplier = 1f;
                }
                ReceivedFromServer = true;
                Plugin.Log.LogInfo($"Server settings received: explore radius x{ExploreRadiusMultiplier}, always show players = {AlwaysShowPlayersOnMap}, " +
                                   $"buildings protected = {ProtectBuildingsFromEnemies}, ships/carts protected = {ProtectShipsAndCarts}, " +
                                   $"stacks x{StackSizeMultiplier}, stone drops x{StoneDropMultiplier}, wood drops x{WoodDropMultiplier}.");
                MapTweaks.ApplyLocalPublicPosition();
                ItemTweaks.ApplyStackSizes();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read server config: " + e.Message);
            }
        }

        // New session (main menu -> world): start again from the local config.
        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class ZNet_Awake
        {
            private static void Postfix() => ResetToLocal();
        }

        // Client: listen for the server's settings as soon as the connection exists.
        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        private static class ZNet_OnNewConnection
        {
            private static void Postfix(ZNet __instance, ZNetPeer peer)
            {
                if (!__instance.IsServer()) peer.m_rpc.Register<ZPackage>(RpcName, OnReceive);
            }
        }

        // Server: send the settings once the player has identified itself.
        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        private static class ZNet_RPC_PeerInfo
        {
            private static void Postfix(ZNet __instance, ZRpc rpc)
            {
                if (!__instance.IsServer()) return;
                foreach (ZNetPeer peer in __instance.GetPeers())
                {
                    if (peer != null && peer.m_rpc == rpc)
                    {
                        Send(peer);
                        return;
                    }
                }
            }
        }
    }
}
