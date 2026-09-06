using UnityEngine;

namespace ProximityVoiceMod
{
    internal static class ProximityMath
    {
        public static float MaxDistanceForChannel(VoiceConfig cfg, VoiceChannel channel)
        {
            if ((channel & VoiceChannel.Radio) != 0) return cfg.RadioMaxDistance;
            if ((channel & VoiceChannel.Party) != 0 || (channel & VoiceChannel.Clan) != 0) return float.MaxValue;
            if ((channel & VoiceChannel.Shout) != 0) return cfg.ShoutMaxDistance;
            if ((channel & VoiceChannel.Whisper) != 0) return cfg.WhisperMaxDistance;
            return cfg.NormalMaxDistance;
        }

        public static float Attenuation(VoiceConfig cfg, VoiceChannel channel, float distance, bool occluded)
        {
            if ((channel & VoiceChannel.Party) != 0 || (channel & VoiceChannel.Clan) != 0)
                return cfg.MasterVolume * (occluded ? 0.85f : 1f);

            float max = MaxDistanceForChannel(cfg, channel);
            if (distance >= max) return 0f;
            float full = cfg.FullVolumeDistance;
            if ((channel & VoiceChannel.Whisper) != 0) full = Mathf.Min(full, 2f);
            if ((channel & VoiceChannel.Shout) != 0) full = Mathf.Max(full, 12f);
            float vol;
            if (distance <= full) vol = 1f;
            else vol = 1f - ((distance - full) / Mathf.Max(0.01f, max - full));
            if (vol < 0f) vol = 0f;
            if (occluded) vol *= 0.35f;
            return vol * cfg.MasterVolume;
        }

        public static bool RayOccluded(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 0.2f) return false;
            RaycastHit hit;
            // best-effort: anything solid between mouths
            if (Physics.Raycast(from + Vector3.up * 1.4f, delta.normalized, out hit, dist))
            {
                // ignore if hit is very close to target (likely the player collider)
                if (hit.distance < dist - 0.75f) return true;
            }
            return false;
        }
    }
}
