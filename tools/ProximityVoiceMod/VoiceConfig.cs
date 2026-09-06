using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace ProximityVoiceMod
{
    internal sealed class VoiceConfig
    {
        public string RelayHost = "127.0.0.1";
        public int RelayPort = 8192;
        public string Secret = "lasthuman-voice";
        public string RoomId = "default";
        public string MicDevice = "";
        public KeyCode PttKey = KeyCode.V;
        public KeyCode WhisperKey = KeyCode.LeftAlt;
        public KeyCode ShoutKey = KeyCode.LeftShift;
        public KeyCode PartyKey = KeyCode.B;
        public KeyCode RadioKey = KeyCode.N;
        public KeyCode MuteKey = KeyCode.M;
        public KeyCode HudKey = KeyCode.F8;
        public float FullVolumeDistance = 8f;
        public float WhisperMaxDistance = 5f;
        public float NormalMaxDistance = 35f;
        public float ShoutMaxDistance = 70f;
        public float RadioMaxDistance = 500f;
        public float MasterVolume = 1f;
        public int SampleRate = 16000;
        public int FrameMs = 40;
        public float VadRmsThreshold = 0.012f;
        public bool HudEnabled = true;
        public bool OcclusionEnabled = true;

        public static VoiceConfig Load(string modRoot)
        {
            VoiceConfig cfg = new VoiceConfig();
            try
            {
                string path = Path.Combine(modRoot ?? "", "voice.json");
                if (!File.Exists(path))
                {
                    // also allow game/voice.json
                    string alt = Path.Combine(Path.Combine(Application.dataPath, ".."), "voice.json");
                    if (File.Exists(alt)) path = alt;
                    else
                    {
                        Save(path, cfg);
                        return cfg;
                    }
                }
                string json = File.ReadAllText(path);
                cfg.RelayHost = ReadString(json, "relayHost", cfg.RelayHost);
                cfg.RelayPort = ReadInt(json, "relayPort", cfg.RelayPort);
                cfg.Secret = ReadString(json, "secret", cfg.Secret);
                cfg.RoomId = ReadString(json, "roomId", cfg.RoomId);
                cfg.MicDevice = ReadString(json, "micDevice", cfg.MicDevice);
                cfg.PttKey = ReadKey(json, "pttKey", cfg.PttKey);
                cfg.WhisperKey = ReadKey(json, "whisperKey", cfg.WhisperKey);
                cfg.ShoutKey = ReadKey(json, "shoutKey", cfg.ShoutKey);
                cfg.PartyKey = ReadKey(json, "partyKey", cfg.PartyKey);
                cfg.RadioKey = ReadKey(json, "radioKey", cfg.RadioKey);
                cfg.MuteKey = ReadKey(json, "muteKey", cfg.MuteKey);
                cfg.HudKey = ReadKey(json, "hudKey", cfg.HudKey);
                cfg.FullVolumeDistance = ReadFloat(json, "fullVolumeDistance", cfg.FullVolumeDistance);
                cfg.WhisperMaxDistance = ReadFloat(json, "whisperMaxDistance", cfg.WhisperMaxDistance);
                cfg.NormalMaxDistance = ReadFloat(json, "normalMaxDistance", cfg.NormalMaxDistance);
                cfg.ShoutMaxDistance = ReadFloat(json, "shoutMaxDistance", cfg.ShoutMaxDistance);
                cfg.RadioMaxDistance = ReadFloat(json, "radioMaxDistance", cfg.RadioMaxDistance);
                cfg.MasterVolume = ReadFloat(json, "masterVolume", cfg.MasterVolume);
                cfg.SampleRate = ReadInt(json, "sampleRate", cfg.SampleRate);
                cfg.FrameMs = ReadInt(json, "frameMs", cfg.FrameMs);
                cfg.VadRmsThreshold = ReadFloat(json, "vadRmsThreshold", cfg.VadRmsThreshold);
                cfg.HudEnabled = ReadBool(json, "hudEnabled", cfg.HudEnabled);
                cfg.OcclusionEnabled = ReadBool(json, "occlusionEnabled", cfg.OcclusionEnabled);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ProximityVoice] load config failed: " + e.Message);
            }

            string envHost = Environment.GetEnvironmentVariable("DURANGO_VOICE_HOST");
            string envPort = Environment.GetEnvironmentVariable("DURANGO_VOICE_PORT");
            string envSecret = Environment.GetEnvironmentVariable("DURANGO_VOICE_SECRET");
            string envRoom = Environment.GetEnvironmentVariable("DURANGO_VOICE_ROOM");
            if (!string.IsNullOrEmpty(envHost)) cfg.RelayHost = envHost;
            if (!string.IsNullOrEmpty(envPort))
            {
                int p;
                if (int.TryParse(envPort, out p)) cfg.RelayPort = p;
            }
            if (!string.IsNullOrEmpty(envSecret)) cfg.Secret = envSecret;
            if (!string.IsNullOrEmpty(envRoom)) cfg.RoomId = envRoom;
            return cfg;
        }

        public static void Save(string path, VoiceConfig cfg)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                string json =
                    "{\n" +
                    "  \"relayHost\": \"" + Esc(cfg.RelayHost) + "\",\n" +
                    "  \"relayPort\": " + cfg.RelayPort + ",\n" +
                    "  \"secret\": \"" + Esc(cfg.Secret) + "\",\n" +
                    "  \"roomId\": \"" + Esc(cfg.RoomId) + "\",\n" +
                    "  \"micDevice\": \"" + Esc(cfg.MicDevice) + "\",\n" +
                    "  \"pttKey\": \"" + cfg.PttKey + "\",\n" +
                    "  \"whisperKey\": \"" + cfg.WhisperKey + "\",\n" +
                    "  \"shoutKey\": \"" + cfg.ShoutKey + "\",\n" +
                    "  \"partyKey\": \"" + cfg.PartyKey + "\",\n" +
                    "  \"radioKey\": \"" + cfg.RadioKey + "\",\n" +
                    "  \"muteKey\": \"" + cfg.MuteKey + "\",\n" +
                    "  \"hudKey\": \"" + cfg.HudKey + "\",\n" +
                    "  \"fullVolumeDistance\": " + F(cfg.FullVolumeDistance) + ",\n" +
                    "  \"whisperMaxDistance\": " + F(cfg.WhisperMaxDistance) + ",\n" +
                    "  \"normalMaxDistance\": " + F(cfg.NormalMaxDistance) + ",\n" +
                    "  \"shoutMaxDistance\": " + F(cfg.ShoutMaxDistance) + ",\n" +
                    "  \"radioMaxDistance\": " + F(cfg.RadioMaxDistance) + ",\n" +
                    "  \"masterVolume\": " + F(cfg.MasterVolume) + ",\n" +
                    "  \"sampleRate\": " + cfg.SampleRate + ",\n" +
                    "  \"frameMs\": " + cfg.FrameMs + ",\n" +
                    "  \"vadRmsThreshold\": " + F(cfg.VadRmsThreshold) + ",\n" +
                    "  \"hudEnabled\": " + (cfg.HudEnabled ? "true" : "false") + ",\n" +
                    "  \"occlusionEnabled\": " + (cfg.OcclusionEnabled ? "true" : "false") + "\n" +
                    "}\n";
                File.WriteAllText(path, json);
            }
            catch
            {
            }
        }

        private static string Esc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string F(float v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string ReadString(string json, string key, string fallback)
        {
            string token = "\"" + key + "\"";
            int i = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return fallback;
            int colon = json.IndexOf(':', i);
            if (colon < 0) return fallback;
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return fallback;
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return fallback;
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }

        private static int ReadInt(string json, string key, int fallback)
        {
            string s = ReadNumberToken(json, key);
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static float ReadFloat(string json, string key, float fallback)
        {
            string s = ReadNumberToken(json, key);
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static bool ReadBool(string json, string key, bool fallback)
        {
            string token = "\"" + key + "\"";
            int i = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return fallback;
            int colon = json.IndexOf(':', i);
            if (colon < 0) return fallback;
            string tail = json.Substring(colon + 1).TrimStart();
            if (tail.StartsWith("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (tail.StartsWith("false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        private static KeyCode ReadKey(string json, string key, KeyCode fallback)
        {
            string s = ReadString(json, key, fallback.ToString());
            try { return (KeyCode)Enum.Parse(typeof(KeyCode), s, true); }
            catch { return fallback; }
        }

        private static string ReadNumberToken(string json, string key)
        {
            string token = "\"" + key + "\"";
            int i = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            int colon = json.IndexOf(':', i);
            if (colon < 0) return "";
            int j = colon + 1;
            while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
            int k = j;
            while (k < json.Length && (char.IsDigit(json[k]) || json[k] == '-' || json[k] == '+' || json[k] == '.' || json[k] == 'e' || json[k] == 'E')) k++;
            return json.Substring(j, k - j);
        }
    }
}
