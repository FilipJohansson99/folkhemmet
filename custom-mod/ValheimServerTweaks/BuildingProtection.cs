using HarmonyLib;

namespace ValheimServerTweaks
{
    /// <summary>
    /// Player buildings take no damage from monsters (melee, projectiles, area attacks, their fire).
    /// Players can still damage/deconstruct their own pieces; weather, falling trees etc. still work as normal.
    /// Damage is applied by whoever "owns" the piece (usually the nearest player), so every client needs the mod
    /// - Play.bat takes care of that. The setting itself comes from the server.
    /// </summary>
    internal static class BuildingProtection
    {
        [HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
        private static class WearNTear_RPC_Damage
        {
            private static bool Prefix(WearNTear __instance, HitData hit)
            {
                if (!ConfigSync.ProtectBuildingsFromEnemies || hit == null) return true;
                if (!IsEnemyHit(hit) || !IsProtected(__instance)) return true;
                return false; // ignore the hit completely
            }
        }

        private static bool IsEnemyHit(HitData hit)
        {
            if (hit.m_hitType == HitData.HitType.EnemyHit) return true;
            if (hit.m_hitType == HitData.HitType.PlayerHit) return false;
            Character attacker = hit.GetAttacker();
            return attacker != null && !attacker.IsPlayer();
        }

        private static bool IsProtected(WearNTear wnt)
        {
            if (wnt.GetComponent<Piece>() == null) return false; // only things that are built
            if (!ConfigSync.ProtectShipsAndCarts && (wnt.GetComponent<Ship>() != null || wnt.GetComponent<Vagon>() != null)) return false;
            return true;
        }
    }
}
