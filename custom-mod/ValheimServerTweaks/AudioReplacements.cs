using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace ValheimServerTweaks
{
    internal static class AudioReplacements
    {
        private static readonly Dictionary<string, AudioClip> Effects = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AudioClip> Music = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

        internal static IEnumerator Load()
        {
            string root = Path.Combine(Plugin.PluginDir, "audio");
            foreach (string kind in new[] { "sfx", "music" })
            {
                string folder = Path.Combine(root, kind);
                if (!Directory.Exists(folder)) continue;
                Dictionary<string, AudioClip> target = kind == "sfx" ? Effects : Music;
                foreach (string file in Directory.GetFiles(folder).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    AudioType format;
                    switch (Path.GetExtension(file).ToLowerInvariant())
                    {
                        case ".wav": format = AudioType.WAV; break;
                        case ".ogg": format = AudioType.OGGVORBIS; break;
                        case ".mp3": format = AudioType.MPEG; break;
                        default: continue;
                    }
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (target.ContainsKey(name))
                    {
                        Plugin.Log.LogWarning($"Duplicate audio replacement '{kind}/{name}'; keeping the first file.");
                        continue;
                    }
                    UnityWebRequest request;
                    try { request = UnityWebRequestMultimedia.GetAudioClip(new Uri(Path.GetFullPath(file)).AbsoluteUri, format); }
                    catch (Exception e) { Plugin.Log.LogWarning($"Could not open audio '{file}': {e.Message}"); continue; }
                    using (request)
                    {
                        request.timeout = 30;
                        yield return request.SendWebRequest();
                        if (request.result != UnityWebRequest.Result.Success)
                        {
                            Plugin.Log.LogWarning($"Could not load audio '{file}': {request.error}; keeping game audio.");
                            continue;
                        }
                        AudioClip clip = null;
                        try { clip = DownloadHandlerAudioClip.GetContent(request); }
                        catch (Exception e) { Plugin.Log.LogWarning($"Could not decode audio '{file}': {e.Message}"); }
                        if (clip == null || clip.samples <= 0) continue;
                        clip.name = name;
                        target.Add(name, clip);
                    }
                }
            }
            Plugin.Log.LogInfo($"Custom audio loaded: {Effects.Count} sound effects, {Music.Count} music tracks.");
        }

        // Only replace the array during selection. Restore even if playback throws,
        // so prefab data, volume, spatial audio and vanilla playback rules survive.
        [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Play))]
        private static class ZSFX_Play
        {
            private static void Prefix(ZSFX __instance, out AudioClip[] __state)
            {
                __state = null;
                if (Plugin.IsHeadless || !Plugin.AudioEnabled.Value || Effects.Count == 0 || __instance.m_audioClips == null) return;
                AudioClip[] replaced = null;
                for (int i = 0; i < __instance.m_audioClips.Length; i++)
                {
                    AudioClip original = __instance.m_audioClips[i];
                    if (original == null || !Effects.TryGetValue(original.name, out AudioClip replacement)) continue;
                    if (replaced == null) replaced = (AudioClip[])__instance.m_audioClips.Clone();
                    replaced[i] = replacement;
                }
                if (replaced == null) return;
                __state = __instance.m_audioClips;
                __instance.m_audioClips = replaced;
            }

            private static void Finalizer(ZSFX __instance, AudioClip[] __state)
            {
                if (__state != null) __instance.m_audioClips = __state;
            }
        }

        private sealed class MusicState
        {
            internal MusicMan.NamedMusic Track;
            internal AudioClip[] OriginalClips;
        }

        [HarmonyPatch(typeof(MusicMan), "UpdateMusic")]
        private static class MusicMan_UpdateMusic
        {
            private static void Prefix(MusicMan.NamedMusic ___m_queuedMusic, out MusicState __state)
            {
                __state = null;
                if (Plugin.IsHeadless || !Plugin.AudioEnabled.Value || ___m_queuedMusic == null ||
                    !Music.TryGetValue(___m_queuedMusic.m_name, out AudioClip replacement)) return;
                __state = new MusicState { Track = ___m_queuedMusic, OriginalClips = ___m_queuedMusic.m_clips };
                ___m_queuedMusic.m_clips = new[] { replacement };
                // A replacement may be shorter than the original track.
                ___m_queuedMusic.m_savedPlaybackPos = Mathf.Clamp(___m_queuedMusic.m_savedPlaybackPos, 0, replacement.samples - 1);
            }

            private static void Finalizer(MusicState __state)
            {
                if (__state != null) __state.Track.m_clips = __state.OriginalClips;
            }
        }

        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        private static class Terminal_InitTerminal
        {
            private static void Postfix()
            {
                if (Plugin.IsHeadless) return;
                new Terminal.ConsoleCommand("audio_find", "[text] Find loaded sound effects and their original clip names",
                    args => ListEffects(args.Length > 1 ? args[1] : "", args.Context));
                new Terminal.ConsoleCommand("audio_music", "[text] List music group names and their original clips",
                    args => ListMusic(args.Length > 1 ? args[1] : "", args.Context));
            }
        }

        private static bool Matches(string name, string query) => (name ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        private static void ListEffects(string query, Terminal terminal)
        {
            var lines = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ZSFX effect in Resources.FindObjectsOfTypeAll<ZSFX>())
            {
                if (effect == null || effect.m_audioClips == null) continue;
                foreach (AudioClip clip in effect.m_audioClips)
                    if (clip != null && (Matches(effect.name, query) || Matches(clip.name, query)))
                        lines.Add($"{effect.name}: {clip.name}");
            }
            foreach (string line in lines.Take(100)) terminal.AddString(line);
            terminal.AddString($"{lines.Count} effect/clip pairs found (showing up to 100). Enter a world first to load mob sounds.");
        }

        private static void ListMusic(string query, Terminal terminal)
        {
            var lines = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MusicMan manager in Resources.FindObjectsOfTypeAll<MusicMan>())
                foreach (MusicMan.NamedMusic track in manager.m_music)
                    if (track != null && (Matches(track.m_name, query) || (track.m_clips ?? new AudioClip[0]).Any(clip => clip != null && Matches(clip.name, query))))
                        lines.Add($"{track.m_name}: {string.Join(", ", (track.m_clips ?? new AudioClip[0]).Where(clip => clip != null).Select(clip => clip.name))}");
            foreach (string line in lines.Take(100)) terminal.AddString(line);
            terminal.AddString($"{lines.Count} music groups found. Use the group name for audio/music/<name>.ogg.");
        }
    }
}
