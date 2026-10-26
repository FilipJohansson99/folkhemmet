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
    [BepInDependency("eu.mydayyy.plugins.serversidemap")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "filip.valheim.servertweaks";
        public const string ModName = "ValheimServerTweaks";
        public const string Version = "1.3.4";

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
        internal static ConfigEntry<bool> InfiniteLightFuel;
        internal static ConfigEntry<bool> AutoRepair;
        internal static ConfigEntry<float> WorkbenchAreaMultiplier;
        internal static ConfigEntry<int> BronzeNailsMultiplier;
        internal static ConfigEntry<bool> SharedPersistentPins;

        // Client only
        internal static ConfigEntry<bool> TexturesEnabled;
        internal static ConfigEntry<string> TextureFolder;
        internal static ConfigEntry<string> DumpFolder;
        internal static ConfigEntry<bool> AudioEnabled;
        internal static ConfigEntry<string> FaderDisplayName;
        internal static ConfigEntry<bool> IntroEnabled;
        internal static ConfigEntry<bool> ForceStartupIntro;

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

            InfiniteLightFuel = Config.Bind("Buildings", "InfiniteLightFuel", true,
                "Torches, braziers, campfires and hearths burn without fuel. Does not change smelters or kilns. Server controlled.");
            AutoRepair = Config.Bind("Crafting", "AutoRepair", true,
                "Repair all eligible carried equipment when opening a usable crafting station. Normal station/type/level requirements apply. Server controlled.");
            WorkbenchAreaMultiplier = Config.Bind("Crafting", "WorkbenchAreaMultiplier", 2f,
                new ConfigDescription("Covered building area multiplier for workbenches (2 = radius x sqrt(2), about 28.3 m from 20 m). Server controlled.",
                    new AcceptableValueRange<float>(1f, 100f)));
            BronzeNailsMultiplier = Config.Bind("Crafting", "BronzeNailsMultiplier", 2,
                new ConfigDescription("Bronze nail crafting output multiplier; ingredient costs are unchanged. Server controlled.",
                    new AcceptableValueRange<int>(1, 10)));
            SharedPersistentPins = Config.Bind("Map", "SharedPersistentPins", true,
                "Share saved map pins from the custom UI, including existing local pins, and keep an atomic server pin save with a backup. Personal death/bed markers stay private. Server controlled.");

            TexturesEnabled = Config.Bind("Textures", "Enabled", true,
                "Client only. Load replacement textures from the texture folder.");

            TextureFolder = Config.Bind("Textures", "TextureFolder", "",
                "Client only. Folder with replacement textures. Empty = the 'textures' folder next to this mod's dll. " +
                "Layout: <folder>\\<PrefabName>\\<MaterialName>.png (main texture) or <MaterialName>._BumpMap.png etc.");

            DumpFolder = Config.Bind("Textures", "DumpFolder", "",
                "Client only. Where the tex_dump console command writes the original textures. Empty = 'texture-dumps' next to this mod's dll.");

            AudioEnabled = Config.Bind("Audio", "Enabled", true,
                "Client only. Replace sound effects and music using the audio/sfx and audio/music folders next to this mod's DLL.");
            FaderDisplayName = Config.Bind("Bosses", "FaderDisplayName", "Jimpan",
                "Client display name for Fader. Does not change the boss prefab, progression keys or kill records.");
            IntroEnabled = Config.Bind("Video", "ReplaceStartupIntro", true,
                "Client only. Replace the startup intro cinematic with videos/intro.mp4 next to this mod's DLL.");
            ForceStartupIntro = Config.Bind("Video", "ShowOnStartup", true,
                "Show the custom intro on a normal fresh game launch even if the vanilla intro was disabled. Preserves the saved vanilla preference.");

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
            InfiniteLightFuel.SettingChanged += (s, e) => ConfigSync.OnLocalConfigChanged();
            AutoRepair.SettingChanged += (s, e) => ConfigSync.OnLocalConfigChanged();
            WorkbenchAreaMultiplier.SettingChanged += (s, e) => ConfigSync.OnLocalConfigChanged();
            BronzeNailsMultiplier.SettingChanged += (s, e) => ConfigSync.OnLocalConfigChanged();
            SharedPersistentPins.SettingChanged += (s, e) => ConfigSync.OnLocalConfigChanged();

            new Harmony(Guid).PatchAll(Assembly.GetExecutingAssembly());
            if (!IsHeadless && AudioEnabled.Value) StartCoroutine(AudioReplacements.Load());

            Log.LogInfo($"{ModName} {Version} loaded ({(IsHeadless ? "dedicated server" : "client")}).");
        }

        private void Update()
        {
            SharedPins.Tick();
            AggroAudio.Tick();
        }

        private void OnDestroy() => AggroAudio.Cleanup();
    }
}
