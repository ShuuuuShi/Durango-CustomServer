using System;
using System.IO;
using Durango.Modding;
using UnityEngine;

namespace ProximityVoiceMod
{
    /// <summary>
    /// GTA-style proximity voice chat for Durango LastHuman.
    /// Requires VoiceRelay (tools/VoiceRelay) and ClientModLoader hook.
    /// </summary>
    public sealed class ProximityVoicePlugin : IClientPlugin, IClientModIdentity, IClientModLifecycle
    {
        public string Name { get { return "ProximityVoice"; } }
        public string Version { get { return "1.0.0"; } }
        public string Id { get { return "proximity-voice"; } }
        public string ApiVersion { get { return "1"; } }
        public string Signature { get { return ""; } }
        public string PublicKey { get { return ""; } }

        private IClientModApi _api;
        private VoiceRuntime _runtime;
        private string _modRoot;

        public void OnPreLoad(IClientModApi api)
        {
            _api = api;
            _modRoot = FindModRoot();
            api.Log("PreLoad root=" + _modRoot);
        }

        public void OnLoad(IClientModApi api)
        {
            VoiceConfig cfg = VoiceConfig.Load(_modRoot);
            GameObject go = new GameObject("__ProximityVoiceRuntime");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runtime = go.AddComponent<VoiceRuntime>();
            _runtime.Bootstrap(api, cfg, _modRoot);

            api.OnGameReady(delegate
            {
                if (_runtime != null) _runtime.OnGameReady();
            });

            // Keep RegisterHotkey for discoverability; hold-to-talk is handled in Update via Input.GetKey
            api.RegisterHotkey(cfg.HudKey, delegate
            {
                api.ShowMessage("ProximityVoice HUD: F8 / PTT=" + cfg.PttKey + " Whisper=" + cfg.WhisperKey + " Shout=" + cfg.ShoutKey);
            });

            api.Log("โหลดแล้ว — PTT=" + cfg.PttKey + " relay=" + cfg.RelayHost + ":" + cfg.RelayPort);
        }

        public void OnPostLoad(IClientModApi api)
        {
            api.Log("PostLoad — ProximityVoice พร้อม (ต้องเปิด VoiceRelay ด้วย)");
        }

        public void OnDisable(IClientModApi api)
        {
            if (_runtime != null)
            {
                _runtime.Shutdown();
                if (_runtime.gameObject != null) UnityEngine.Object.Destroy(_runtime.gameObject);
                _runtime = null;
            }
        }

        private static string FindModRoot()
        {
            try
            {
                string mods = Path.Combine(Path.Combine(Path.Combine(Application.dataPath, ".."), "mods"), "ProximityVoice");
                if (Directory.Exists(mods)) return Path.GetFullPath(mods);
                string flat = Path.Combine(Path.Combine(Application.dataPath, ".."), "mods");
                if (Directory.Exists(flat)) return Path.GetFullPath(flat);
            }
            catch { }
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }
    }
}
