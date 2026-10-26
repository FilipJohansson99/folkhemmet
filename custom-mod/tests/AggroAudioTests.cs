using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using ValheimServerTweaks;

internal static class AggroAudioTests
{
    private static void Assert(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS: " + label);
    }
    private static AudioSource Source => (AudioSource)typeof(AggroAudio).GetField("source", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static void Tick() { Time.unscaledTime += 1; AggroAudio.Tick(); }
    internal static void Run()
    {
        var clips = (Dictionary<string, AudioClip>)typeof(AudioReplacements).GetField("Encounters", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        clips.Add("serpent_aggro", new AudioClip { name = "song" });
        var serpent = new BaseAI();
        serpent.Character.gameObject.name = "Serpent(Clone)";
        serpent.Character.transform.position = new Vector3(20, 0, 0);
        BaseAI.Instances.Add(serpent);
        Tick();
        Assert(Source == null, "idle serpent does not start encounter song");
        serpent.Alerted = true;
        Tick();
        Assert(Source.isPlaying && AudioSource.Plays == 1 && Source.loop && Source.spatialBlend == 1 && Source.outputAudioMixerGroup == AudioMan.instance.m_ambientMixer,
            "aggro serpent starts one spatial looping source through the game SFX mixer");
        var source = Source;
        var bonemaw = new BaseAI { Alerted = true };
        bonemaw.Character.gameObject.name = "BonemawSerpent(Clone)";
        bonemaw.Character.transform.position = new Vector3(10, 0, 0);
        BaseAI.Instances.Add(bonemaw);
        Tick();
        Assert(Source == source && AudioSource.Plays == 1 && source.transform.position.x == 10, "multiple serpents share one uninterrupted song following nearest aggro mob");
        serpent.Character.Dead = true;
        bonemaw.Character.Dead = true;
        Tick();
        Assert(!source.isPlaying, "song stops when nearby serpents die");
        bonemaw.Character.Dead = false;
        Tick();
        Assert(source.isPlaying && AudioSource.Plays == 2, "new encounter restarts song");
        bonemaw.Alerted = false;
        Tick();
        Assert(!source.isPlaying, "losing aggro stops song");
        bonemaw.Alerted = true;
        bonemaw.Character.transform.position = new Vector3(101, 0, 0);
        Tick();
        Assert(!source.isPlaying, "out of range aggro does not play song");
        bonemaw.Character.transform.position = new Vector3(10, 0, 0);
        bonemaw.Character.gameObject.name = "BonemawSerpentOther";
        Tick();
        Assert(!source.isPlaying, "unrelated creature names do not trigger encounter song");
        bonemaw.Character.gameObject.name = "BonemawSerpent";
        Tick();
        Plugin.AudioEnabled.Value = false;
        AggroAudio.Tick();
        Assert(!source.isPlaying, "disabling custom audio stops song immediately");
        Plugin.AudioEnabled.Value = true;
        Tick();
        Player.m_localPlayer.Dead = true;
        AggroAudio.Tick();
        Assert(!source.isPlaying, "player death stops song immediately");
        Player.m_localPlayer.Dead = false;
        Tick();
        Player.m_localPlayer = null;
        AggroAudio.Tick();
        Assert(!source.isPlaying, "logout stops song immediately");
        Player.m_localPlayer = new Player();
        AggroAudio.Cleanup();
        Plugin.IsHeadless = true;
        Tick();
        Assert(Source == null, "dedicated server never creates an audio source");
        Plugin.IsHeadless = false;
        BaseAI.Instances.Clear();
        clips.Clear();
    }
}
