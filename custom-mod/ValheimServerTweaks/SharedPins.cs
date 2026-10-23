using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using ServerSideMap;
using UnityEngine;
using MapPin = Minimap.PinData;
using SharedPin = ServerSideMap.PinData;

namespace ValheimServerTweaks
{
    // ServerSideMap's vanilla UI hooks miss SeneaL's custom pin dialog. Reconcile
    // actual pin objects, while retaining its existing RPC protocol and map format.
    internal static class SharedPins
    {
        private static readonly Dictionary<MapPin, SharedPin> Tracked = new Dictionary<MapPin, SharedPin>();
        private static readonly AccessTools.FieldRef<Minimap, List<MapPin>> MapPins =
            AccessTools.FieldRefAccess<Minimap, List<MapPin>>("m_pins");
        private static readonly AccessTools.FieldRef<Minimap, MapPin> NamingPin =
            AccessTools.FieldRefAccess<Minimap, MapPin>("m_namePin");
        private static float nextTick;
        private static bool dirty, persistenceReady;
        private static string savePath;

        private static bool Enabled => ConfigSync.SharedPersistentPins && ZNet.instance != null && Store.IsSharingPin();
        private static bool Same(SharedPin a, SharedPin b) => a.Name == b.Name && a.Type == b.Type &&
            (a.Pos - b.Pos).sqrMagnitude < 0.01f;
        private static SharedPin Copy(MapPin pin) => UtilityPin.ConvertPin(pin);
        private static bool Shareable(MapPin pin) => pin.m_type == Minimap.PinType.Icon0 || pin.m_type == Minimap.PinType.Icon1 ||
            pin.m_type == Minimap.PinType.Icon2 || pin.m_type == Minimap.PinType.Icon3 || pin.m_type == Minimap.PinType.Icon4 ||
            pin.m_type == Minimap.PinType.Boss || pin.m_type == Minimap.PinType.Hildir1 ||
            pin.m_type == Minimap.PinType.Hildir2 || pin.m_type == Minimap.PinType.Hildir3;

        internal static void Tick()
        {
            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 1f;
            if (!Enabled) return;
            try
            {
                if (ZNet.instance.IsServer() && dirty) Save();
                if (Plugin.IsHeadless || Player.m_localPlayer == null || Minimap.instance == null ||
                    (!ZNet.instance.IsServer() && !ConfigSync.ReceivedFromServer)) return;
                Reconcile();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Pin synchronization: " + e.Message); }
        }

        private static void Reconcile()
        {
            List<MapPin> pins = MapPins(Minimap.instance);
            MapPin editing = NamingPin(Minimap.instance);
            foreach (var pair in Tracked.ToArray())
            {
                MapPin pin = pair.Key;
                SharedPin old = pair.Value;
                if (pin == editing) continue;
                if (!pins.Contains(pin))
                {
                    PinSync.RemovePinFromServer(old);
                    Tracked.Remove(pin);
                    continue;
                }
                SharedPin current = Copy(pin);
                if (!Same(old, current))
                {
                    PinSync.RemovePinFromServer(old);
                    Tracked.Remove(pin);
                    PinSync.SendPinToServer(pin);
                }
                else if (old.Checked != current.Checked)
                {
                    SharedPin backing = ExplorationDatabase.ClientPins.FirstOrDefault(p => Same(p, old));
                    if (backing != null && backing.Checked != current.Checked)
                    {
                        backing.Checked = current.Checked;
                        PinSync.CheckPinOnServer(backing, current.Checked);
                    }
                }
                Tracked[pin] = current;
            }
            foreach (MapPin pin in pins.ToArray())
            {
                if (Tracked.ContainsKey(pin) || pin == editing || !Shareable(pin)) continue;
                SharedPin current = Copy(pin);
                SharedPin backing = ExplorationDatabase.ClientPins.FirstOrDefault(p => Same(p, current));
                if (backing != null)
                {
                    if (Tracked.Values.Any(shared => Same(shared, current)))
                    {
                        Minimap.instance.RemovePin(pin);
                        continue;
                    }
                    pin.m_checked = backing.Checked;
                    Tracked[pin] = Copy(pin);
                    pin.m_save = false;
                }
                else if (pin.m_save)
                {
                    // This also migrates older local pins automatically. Match exact
                    // name, type and position, rather than discarding nearby markers.
                    PinSync.SendPinToServer(pin);
                    Tracked[pin] = current;
                }
            }
        }

        private static void Save()
        {
            if (!persistenceReady || savePath == null) return;
            try
            {
                var pins = ExplorationDatabase.GetPins().Select(pin => new SavedPin { Name = pin.Name,
                    X = pin.Pos.x, Y = pin.Pos.y, Z = pin.Pos.z, Type = (int)pin.Type, Checked = pin.Checked }).ToList();
                PinStore.Save(savePath, pins);
                dirty = false;
            }
            catch (Exception e) { Plugin.Log.LogError("Could not persist shared pins: " + e.Message); }
        }

        [HarmonyPatch(typeof(ZNet), "ServerLoadWorld")]
        private static class LoadWorld
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix()
            {
                if (!ConfigSync.SharedPersistentPins || ZNet.World == null) return;
                savePath = Path.ChangeExtension(ZNet.World.GetDBPath(), null) + ".mod.folkhemmet-pins";
                try
                {
                    List<SavedPin> saved = PinStore.Load(savePath);
                    if (saved != null)
                    {
                        List<SharedPin> pins = ExplorationDatabase.GetPins();
                        pins.Clear();
                        foreach (SavedPin pin in saved)
                            pins.Add(new SharedPin { Name = pin.Name, Pos = new Vector3(pin.X, pin.Y, pin.Z),
                                Type = (Minimap.PinType)pin.Type, Checked = pin.Checked });
                        Plugin.Log.LogInfo($"Restored {saved.Count} persistent shared pins.");
                    }
                    persistenceReady = true;
                    dirty = true;
                    Save();
                }
                catch (Exception e)
                {
                    persistenceReady = false;
                    Plugin.Log.LogError("Could not load pin save or backup; preserving files and using ServerSideMap data: " + e.Message);
                }
            }
        }

