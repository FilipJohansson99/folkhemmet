using System;
using System.Collections;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Video;

namespace ValheimServerTweaks
{
    internal static class IntroVideo
    {
        private sealed class Playback
        {
            internal VideoPlayer Player;
            internal VideoClip OriginalClip;
            internal VideoAspectRatio Aspect;
            internal float StartedAt;
        }
        private static Playback active;
        private static VideoPlayer lastCustomPlayer;
        private static string VideoPath => Path.Combine(Plugin.PluginDir, "videos", "intro.mp4");
        private static bool Available => !Plugin.IsHeadless && Plugin.IntroEnabled.Value && File.Exists(VideoPath);

        // Let the game's own cinematic code manage its UI, cursor, cameras and
        // pause state; change only the video source after that setup has completed.
        [HarmonyPatch(typeof(CinematicsManager), nameof(CinematicsManager.Play),
            new[] { typeof(CinematicsManager.VideoEntry), typeof(CinematicsManager.VideoCompleteAction) })]
        private static class Play
        {
            private static void Postfix(CinematicsManager.VideoEntry video, bool __result)
            {
                if (!__result || !Available || video == null || !video.m_settings.HasFlag(CinematicsManager.Settings.Intro)) return;
                CinematicsManager manager = CinematicsManager.s_instance;
                if (manager == null || manager.m_videoPlayer == null) return;
                VideoPlayer player = manager.m_videoPlayer;
                lastCustomPlayer = player;
                active = new Playback { Player = player, OriginalClip = player.clip, Aspect = player.aspectRatio,
                    StartedAt = Time.realtimeSinceStartup };
                try
                {
                    player.Stop();
                    player.source = VideoSource.Url;
                    player.url = new Uri(Path.GetFullPath(VideoPath)).AbsoluteUri;
                    player.aspectRatio = VideoAspectRatio.FitInside;
                    player.isLooping = false;
                    player.loopPointReached += Finished;
                    // The replacement does not use the original story's subtitles.
                    Traverse.Create(manager).Field("m_subtitleQueue").SetValue(null);
                    Traverse.Create(manager).Field("m_activeSubtitle").SetValue(null);
                    player.Play();
                    Plugin.Log.LogInfo("Playing custom startup intro: " + VideoPath);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Custom intro could not start: " + e.Message);
                    CinematicsManager.Stop();
                }
            }
        }

        private static void Finished(VideoPlayer source)
        {
            if (active != null && active.Player == source) CinematicsManager.Stop();
        }

        // Vanilla's Stop only cleans up when VideoPlayer.clip is non-null, which
        // is not the case for URL playback. Restore its clip before its normal stop.
        [HarmonyPatch(typeof(CinematicsManager), nameof(CinematicsManager.Stop))]
        private static class Stop
        {
            private static void Prefix()
            {
                Playback playback = active;
                active = null;
                if (playback == null || playback.Player == null) return;
                playback.Player.loopPointReached -= Finished;
                playback.Player.Stop();
                playback.Player.source = VideoSource.VideoClip;
                playback.Player.clip = playback.OriginalClip;
                playback.Player.aspectRatio = playback.Aspect;
            }
        }

        [HarmonyPatch(typeof(CinematicsManager), "OnPlaybackError")]
        private static class PlaybackError
        {
            private static bool Prefix(VideoPlayer source, string message)
            {
                if (active == null || active.Player != source)
                {
                    // A cancelled URL can report a late decoder error after cleanup.
                    // There is no remaining cinematic to stop or clip to name then.
                    if (source == lastCustomPlayer && source != null && source.clip == null) return false;
                    return true;
                }
                // Vanilla's error logger assumes source.clip exists, so bypass it
                // for the custom URL and return to the menu through normal cleanup.
                Plugin.Log.LogWarning("Custom intro playback failed: " + message);
                CinematicsManager.Stop();
                return false;
            }
        }

        [HarmonyPatch(typeof(CinematicsManager), "Update")]
        private static class Update
        {
            private static void Postfix()
            {
                if (active != null && active.Player != null && !active.Player.isPlaying && active.Player.frame <= 0 &&
                    Time.realtimeSinceStartup - active.StartedAt > 15f)
                {
                    Plugin.Log.LogWarning("Custom intro did not start within 15 seconds; returning to menu.");
                    CinematicsManager.Stop();
                }
            }
        }

        [HarmonyPatch(typeof(CinematicsManager), nameof(CinematicsManager.GetMusic))]
        private static class Music
        {
            private static void Postfix(ref string __result) { if (active != null) __result = ""; }
        }

        [HarmonyPatch(typeof(CinematicsManager), "OnDestroy")]
        private static class Destroy
        {
            private static void Postfix(CinematicsManager __instance)
            {
                if (lastCustomPlayer == __instance.m_videoPlayer) lastCustomPlayer = null;
                if (active == null || active.Player != __instance.m_videoPlayer) return;
                if (active.Player != null) active.Player.loopPointReached -= Finished;
                active = null;
            }
        }

        [HarmonyPatch(typeof(FejdStartup), "TryPlayIntroCinematic")]
        private static class Startup
        {
            private static void Postfix(ref IEnumerator __result)
            {
                if (Available && Plugin.ForceStartupIntro.Value && !Game.m_hasStartedOnce)
                    __result = WithIntroEnabled(__result);
            }
        }

        private static IEnumerator WithIntroEnabled(IEnumerator original)
        {
            try
            {
                bool first = true;
                while (true)
                {
                    bool moved;
                    if (first)
                    {
                        first = false;
                        bool skipped = PlatformPrefs.GetBool("SkipIntroCinematic");
                        if (skipped) PlatformPrefs.SetBool("SkipIntroCinematic", false);
                        try { moved = original.MoveNext(); }
                        finally { if (skipped) PlatformPrefs.SetBool("SkipIntroCinematic", true); }
                    }
                    else moved = original.MoveNext();
                    if (!moved) yield break;
                    yield return original.Current;
                }
            }
            finally { (original as IDisposable)?.Dispose(); }
        }
    }
}
