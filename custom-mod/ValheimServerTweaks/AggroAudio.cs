using System;
using UnityEngine;

namespace ValheimServerTweaks
{
    internal static class AggroAudio
    {
        private const float AudibleRange = 100f;
        private static float nextCheck;
        private static AudioSource source;

        private static bool IsSerpent(string name)
        {
            if (name == null) return false;
            if (name.EndsWith("(Clone)", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 7).TrimEnd();
            return name.Equals("Serpent", StringComparison.OrdinalIgnoreCase) || name.Equals("BonemawSerpent", StringComparison.OrdinalIgnoreCase);
        }

        internal static void Tick()
        {
            if (Plugin.IsHeadless) return;
            Player player = Player.m_localPlayer;
            if (!Plugin.AudioEnabled.Value || player == null || player.IsDead())
            {
                if (source != null && source.isPlaying) source.Stop();
                return;
            }
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.25f;

            Character nearest = null;
            float nearestDistance = AudibleRange * AudibleRange;
            foreach (BaseAI ai in BaseAI.GetAllInstances())
            {
                // IsAlerted also reflects the owner's replicated alert state on
                // other players' clients, so no server mod update is required.
                if (ai == null || !IsSerpent(ai.gameObject.name) || !ai.IsAlerted()) continue;
                Character character = ai.GetComponent<Character>();
                if (character == null || character.IsDead()) continue;
                float distance = (character.transform.position - player.transform.position).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearest = character;
                nearestDistance = distance;
            }

            if (nearest == null || !AudioReplacements.TryGetEncounter("serpent_aggro", out AudioClip clip))
            {
                if (source != null && source.isPlaying) source.Stop();
                return;
            }
            if (source == null)
            {
                var emitter = new GameObject("Folkhemmet serpent encounter audio");
                emitter.transform.SetParent(Plugin.Instance.transform);
                source = emitter.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 1f;
                source.minDistance = 10f;
                source.maxDistance = AudibleRange;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.dopplerLevel = 0f;
                source.bypassReverbZones = true;
                source.volume = 0.8f;
            }
            // Use the same mixer as ocean ambience: respects the game's master
            // and SFX settings and environmental audio snapshots.
            if (AudioMan.instance != null) source.outputAudioMixerGroup = AudioMan.instance.m_ambientMixer;
            source.transform.position = nearest.transform.position;
            if (source.clip != clip)
            {
                source.Stop();
                source.clip = clip;
            }
            // One source for all nearby serpents. More mobs or attacks never
            // stack the song or interrupt its current playback position.
            if (!source.isPlaying) source.Play();
        }

        internal static void Cleanup()
        {
            if (source != null)
            {
                source.Stop();
                UnityEngine.Object.Destroy(source.gameObject);
                source = null;
            }
            nextCheck = 0f;
        }
    }
}
