using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

[assembly: AssemblyTitle("ValheimServerTweaks")]
[assembly: AssemblyVersion(ValheimServerTweaks.Plugin.Version)]
[assembly: AssemblyFileVersion(ValheimServerTweaks.Plugin.Version)]

namespace ValheimServerTweaks
{
    /// <summary>
    /// Small server/client mod for our Valheim server:
    ///  - Custom textures: drop PNGs in the "textures" folder to re-skin trolls, sails, anything.
    ///  - Map exploration radius multiplier (server controlled).
    ///  - Every player is always visible on the map (server enforced).
    ///  - Buildings can't be damaged by monsters.
    ///  - Bigger item stacks, more stone and wood from gathering.
    /// Hunger speed is NOT done here: it uses the vanilla "foodrate" world modifier set by start-server.ps1.
    /// </summary>
    [BepInPlugin(Guid, ModName, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "filip.valheim.servertweaks";
        public const string ModName = "ValheimServerTweaks";
        public const string Version = "1.2.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;
        internal static string PluginDir;

        // Server controlled (the server's values are pushed to every client that joins)
        internal static ConfigEntry<float> ExploreRadiusMultiplier;
        internal static ConfigEntry<bool> AlwaysShowPlayersOnMap;
        internal static ConfigEntry<bool> ProtectBuildingsFromEnemies;
        internal static ConfigEntry<bool> ProtectShipsAndCarts;
        internal static ConfigEntry<float> StackSizeMultiplier;
        internal static ConfigEntry<float> StoneDropMultiplier;
        internal static ConfigEntry<float> WoodDropMultiplier;
        internal static ConfigEntry<string> StoneItems;
        internal static ConfigEntry<string> WoodItems;

        // Client only
        internal static ConfigEntry<bool> TexturesEnabled;
        internal static ConfigEntry<string> TextureFolder;
        internal static ConfigEntry<string> DumpFolder;

        /// <summary>True when running without graphics (the dedicated server).</summary>
        internal static bool IsHeadless => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            PluginDir = Path.GetDirectoryName(Info.Location);

            ExploreRadiusMultiplier = Config.Bind("Map", "ExploreRadiusMultiplier", 3f,
                new ConfigDescription(
                    "Multiplier for how far around you the map gets revealed (vanilla radius is 100 m, so 3 = 300 m). " +
                    "Server controlled: the server's value is sent to every player when they join.",
                    new AcceptableValueRange<float>(0.1f, 20f)));

            AlwaysShowPlayersOnMap = Config.Bind("Map", "AlwaysShowPlayersOnMap", true,
                "Force every player's position to be visible on the map for everyone. " +
                "Server controlled: enforced by the server, works even for players without the mod.");

            ProtectBuildingsFromEnemies = Config.Bind("Buildings", "ProtectFromEnemies", true,
                "Built pieces take no damage from monsters (players can still damage and deconstruct them). " +
                "Server controlled: the server's value is sent to every player when they join.");

            ProtectShipsAndCarts = Config.Bind("Buildings", "AlsoProtectShipsAndCarts", false,
                "Also make ships and carts immune to monsters (e.g. serpents). Server controlled.");

            StackSizeMultiplier = Config.Bind("Items", "StackSizeMultiplier", 2f,
                new ConfigDescription("Max stack size of every stackable item is multiplied by this (2 = wood stacks to 100 instead of 50). " +
                    "Server controlled.", new AcceptableValueRange<float>(0.1f, 100f)));

            StoneDropMultiplier = Config.Bind("Drops", "StoneMultiplier", 3f,
                new ConfigDescription("Stone from rocks, ore deposits and the ground is multiplied by this. Server controlled.",
                    new AcceptableValueRange<float>(0f, 100f)));

            StoneItems = Config.Bind("Drops", "StoneItems", "Stone,Grausten,BlackMarble",
                "Item names (prefab names) that count as stone, comma separated. Server controlled.");

            WoodDropMultiplier = Config.Bind("Drops", "WoodMultiplier", 2f,
                new ConfigDescription("Wood from trees, logs, stumps and branches is multiplied by this. Server controlled.",
                    new AcceptableValueRange<float>(0f, 100f)));

            WoodItems = Config.Bind("Drops", "WoodItems", "Wood,FineWood,RoundLog,ElderBark,YggdrasilWood,Blackwood",
                "Item names (prefab names) that count as wood, comma separated (RoundLog = core wood). Server controlled.");

            TexturesEnabled = Config.Bind("Textures", "Enabled", true,
                "Client only. Load replacement textures from the texture folder.");

            TextureFolder = Config.Bind("Textures", "TextureFolder", "",
                "Client only. Folder with replacement textures. Empty = the 'textures' folder next to this mod's dll. " +
                "Layout: <folder>\\<PrefabName>\\<MaterialName>.png (main texture) or <MaterialName>._BumpMap.png etc.");

            DumpFolder = Config.Bind("Textures", "DumpFolder", "",
                "Client only. Where the tex_dump console command writes the original textures. Empty = 'texture-dumps' next to this mod's dll.");

            ConfigSync.Init();
            ExploreRadiusMultiplier.SettingChanged += (s, e) => ConfigSync.OnLocalConfigChanged();
            AlwaysShowPlayersOnMap.SettingChanged += (s, e) => ConfigSync.OnLocalConfigChanged();
            ProtectBuildingsFromEnemies.SettingChanged += (s, e) => ConfigSync.OnLocalConfigChanged();
            ProtectShipsAndCarts.SettingChanged += (s, e) => ConfigSync.OnLocalConfigChanged();
            StackSizeMultiplier.SettingChanged += (s, e) => ConfigSync.OnLocalConfigChanged();
            StoneDropMultiplier.SettingChanged += (s, e) => ConfigSync.OnLocalConfigChanged();
            WoodDropMultiplier.SettingChanged += (s, e) => ConfigSync.OnLocalConfigChanged();
            StoneItems.SettingChanged += (s, e) => ConfigSync.OnLocalConfigChanged();
            WoodItems.SettingChanged += (s, e) => ConfigSync.OnLocalConfigChanged();

            new Harmony(Guid).PatchAll(Assembly.GetExecutingAssembly());

            Log.LogInfo($"{ModName} {Version} loaded ({(IsHeadless ? "dedicated server" : "client")}).");
        }
    }
}
