using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class AudioClip { public string name; public int samples = 100; }
    public class Transform { public Vector3 position; public void SetParent(Transform parent) { } }
    public class GameObject
    {
        public string name;
        public Transform transform = new Transform();
        public GameObject(string value = "") { name = value; }
        public T AddComponent<T>() where T : new() => new T();
    }
    public enum AudioRolloffMode { Linear }
    public class AudioSource
    {
        public static int Plays;
        public bool playOnAwake, loop, bypassReverbZones, isPlaying;
        public float spatialBlend, minDistance, maxDistance, dopplerLevel, volume;
        public AudioRolloffMode rolloffMode;
        public object outputAudioMixerGroup;
        public GameObject gameObject = new GameObject();
        public Transform transform => gameObject.transform;
        public AudioClip clip;
        public void Play() { isPlaying = true; Plays++; }
        public void Stop() => isPlaying = false;
    }
    public static class Object { public static void Destroy(object value) { } }
    public enum AudioType { WAV, OGGVORBIS, MPEG }
    public static class Mathf { public static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max); }
    public static class Resources { public static T[] FindObjectsOfTypeAll<T>() => Array.Empty<T>(); }
}
namespace UnityEngine.Networking
{
    public class UnityWebRequest : IDisposable
    {
        public enum Result { Success, ConnectionError }
        public int timeout;
        public Result result = Result.Success;
        public string error;
        public object downloadHandler = new DownloadHandlerAudioClip();
        public object SendWebRequest() => null;
        public void Dispose() { }
    }
    public static class UnityWebRequestMultimedia
    {
        public static UnityWebRequest GetAudioClip(string uri, UnityEngine.AudioType format) => new UnityWebRequest();
    }
    public class DownloadHandlerAudioClip
    {
        public bool streamAudio;
        public static UnityEngine.AudioClip GetContent(UnityWebRequest request) => new UnityEngine.AudioClip();
    }
}
public class ZSFX
{
    public string name;
    public UnityEngine.AudioClip[] m_audioClips;
    public void Play() { }
}
public class Character
{
    public string m_name;
    public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
    public string GetHoverName() => m_name;
    public UnityEngine.Transform transform => gameObject.transform;
    public bool Dead;
    public bool IsDead() => Dead;
}
public class BaseAI
{
    public static List<BaseAI> Instances = new List<BaseAI>();
    public Character Character = new Character();
    public bool Alerted;
    public UnityEngine.GameObject gameObject => Character.gameObject;
    public bool IsAlerted() => Alerted;
    public static List<BaseAI> GetAllInstances() => Instances;
    public T GetComponent<T>() => (T)(object)Character;
}
public class AudioMan
{
    public static AudioMan instance = new AudioMan();
    public object m_ambientMixer = new object();
}
public class MusicMan
{
    public List<NamedMusic> m_music = new List<NamedMusic>();
    public class NamedMusic
    {
        public string m_name;
        public UnityEngine.AudioClip[] m_clips;
        public int m_savedPlaybackPos;
    }
}
public class Terminal
{
    public void AddString(string value) { }
    public class ConsoleCommand { public ConsoleCommand(string name, string help, Action<Args> action) { } }
    public class Args
    {
        public int Length;
        public Terminal Context;
        public string this[int index] => "";
    }
}
