using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using ValheimServerTweaks;

internal static class AudioTests
{
    private static void Assert(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS: " + label);
    }
    private static object Call(string patch, string handler, object[] args) =>
        typeof(AudioReplacements).GetNestedType(patch, BindingFlags.NonPublic)
            .GetMethod(handler, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    internal static void Run()
    {
        Plugin.IsHeadless = false;
        var original = new AudioClip { name = "troll_idle" };
        var other = new AudioClip { name = "troll_attack" };
        var custom = new AudioClip { name = "troll_idle" };
        var effects = (Dictionary<string, AudioClip>)typeof(AudioReplacements).GetField("Effects", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        effects.Add("TROLL_IDLE", custom);
        var clips = new[] { original, other };
        var effect = new ZSFX { m_audioClips = clips };
        var args = new object[] { effect, null };
        Call("ZSFX_Play", "Prefix", args);
        Assert(effect.m_audioClips[0] == custom && effect.m_audioClips[1] == other && clips[0] == original,
            "only matching SFX clip is replaced, case insensitively, without mutating original array");
        Call("ZSFX_Play", "Finalizer", args);
        Assert(effect.m_audioClips == clips, "original SFX array restored after playback or exception");
        Plugin.IsHeadless = true;
        args = new object[] { effect, null };
        Call("ZSFX_Play", "Prefix", args);
        Assert(effect.m_audioClips == clips && args[1] == null, "dedicated server skips audio replacement");
        Plugin.IsHeadless = false;
        Plugin.AudioEnabled.Value = false;
        Call("ZSFX_Play", "Prefix", args);
        Assert(effect.m_audioClips == clips, "disabled audio preserves game sounds");
        Plugin.AudioEnabled.Value = true;
        var music = (Dictionary<string, AudioClip>)typeof(AudioReplacements).GetField("Music", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var customTrack = new AudioClip { name = "forest", samples = 50 };
        music.Add("FOREST", customTrack);
        var track = new MusicMan.NamedMusic { m_name = "forest", m_clips = clips, m_savedPlaybackPos = 500 };
        args = new object[] { track, null };
        Call("MusicMan_UpdateMusic", "Prefix", args);
        Assert(track.m_clips.Length == 1 && track.m_clips[0] == customTrack && track.m_savedPlaybackPos == 49,
            "named music replaced and resume offset clamped for shorter replacement");
        Call("MusicMan_UpdateMusic", "Finalizer", new[] { args[1] });
        Assert(track.m_clips == clips, "original music catalogue restored after track selection");
        args = new object[] { new MusicMan.NamedMusic { m_name = "unmapped", m_clips = clips }, null };
        Call("MusicMan_UpdateMusic", "Prefix", args);
        Assert(args[1] == null, "unmapped music uses original soundtrack");

        var musicClips = (Dictionary<string, AudioClip>)typeof(AudioReplacements).GetField("MusicClips", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        musicClips.Add("TROLL_IDLE", customTrack);
        var perClipTrack = new MusicMan.NamedMusic { m_name = "per-clip", m_clips = clips, m_savedPlaybackPos = 100 };
        args = new object[] { perClipTrack, null };
        Call("MusicMan_UpdateMusic", "Prefix", args);
        Assert(perClipTrack.m_clips[0] == customTrack && perClipTrack.m_clips[1] == other && clips[0] == original,
            "individual music file replaced without replacing other tracks in its group");
        Call("MusicMan_UpdateMusic", "Finalizer", new[] { args[1] });
        Assert(perClipTrack.m_clips == clips, "individual music replacement restores catalogue after selection");
        track.m_clips = clips;
        args = new object[] { track, null };
        Call("MusicMan_UpdateMusic", "Prefix", args);
        Assert(track.m_clips.Length == 1 && track.m_clips[0] == customTrack, "whole-group override takes priority over per-file music replacement");
        Call("MusicMan_UpdateMusic", "Finalizer", new[] { args[1] });

        var fader = new Character { m_name = "$enemy_fader", gameObject = new GameObject { name = "Fader(Clone)" } };
        var rename = typeof(BossNames).GetNestedType("Character_GetHoverName", BindingFlags.NonPublic)
            .GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
        args = new object[] { fader, "Fader" };
        rename.Invoke(null, args);
        Assert((string)args[1] == "Jimpan" && fader.m_name == "$enemy_fader" && fader.gameObject.name == "Fader(Clone)",
            "Fader display renamed to Jimpan without changing internal boss identifiers");
        args = new object[] { new Character { m_name = "$enemy_neck", gameObject = new GameObject { name = "Neck(Clone)" } }, "Neck" };
        rename.Invoke(null, args);
        Assert((string)args[1] == "Neck", "other creature names unchanged");
    }
}
