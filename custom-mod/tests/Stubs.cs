// Stand-ins for game APIs: run the production reconciliation and persistence
// code without starting Valheim or accessing any actual world saves.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch : Attribute { public HarmonyPatch(Type t, string name) { } public HarmonyPatch(Type t, string name, Type[] args) { } }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPriority : Attribute { public HarmonyPriority(int p) { } }
    public static class Priority { public const int First = 800, Last = 0; }
    public static class AccessTools
    {
        public delegate ref F FieldRef<T, F>(T instance);
        private static ref List<Minimap.PinData> Pins(Minimap map) => ref map.m_pins;
        private static ref Minimap.PinData Naming(Minimap map) => ref map.m_namePin;
        public static FieldRef<T, F> FieldRefAccess<T, F>(string name) => name == "m_pins"
            ? (FieldRef<T, F>)(object)(FieldRef<Minimap, List<Minimap.PinData>>)Pins
            : (FieldRef<T, F>)(object)(FieldRef<Minimap, Minimap.PinData>)Naming;
    }
}
namespace UnityEngine
{
    public static class Time { public static float unscaledTime, realtimeSinceStartup; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public float sqrMagnitude => x*x + y*y + z*z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x-b.x, a.y-b.y, a.z-b.z);
    }
}
public class Minimap
{
    public static Minimap instance = new Minimap();
    public List<PinData> m_pins = new List<PinData>();
    public PinData m_namePin;
    public void RemovePin(PinData pin) => m_pins.Remove(pin);
    public enum PinType { Icon0, Icon1, Icon2, Icon3, Death, Bed, Icon4, Shout, None, Boss, Player, RandomEvent, Ping, EventArea, Hildir1, Hildir2, Hildir3 }
    public class PinData
    {
        public string m_name;
        public PinType m_type;
        public UnityEngine.Vector3 m_pos;
        public bool m_save, m_checked;
    }
}
public class Player { public static Player m_localPlayer = new Player(); }
public class World { public string Path; public string GetDBPath() => Path; }
public class ZNet
{
    public static ZNet instance = new ZNet();
    public static World World;
    public bool Server;
    public bool IsServer() => Server;
}
public class ZRpc { }
public class ZPackage
{
    public ServerSideMap.PinData Pin;
    public bool Checked;
    public void SetPos(int pos) { }
    public bool ReadBool() => Checked;
}
namespace ValheimServerTweaks
{
    internal static class ConfigSync { public static bool SharedPersistentPins = true, ReceivedFromServer = true; }
    internal static class Plugin
    {
        public static bool IsHeadless;
        public static string PluginDir = "";
        public static BoolSetting AudioEnabled = new BoolSetting { Value = true };
        public static BoolSetting IntroEnabled = new BoolSetting { Value = true };
        public static BoolSetting ForceStartupIntro = new BoolSetting { Value = true };
        public static StringSetting FaderDisplayName = new StringSetting { Value = "Jimpan" };
        public class BoolSetting { public bool Value; }
        public class StringSetting { public string Value; }
        public static LogSink Log = new LogSink();
        public static bool AllowWarnings;
        public class LogSink { public void LogInfo(string message) { } public void LogWarning(string message) { if (!AllowWarnings) throw new Exception(message); } public void LogError(string message) => throw new Exception(message); }
    }
    internal static class Hooks
    {
        internal static object Call(string patch, string method, params object[] args) =>
            typeof(SharedPins).GetNestedType(patch, BindingFlags.NonPublic).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
    }
}
namespace ServerSideMap
{
    public class PinData { public string Name; public UnityEngine.Vector3 Pos; public Minimap.PinType Type; public bool Checked; }
    public static class Store { public static bool IsSharingPin() => true; }
    public static class ExplorationDatabase
    {
        public static List<PinData> ClientPins = new List<PinData>();
        public static List<PinData> ServerPins = new List<PinData>();
        public static List<PinData> GetPins() => ServerPins;
        public static PinData UnpackPin(ZPackage data) => data.Pin;
    }
    public static class UtilityPin
    {
        public static PinData ConvertPin(Minimap.PinData pin) => new PinData { Name = pin.m_name, Pos = pin.m_pos, Type = pin.m_type, Checked = pin.m_checked };
        public static bool Same(PinData a, PinData b) => a.Name == b.Name && a.Type == b.Type && (a.Pos-b.Pos).sqrMagnitude < 0.01f;
    }
    public static class InitialPinSync { public static void OnReceiveInitialDataPin() { } }
    public static class PinSync
    {
        public static int Adds, Removes, Checks;
        public static void SendPinToServer(Minimap.PinData pin)
        {
            if ((bool)ValheimServerTweaks.Hooks.Call("ClientSend", "Prefix", pin))
            {
                pin.m_save = false;
                ExplorationDatabase.ClientPins.Add(UtilityPin.ConvertPin(pin));
                OnClientAddPin(new ZPackage { Pin = UtilityPin.ConvertPin(pin) });
            }
            ValheimServerTweaks.Hooks.Call("ClientSend", "Postfix", pin);
        }
        public static void OnClientAddPin(ZPackage data)
        {
            if ((bool)ValheimServerTweaks.Hooks.Call("ServerAdd", "Prefix", data))
            { ExplorationDatabase.ServerPins.Add(data.Pin); Adds++; }
            ValheimServerTweaks.Hooks.Call("ServerAdd", "Postfix");
        }
        public static void RemovePinFromServer(PinData pin)
        {
            ExplorationDatabase.ClientPins.RemoveAll(p => UtilityPin.Same(p, pin));
            ExplorationDatabase.ServerPins.RemoveAll(p => UtilityPin.Same(p, pin));
            Removes++;
            ValheimServerTweaks.Hooks.Call("ServerRemove", "Postfix");
        }
        public static void CheckPinOnServer(PinData pin, bool isChecked)
        {
            foreach (PinData shared in ExplorationDatabase.ServerPins.Where(p => UtilityPin.Same(p, pin))) shared.Checked = isChecked;
            Checks++;
            ValheimServerTweaks.Hooks.Call("ServerCheck", "Postfix");
        }
        public static void OnClientRemovePin() { }
        public static void OnClientCheckPin() { }
        public static void OnServerAddPin(PinData pin)
        {
            if (!(bool)ValheimServerTweaks.Hooks.Call("ClientAdd", "Prefix", new ZPackage { Pin = pin })) return;
            ExplorationDatabase.ClientPins.Add(pin);
            Minimap.instance.m_pins.Add(new Minimap.PinData { m_name = pin.Name, m_type = pin.Type, m_pos = pin.Pos, m_checked = pin.Checked });
            ValheimServerTweaks.Hooks.Call("ClientAdd", "Postfix");
        }
        public static void OnServerRemovePin(PinData pin)
        {
            ValheimServerTweaks.Hooks.Call("ClientRemove", "Prefix", new ZPackage { Pin = pin });
            Minimap.instance.m_pins.RemoveAll(p => UtilityPin.Same(UtilityPin.ConvertPin(p), pin));
            ExplorationDatabase.ClientPins.RemoveAll(p => UtilityPin.Same(p, pin));
        }
        public static void OnServerCheckPin(PinData pin, bool isChecked)
        {
            foreach (PinData shared in ExplorationDatabase.ClientPins.Where(p => UtilityPin.Same(p, pin))) shared.Checked = isChecked;
            foreach (Minimap.PinData local in Minimap.instance.m_pins.Where(p => UtilityPin.Same(UtilityPin.ConvertPin(p), pin))) local.m_checked = isChecked;
            ValheimServerTweaks.Hooks.Call("ClientCheck", "Postfix", new ZPackage { Pin = pin, Checked = isChecked });
        }
    }
}
