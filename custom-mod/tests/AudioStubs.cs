using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class AudioClip { public string name; public int samples = 100; }
    public class GameObject { public string name; }
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
        public object SendWebRequest() => null;
        public void Dispose() { }
    }
    public static class UnityWebRequestMultimedia
    {
        public static UnityWebRequest GetAudioClip(string uri, UnityEngine.AudioType format) => new UnityWebRequest();
    }
    public static class DownloadHandlerAudioClip
    {
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
