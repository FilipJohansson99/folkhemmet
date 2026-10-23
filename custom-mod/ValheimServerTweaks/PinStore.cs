using System;
using System.Collections.Generic;
using System.IO;

namespace ValheimServerTweaks
{
    // Independent from Unity so persistence can be tested without starting a game.
    internal sealed class SavedPin
    {
        internal string Name;
        internal float X, Y, Z;
        internal int Type;
        internal bool Checked;
    }

    internal static class PinStore
    {
        private const int Magic = 0x46504E31; // FPN1
        internal static List<SavedPin> Load(string path)
        {
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return null;
            try { return Read(path); }
            catch (Exception) { return Read(path + ".bak"); }
        }

        private static List<SavedPin> Read(string path)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                if (reader.ReadInt32() != Magic) throw new InvalidDataException("Unknown pin save format.");
                int count = reader.ReadInt32();
                if (count < 0 || count > 50000) throw new InvalidDataException("Invalid pin count.");
                var pins = new List<SavedPin>(count);
                for (int i = 0; i < count; i++)
                {
                    var pin = new SavedPin { Name = reader.ReadString(), X = reader.ReadSingle(), Y = reader.ReadSingle(),
                        Z = reader.ReadSingle(), Type = reader.ReadInt32(), Checked = reader.ReadBoolean() };
                    if (pin.Name.Length > 4096 || pin.Type < 0 || pin.Type > 100 ||
                        float.IsNaN(pin.X) || float.IsInfinity(pin.X) || float.IsNaN(pin.Y) || float.IsInfinity(pin.Y) ||
                        float.IsNaN(pin.Z) || float.IsInfinity(pin.Z)) throw new InvalidDataException("Invalid pin data.");
                    pins.Add(pin);
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing pin data.");
                return pins;
            }
        }

        internal static void Save(string path, IList<SavedPin> pins)
        {
            string temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                {
                    writer.Write(Magic);
                    writer.Write(pins.Count);
                    foreach (SavedPin pin in pins)
                    {
                        writer.Write(pin.Name ?? "");
                        writer.Write(pin.X); writer.Write(pin.Y); writer.Write(pin.Z);
                        writer.Write(pin.Type); writer.Write(pin.Checked);
                    }
                    writer.Flush();
                }
                stream.Flush(true);
            }
            if (File.Exists(path))
            {
                bool valid = false;
                try { Read(path); valid = true; } catch (Exception) { }
                if (valid) File.Replace(temporary, path, path + ".bak");
                else
                {
                    // A recovered backup must not be replaced with the corrupt primary.
                    File.Move(path, path + ".corrupt-" + DateTime.UtcNow.Ticks);
                    File.Move(temporary, path);
                }
            }
            else File.Move(temporary, path);
        }
    }
}