        // Deduplicate imports from several players and repeated vanilla/UI callbacks.
        [HarmonyPatch(typeof(PinSync), nameof(PinSync.OnClientAddPin))]
        private static class ServerAdd
        {
            private static bool Prefix(ZPackage pinData)
            {
                if (!Enabled) return true;
                pinData.SetPos(0);
                SharedPin incoming = ExplorationDatabase.UnpackPin(pinData);
                pinData.SetPos(0);
                return !ExplorationDatabase.GetPins().Any(pin => Same(pin, incoming));
            }
            private static void Postfix() { if (Enabled) dirty = true; }
        }

        [HarmonyPatch(typeof(PinSync), nameof(PinSync.OnClientRemovePin))]
        private static class ServerRemove { private static void Postfix() { if (Enabled) dirty = true; } }

        [HarmonyPatch(typeof(PinSync), nameof(PinSync.OnClientCheckPin))]
        private static class ServerCheck { private static void Postfix() { if (Enabled) dirty = true; } }

        [HarmonyPatch(typeof(PinSync), nameof(PinSync.SendPinToServer))]
        private static class ClientSend
        {
            private static bool Prefix(MapPin pin)
            {
                if (!Enabled) return true;
                SharedPin current = Copy(pin);
                if (Tracked.TryGetValue(pin, out SharedPin old) && !Same(old, current))
                    PinSync.RemovePinFromServer(old);
                if (!ExplorationDatabase.ClientPins.Any(shared => Same(shared, current))) return true;
                pin.m_save = false;
                return false;
            }
            private static void Postfix(MapPin pin)
            {
                if (!Enabled) return;
                SharedPin current = Copy(pin);
                if (Tracked.Any(pair => pair.Key != pin && Same(pair.Value, current)))
                {
                    Tracked.Remove(pin);
                    if (Minimap.instance != null) Minimap.instance.RemovePin(pin);
                }
                else Tracked[pin] = current;
            }
        }

        [HarmonyPatch(typeof(InitialPinSync), nameof(InitialPinSync.OnReceiveInitialDataPin))]
        private static class ClientInitial
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix() { Tracked.Clear(); AdoptRemotePins(); nextTick = 0f; }
        }

        private static void AdoptRemotePins()
        {
            if (!Enabled || Minimap.instance == null || Plugin.IsHeadless) return;
            foreach (MapPin pin in MapPins(Minimap.instance))
                if (!pin.m_save && !Tracked.ContainsKey(pin) && ExplorationDatabase.ClientPins.Any(shared => Same(shared, Copy(pin))))
                    Tracked[pin] = Copy(pin);
        }

        [HarmonyPatch(typeof(PinSync), nameof(PinSync.OnServerAddPin))]
        private static class ClientAdd
        {
            private static bool Prefix(ZPackage pinData)
            {
                if (!Enabled) return true;
                pinData.SetPos(0);
                SharedPin incoming = ExplorationDatabase.UnpackPin(pinData);
                pinData.SetPos(0);
                return !ExplorationDatabase.ClientPins.Any(pin => Same(pin, incoming));
            }
            private static void Postfix() => AdoptRemotePins();
        }

        [HarmonyPatch(typeof(Minimap), "ClearPins")]
        private static class ClearPins
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix() { Tracked.Clear(); AdoptRemotePins(); }
        }

        // Remote removal/check changes must not be echoed back as local edits.
        [HarmonyPatch(typeof(PinSync), nameof(PinSync.OnServerRemovePin))]
        private static class ClientRemove
        {
            private static void Prefix(ZPackage pinData)
            {
                if (!Enabled) return;
                pinData.SetPos(0);
                SharedPin removed = ExplorationDatabase.UnpackPin(pinData);
                pinData.SetPos(0);
                foreach (var pair in Tracked.Where(pair => Same(pair.Value, removed)).ToArray()) Tracked.Remove(pair.Key);
            }
        }

        [HarmonyPatch(typeof(PinSync), nameof(PinSync.OnServerCheckPin))]
        private static class ClientCheck
        {
            private static void Postfix(ZPackage data)
            {
                if (!Enabled) return;
                data.SetPos(0);
                SharedPin changed = ExplorationDatabase.UnpackPin(data);
                bool isChecked = data.ReadBool();
                foreach (SharedPin tracked in Tracked.Values)
                    if (Same(tracked, changed)) tracked.Checked = isChecked;
            }
        }

        [HarmonyPatch(typeof(ZNet), "Shutdown")]
        private static class Shutdown
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix()
            {
                if (ZNet.instance != null && ZNet.instance.IsServer() && dirty) Save();
                Tracked.Clear(); persistenceReady = false; savePath = null; dirty = false;
            }
        }
    }
}
