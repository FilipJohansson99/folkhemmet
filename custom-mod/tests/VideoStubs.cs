using System;
using System.Reflection;
using ValheimServerTweaks;

namespace HarmonyLib
{
    public class Traverse
    {
        private object instance;
        private string field;
        public static Traverse Create(object value) => new Traverse { instance = value };
        public Traverse Field(string value) { field = value; return this; }
        public void SetValue(object value) => instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(instance, value);
    }
}
namespace UnityEngine.Video
{
    public class VideoClip { }
    public enum VideoSource { VideoClip, Url }
    public enum VideoAspectRatio { Stretch, FitInside }
    public class VideoPlayer
    {
        public VideoClip clip;
        public VideoSource source;
        public string url;
        public VideoAspectRatio aspectRatio;
        public bool isLooping, isPlaying;
        public long frame;
        public event Action<VideoPlayer> loopPointReached;
        public void Play() { isPlaying = true; if (source == VideoSource.Url) clip = null; }
        public void Stop() => isPlaying = false;
        public void Finish() { isPlaying = false; frame = 100; loopPointReached?.Invoke(this); }
    }
}
public class CinematicsManager
{
    public static CinematicsManager s_instance;
    public UnityEngine.Video.VideoPlayer m_videoPlayer;
    public object m_subtitleQueue, m_activeSubtitle;
    public int CleanupCount;
    [Flags] public enum Settings { Intro = 2, Outro = 4 }
    public class VideoEntry { public Settings m_settings; }
    public delegate void VideoCompleteAction(VideoEntry entry, bool watchedToEnd);
    public static bool Play(VideoEntry entry, VideoCompleteAction onStop = null) => true;
    public static string GetMusic() => "original";
    public static void Stop()
    {
        VideoTests.Hook("Stop", "Prefix");
        // The actual game has this clip-null guard, so the test must include it.
        if (s_instance.m_videoPlayer.clip == null) return;
        s_instance.CleanupCount++;
        s_instance.m_videoPlayer.Stop();
        s_instance.m_videoPlayer.clip = null;
    }
}
public class FejdStartup { }
public static class Game { public static bool m_hasStartedOnce; }
public static class PlatformPrefs
{
    public static bool Skip;
    public static bool GetBool(string key) => Skip;
    public static void SetBool(string key, bool value) { Skip = value; }
}
