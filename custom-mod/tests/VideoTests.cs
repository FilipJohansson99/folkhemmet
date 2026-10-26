using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Video;
using ValheimServerTweaks;

internal static class VideoTests
{
    internal static object Hook(string patch, string method, params object[] args) =>
        typeof(IntroVideo).GetNestedType(patch, BindingFlags.NonPublic).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    private static void Assert(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS: " + label); }
    private static bool observedSkip;
    private static IEnumerator OriginalStartup() { observedSkip = PlatformPrefs.GetBool("SkipIntroCinematic"); yield return null; }
    private static CinematicsManager NewManager()
    {
        var manager = new CinematicsManager { m_videoPlayer = new VideoPlayer { clip = new VideoClip(), aspectRatio = VideoAspectRatio.Stretch },
            m_subtitleQueue = new object(), m_activeSubtitle = new object() };
        CinematicsManager.s_instance = manager;
        return manager;
    }
    internal static void Run()
    {
        Plugin.IsHeadless = false;
        string oldDir = Plugin.PluginDir;
        string temp = Path.Combine(Path.GetTempPath(), "folkhemmet-video-test-" + Guid.NewGuid());
        string videos = Path.Combine(temp, "videos");
        Directory.CreateDirectory(videos);
        Plugin.PluginDir = temp;
        var intro = new CinematicsManager.VideoEntry { m_settings = CinematicsManager.Settings.Intro };
        try
        {
            var manager = NewManager();
            Hook("Play", "Postfix", intro, true);
            Assert(manager.m_videoPlayer.source == VideoSource.VideoClip, "missing custom video preserves original cinematic");
            File.WriteAllText(Path.Combine(videos, "intro.mp4"), "test fixture; codec verified separately on real MP4");
            PlatformPrefs.Skip = true;
            var args = new object[] { OriginalStartup() };
            Hook("Startup", "Postfix", args);
            ((IEnumerator)args[0]).MoveNext();
            Assert(!observedSkip && PlatformPrefs.Skip, "custom startup intro bypasses skip while preserving saved preference");
            ((IDisposable)args[0]).Dispose();
            Hook("Play", "Postfix", intro, true);
            Assert(manager.m_videoPlayer.source == VideoSource.Url && manager.m_videoPlayer.isPlaying &&
                manager.m_videoPlayer.aspectRatio == VideoAspectRatio.FitInside && manager.m_subtitleQueue == null,
                "startup switches to local MP4, fits portrait aspect and removes original subtitles");
            args = new object[] { "original soundtrack" };
            Hook("Music", "Postfix", args);
            Assert((string)args[0] == "", "custom intro does not inherit original story soundtrack");
            manager.m_videoPlayer.Finish();
            Assert(manager.CleanupCount == 1 && manager.m_videoPlayer.aspectRatio == VideoAspectRatio.Stretch,
                "natural video end reaches vanilla menu cleanup despite URL clip-null state");
            manager = NewManager();
            Hook("Play", "Postfix", intro, true);
            CinematicsManager.Stop();
            manager.m_videoPlayer.Finish();
            Assert(manager.CleanupCount == 1, "skipping restores menu and detaches completion handler");
            manager = NewManager();
            Hook("Play", "Postfix", intro, true);
            Plugin.AllowWarnings = true;
            bool runOriginal = (bool)Hook("PlaybackError", "Prefix", manager.m_videoPlayer, "decode failed");
            Assert(!runOriginal && manager.CleanupCount == 1, "URL decoder error avoids native null-clip logger and restores menu");
            runOriginal = (bool)Hook("PlaybackError", "Prefix", manager.m_videoPlayer, "late cancellation event");
            Assert(!runOriginal && manager.CleanupCount == 1, "late URL error after cleanup does not access a null native clip");
            manager = NewManager();
            Hook("Play", "Postfix", intro, true);
            manager.m_videoPlayer.isPlaying = false;
            Time.realtimeSinceStartup += 16;
            Hook("Update", "Postfix");
            Assert(manager.CleanupCount == 1, "failed video preparation times out to menu");
            manager = NewManager();
            Hook("Play", "Postfix", new CinematicsManager.VideoEntry { m_settings = CinematicsManager.Settings.Outro }, true);
            Assert(manager.m_videoPlayer.source == VideoSource.VideoClip, "other cinematics retain original playback");
        }
        finally
        {
            Plugin.AllowWarnings = false;
            Plugin.PluginDir = oldDir;
            File.Delete(Path.Combine(videos, "intro.mp4"));
            Directory.Delete(videos);
            Directory.Delete(temp);
        }
    }
}
