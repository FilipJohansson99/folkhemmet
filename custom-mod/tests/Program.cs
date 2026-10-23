using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ServerSideMap;
using UnityEngine;
using ValheimServerTweaks;

class Program
{
    static void Assert(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); }
    static void Tick() { Time.unscaledTime += 2; SharedPins.Tick(); }
    static Minimap.PinData Pin(string name, float x = 1, Minimap.PinType type = Minimap.PinType.Icon0) =>
        new Minimap.PinData { m_name = name, m_pos = new Vector3(x, 0, 2), m_type = type, m_save = true };

    static void Main()
    {
        var local = Pin("Copper");
        Minimap.instance.m_pins.Add(local);
        Tick();
        Assert(PinSync.Adds == 1 && ExplorationDatabase.ServerPins.Count == 1 && !local.m_save, "existing local pin imported once");
        Tick();
        PinSync.SendPinToServer(local);
        Assert(PinSync.Adds == 1, "repeat tick and vanilla callback do not duplicate pins");
        local.m_name = "Copper mine";
        Tick();
        Assert(ExplorationDatabase.ServerPins.Count == 1 && ExplorationDatabase.ServerPins[0].Name == "Copper mine", "custom UI rename replaces old shared pin");
        local.m_checked = true;
        Tick();
        Assert(PinSync.Checks == 1 && ExplorationDatabase.ServerPins[0].Checked, "custom UI checked state shared");
        var duplicate = Pin("Copper mine");
        Minimap.instance.m_pins.Add(duplicate);
        Tick();
        Assert(ExplorationDatabase.ServerPins.Count == 1 && !Minimap.instance.m_pins.Contains(duplicate), "identical local copies are merged without duplicate map icons");
        Minimap.instance.m_pins.Add(Pin("Another deposit", 1.5f));
        Minimap.instance.m_pins.Add(Pin("My grave", 7, Minimap.PinType.Death));
        Minimap.instance.m_pins.Add(Pin("My bed", 8, Minimap.PinType.Bed));
        Tick();
        Assert(ExplorationDatabase.ServerPins.Count == 2, "nearby distinct pins shared; personal death and bed markers excluded");
        var pending = Pin("");
        Minimap.instance.m_pins.Add(pending);
        Minimap.instance.m_namePin = pending;
        Tick();
        Assert(ExplorationDatabase.ServerPins.Count == 2, "unfinished vanilla pin is not uploaded");
        pending.m_name = "Portal";
        Minimap.instance.m_namePin = null;
        PinSync.SendPinToServer(pending);
        Tick();
        Assert(ExplorationDatabase.ServerPins.Count == 3, "completed vanilla pin is shared once");
        local.m_name = "Renamed during incoming update";
        PinSync.OnServerCheckPin(UtilityPin.ConvertPin(pending), true);
        Tick();
        Assert(ExplorationDatabase.ServerPins.Any(p => p.Name == local.m_name), "unrelated remote check does not swallow a pending local rename");
        var remote = new ServerSideMap.PinData { Name = "Remote", Type = Minimap.PinType.Icon1, Pos = new Vector3(30, 0, 4) };
        PinSync.OnServerAddPin(remote);
        PinSync.OnServerAddPin(remote);
        Assert(Minimap.instance.m_pins.Count(p => p.m_name == "Remote") == 1, "repeated remote add does not create duplicate map icons");
        int removes = PinSync.Removes;
        PinSync.OnServerRemovePin(remote);
        Tick();
        Assert(PinSync.Removes == removes, "remote deletion is not echoed back");
        // Rebuilding minimap pin objects must not delete the server's shared pins.
        Minimap.instance.m_pins = ExplorationDatabase.ClientPins.Select(p => new Minimap.PinData {
            m_name = p.Name, m_type = p.Type, m_pos = p.Pos, m_checked = p.Checked }).ToList();
        Hooks.Call("ClearPins", "Postfix");
        Tick();
        Assert(PinSync.Removes == removes, "minimap refresh does not delete shared pins");
        Minimap.instance.m_pins.RemoveAt(0);
        Tick();
        Assert(PinSync.Removes == removes + 1, "custom UI deletion reaches server");

        string temp = Path.Combine(Path.GetTempPath(), "folkhemmet-pin-test-" + Guid.NewGuid());
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "test.pins");
            var saved = new List<SavedPin> { new SavedPin { Name = "Tångräkyr <mine>", X = 1.25f, Y = -2, Z = 3, Type = 6, Checked = true } };
            Assert(PinStore.Load(path) == null, "missing save is a new world");
            PinStore.Save(path, saved);
            SavedPin roundTrip = PinStore.Load(path)[0];
            Assert(roundTrip.Name == saved[0].Name && roundTrip.X == 1.25f && roundTrip.Y == -2 && roundTrip.Type == 6 && roundTrip.Checked,
                "pin names, positions, types and checks survive save/load");
            PinStore.Save(path, new List<SavedPin>());
            Assert(PinStore.Load(path).Count == 0 && File.Exists(path + ".bak"), "deletions persist and previous save is backed up");
            File.WriteAllBytes(path, new byte[] { 1 });
            Assert(PinStore.Load(path).Count == 1, "corrupt primary recovers from backup");
            PinStore.Save(path, saved);
            File.WriteAllBytes(path, new byte[] { 1 });
            Assert(PinStore.Load(path).Count == 1, "saving after recovery preserves valid backup");
            File.WriteAllBytes(path + ".bak", new byte[] { 1 });
            bool rejected = false;
            try { PinStore.Load(path); } catch (IOException) { rejected = true; }
            Assert(rejected, "two corrupt copies are rejected instead of interpreted as an empty save");

            ZNet.instance.Server = true;
            Plugin.IsHeadless = true;
            ZNet.World = new World { Path = Path.Combine(temp, "test-world.db") };
            int count = ExplorationDatabase.ServerPins.Count;
            Hooks.Call("LoadWorld", "Postfix");
            Hooks.Call("Shutdown", "Prefix");
            ExplorationDatabase.ServerPins.Clear();
            Hooks.Call("LoadWorld", "Postfix");
            Assert(ExplorationDatabase.ServerPins.Count == count && count > 0, "server shutdown/reload restores shared pins from sidecar");
        }
        finally
        {
            // Delete only files created inside this test's unique directory.
            foreach (string file in Directory.GetFiles(temp)) File.Delete(file);
            Directory.Delete(temp);
        }
    }
}
