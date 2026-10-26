using System;
using HarmonyLib;

namespace ValheimServerTweaks
{
    internal static class BossNames
    {
        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
        private static class Character_GetHoverName
        {
            private static void Postfix(Character __instance, ref string __result)
            {
                if (Plugin.IsHeadless || string.IsNullOrWhiteSpace(Plugin.FaderDisplayName.Value)) return;
                // Change only the returned UI label. Character.m_name also participates
                // in gameplay/kill records and must remain the original game identifier.
                if (string.Equals(__instance.m_name, "$enemy_fader", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(__instance.gameObject.name, "Fader(Clone)", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(__instance.gameObject.name, "Fader", StringComparison.OrdinalIgnoreCase))
                    __result = Plugin.FaderDisplayName.Value.Trim();
            }
        }
    }
}
