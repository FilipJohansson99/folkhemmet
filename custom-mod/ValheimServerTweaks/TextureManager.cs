using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ValheimServerTweaks
{
    /// <summary>
    /// Replaces textures on prefabs with PNG/JPG files from disk.
    ///
    ///   textures\Troll\TrollMaterial.png            -> _MainTex of material "TrollMaterial" on prefab "Troll"
    ///   textures\VikingShip\sail._BumpMap.png        -> _BumpMap of material "sail" on prefab "VikingShip"
    ///
    /// Use the console commands to find names:
    ///   tex_find troll      list prefab names containing "troll"
    ///   tex_dump Troll      save the original textures + a materials.txt listing for that prefab
    ///   tex_reload          re-read the texture folder without restarting
    /// </summary>
    internal static class TextureManager
    {
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };

        // (material, property) -> original texture, so a reload can restore removed replacements.
        private static readonly Dictionary<Material, Dictionary<string, Texture>> s_originals =
            new Dictionary<Material, Dictionary<string, Texture>>();

        private static readonly List<Texture2D> s_loaded = new List<Texture2D>();

        internal static string TextureRoot =>
            string.IsNullOrWhiteSpace(Plugin.TextureFolder.Value)
                ? Path.Combine(Plugin.PluginDir, "textures")
                : Plugin.TextureFolder.Value.Trim();

        internal static string DumpRoot =>
            string.IsNullOrWhiteSpace(Plugin.DumpFolder.Value)
                ? Path.Combine(Plugin.PluginDir, "texture-dumps")
                : Plugin.DumpFolder.Value.Trim();

        // ------------------------------------------------------------------ hooks

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ZNetScene_Awake
        {
            // Run after other mods (Jotunn etc.) have registered their prefabs.
            [HarmonyPriority(Priority.Last)]
            private static void Postfix()
            {
                if (Plugin.IsHeadless || !Plugin.TexturesEnabled.Value) return;
                try { ApplyAll(null); }
                catch (Exception e) { Plugin.Log.LogError("Applying textures failed: " + e); }
            }
        }

        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        private static class Terminal_InitTerminal
        {
            private static void Postfix()
            {
                if (Plugin.IsHeadless) return;

                new Terminal.ConsoleCommand("tex_reload", "Reload custom textures from disk",
                    args =>
                    {
                        int n = ApplyAll(args.Context);
                        args.Context.AddString($"Custom textures reloaded: {n} replacement(s) applied.");
                    });

                new Terminal.ConsoleCommand("tex_dump", "[prefab] Save the textures of a prefab as PNGs (plus materials.txt)",
                    args =>
                    {
                        if (args.Length < 2) { args.Context.AddString("Usage: tex_dump <PrefabName>   (find names with tex_find)"); return; }
                        Dump(args.Args[1], args.Context);
                    },
                    optionsFetcher: () => ZNetScene.instance != null
                        ? ZNetScene.instance.m_prefabs.Where(p => p != null).Select(p => p.name).ToList()
                        : new List<string>());

                new Terminal.ConsoleCommand("tex_find", "[text] List prefab names containing the text",
                    args =>
                    {
                        if (args.Length < 2) { args.Context.AddString("Usage: tex_find <text>   e.g. tex_find troll"); return; }
                        Find(args.ArgsAll, args.Context);
                    });
            }
        }

        // ------------------------------------------------------------------ apply

        /// <summary>Restore everything, then apply every file in the texture folder. Returns number of replacements.</summary>
        internal static int ApplyAll(Terminal ctx)
        {
            RestoreOriginals();

            string root = TextureRoot;
            if (!Directory.Exists(root))
            {
                Plugin.Log.LogInfo($"No texture folder at {root} (nothing to replace).");
                return 0;
            }

            int applied = 0;
            foreach (string prefabDir in Directory.GetDirectories(root))
            {
                string prefabName = Path.GetFileName(prefabDir);
                if (prefabName.StartsWith("_") || prefabName.StartsWith(".")) continue; // _examples, _notes ...

                var files = Directory.GetFiles(prefabDir)
                    .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();
                if (files.Count == 0) continue;

                GameObject prefab = FindPrefab(prefabName);
                if (prefab == null)
                {
                    Report(ctx, $"[textures] Prefab '{prefabName}' not found (folder {prefabDir}). Use tex_find to get the exact name.", true);
                    continue;
                }

                List<Material> materials = GetMaterials(prefab);
                foreach (string file in files)
                {
                    ParseFileName(Path.GetFileNameWithoutExtension(file), out string materialName, out string property);
                    var targets = materials.Where(m => SafeName(CleanName(m.name)).Equals(materialName, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (targets.Count == 0)
                    {
                        string known = string.Join(", ", materials.Select(m => SafeName(CleanName(m.name))).Distinct().ToArray());
                        Report(ctx, $"[textures] {prefabName}: no material named '{materialName}' (file {Path.GetFileName(file)}). Materials: {known}", true);
                        continue;
                    }

                    Texture2D tex = null;
                    foreach (Material mat in targets)
                    {
                        if (!mat.HasProperty(property))
                        {
                            Report(ctx, $"[textures] {prefabName}/{materialName}: shader '{mat.shader.name}' has no property {property}.", true);
                            continue;
                        }
                        Texture original = mat.GetTexture(property);
                        if (tex == null)
                        {
                            tex = LoadTexture(file, IsLinearProperty(property), original);
                            if (tex == null) break;
                        }
                        RememberOriginal(mat, property, original);
                        mat.SetTexture(property, tex);
                        applied++;
                    }
                }
            }

            Plugin.Log.LogInfo($"Custom textures: {applied} replacement(s) applied from {root}.");
            return applied;
        }

        private static void RememberOriginal(Material mat, string property, Texture original)
        {
            if (!s_originals.TryGetValue(mat, out var props))
            {
                props = new Dictionary<string, Texture>();
                s_originals[mat] = props;
            }
            if (!props.ContainsKey(property)) props[property] = original;
        }

        private static void RestoreOriginals()
        {
            foreach (var kv in s_originals)
            {
                if (kv.Key == null) continue;
                foreach (var prop in kv.Value) kv.Key.SetTexture(prop.Key, prop.Value);
            }
            s_originals.Clear();
            foreach (Texture2D t in s_loaded)
            {
                if (t != null) UnityEngine.Object.Destroy(t);
            }
            s_loaded.Clear();
        }

        private static Texture2D LoadTexture(string file, bool linear, Texture original)
        {
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
                if (!tex.LoadImage(File.ReadAllBytes(file), false))
                {
                    Plugin.Log.LogWarning("Could not decode image " + file);
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }
                tex.name = "custom_" + Path.GetFileNameWithoutExtension(file);
                if (original != null)
                {
                    tex.wrapMode = original.wrapMode;
                    tex.filterMode = original.filterMode;
                    tex.anisoLevel = original.anisoLevel;
                }
                tex.Apply(true, true); // build mipmaps, free the CPU copy
                s_loaded.Add(tex);
                return tex;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not load {file}: {e.Message}");
                return null;
            }
        }

        // ------------------------------------------------------------------ dump / find

        internal static void Dump(string prefabName, Terminal ctx)
        {
            GameObject prefab = FindPrefab(prefabName);
            if (prefab == null)
            {
                ctx?.AddString($"Prefab '{prefabName}' not found. Try: tex_find {prefabName}");
                return;
            }

            string outDir = Path.Combine(DumpRoot, prefab.name);
            Directory.CreateDirectory(outDir);

            var sb = new StringBuilder();
            sb.AppendLine($"Prefab: {prefab.name}");
            sb.AppendLine("To replace a texture, copy its PNG into textures\\" + prefab.name + "\\ (same file name), edit it, then run tex_reload.");
            sb.AppendLine("File name = <material>.png for the main texture, <material>.<property>.png for the others.");
            sb.AppendLine();

            var done = new HashSet<string>();
            int saved = 0;
            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                sb.AppendLine($"{GetPath(r.transform, prefab.transform)}  [{r.GetType().Name}]");
                foreach (Material mat in r.sharedMaterials)
                {
                    if (mat == null) continue;
                    string matName = SafeName(CleanName(mat.name));
                    sb.AppendLine($"    material: {matName}   shader: {mat.shader.name}");
                    foreach (string prop in mat.GetTexturePropertyNames())
                    {
                        Texture t = mat.GetTexture(prop);
                        if (t == null) continue;
                        string fileName = BuildFileName(matName, prop) + ".png";
                        sb.AppendLine($"        {prop,-22} {t.name} ({t.width}x{t.height})  ->  {fileName}");
                        if (done.Add(fileName) && SaveTexture(t, Path.Combine(outDir, fileName))) saved++;
                    }
                }
            }

            File.WriteAllText(Path.Combine(outDir, "materials.txt"), sb.ToString());
            string msg = $"Dumped {saved} texture(s) of {prefab.name} to {outDir}";
            Plugin.Log.LogInfo(msg);
            ctx?.AddString(msg);
        }

        internal static void Find(string text, Terminal ctx)
        {
            if (ZNetScene.instance == null) { ctx.AddString("Join a world first."); return; }
            text = text.Trim();
            var names = ZNetScene.instance.m_prefabs
                .Where(p => p != null && p.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(p => p.name).Distinct().OrderBy(n => n).ToList();
            ctx.AddString(names.Count == 0 ? "No prefabs found." : $"{names.Count} prefab(s): " + string.Join(", ", names.Take(80).ToArray()));
        }

        private static bool SaveTexture(Texture tex, string path)
        {
            if (tex.dimension != UnityEngine.Rendering.TextureDimension.Tex2D) return false;
            RenderTexture prev = RenderTexture.active;
            RenderTexture rt = null;
            Texture2D copy = null;
            try
            {
                bool linear = !tex.isDataSRGB;
                rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32,
                    linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, linear);
                copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                copy.Apply();
                File.WriteAllBytes(path, copy.EncodeToPNG());
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not save {tex.name}: {e.Message}");
                return false;
            }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (copy != null) UnityEngine.Object.Destroy(copy);
            }
        }

        // ------------------------------------------------------------------ helpers

        internal static GameObject FindPrefab(string name)
        {
            GameObject go = null;
            if (ZNetScene.instance != null)
            {
                go = ZNetScene.instance.GetPrefab(name);
                if (go == null)
                    go = ZNetScene.instance.m_prefabs.FirstOrDefault(p => p != null && p.name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
            if (go == null && ObjectDB.instance != null) go = ObjectDB.instance.GetItemPrefab(name);
            return go;
        }

        private static List<Material> GetMaterials(GameObject prefab)
        {
            var list = new List<Material>();
            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (m != null && !list.Contains(m)) list.Add(m);
                }
            }
            return list;
        }

        /// <summary>"sail._BumpMap" -> ("sail", "_BumpMap"); "sail" -> ("sail", "_MainTex").</summary>
        private static void ParseFileName(string baseName, out string material, out string property)
        {
            int i = baseName.LastIndexOf("._", StringComparison.Ordinal);
            if (i > 0)
            {
                material = baseName.Substring(0, i);
                property = baseName.Substring(i + 1);
            }
            else
            {
                material = baseName;
                property = "_MainTex";
            }
        }

        private static string BuildFileName(string material, string property) =>
            property == "_MainTex" ? material : material + "." + property;

        private static bool IsLinearProperty(string property)
        {
            string p = property.ToLowerInvariant();
            return p.Contains("bump") || p.Contains("normal") || p.Contains("metallic") || p.Contains("mask") || p.Contains("occlusion");
        }

        internal static string CleanName(string name) => name.Replace(" (Instance)", "").Trim();

        internal static string SafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private static string GetPath(Transform t, Transform root)
        {
            var parts = new List<string>();
            while (t != null && t != root) { parts.Add(t.name); t = t.parent; }
            parts.Add(root.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static void Report(Terminal ctx, string msg, bool warn)
        {
            if (warn) Plugin.Log.LogWarning(msg); else Plugin.Log.LogInfo(msg);
            ctx?.AddString(msg);
        }
    }
}
