using System;
using System.Collections.Generic;
using Durango.Modding;
using UnityEngine;

namespace ProximityVoiceMod
{
    internal sealed class VoiceRuntime : MonoBehaviour
    {
        public static VoiceRuntime Instance;

        public VoiceConfig Config;
        public IClientModApi Api;
        public string ModRoot;

        private MicCapture _mic;
        private VoiceNetClient _net;
        private readonly Dictionary<ushort, RemoteVoicePlayer> _peers = new Dictionary<ushort, RemoteVoicePlayer>();
        private readonly Dictionary<string, ushort> _nameToPeer = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        private readonly List<IncomingAudio> _audioDrain = new List<IncomingAudio>();
        private readonly List<IncomingState> _stateDrain = new List<IncomingState>();
        private readonly List<string> _msgDrain = new List<string>();
        private readonly List<NearbyPlayer> _nearby = new List<NearbyPlayer>();
        private readonly VoiceHud _hud = new VoiceHud();

        private float[] _frame;
        private int _frameSamples;
        private uint _seq;
        private float _keepAliveTimer;
        private float _reconnectTimer;
        private bool _partyMode;
        private bool _radioMode;
        private bool _selfMuted;
        private bool _deafened;
        private bool _transmitting;
        private VoiceChannel _txChannel = VoiceChannel.Proximity;
        private string _status = "idle";
        private Transform _audioRoot;

        public VoiceConfig ConfigRef { get { return Config; } }
        public IDictionary<ushort, RemoteVoicePlayer> Peers { get { return _peers; } }
        public bool SelfMuted { get { return _selfMuted; } }
        public bool Deafened { get { return _deafened; } }
        public bool IsTransmitting { get { return _transmitting; } }
        public VoiceChannel TransmitChannel { get { return _txChannel; } }
        public string StatusLine { get { return _status; } }

        public void Bootstrap(IClientModApi api, VoiceConfig cfg, string modRoot)
        {
            Api = api;
            Config = cfg;
            ModRoot = modRoot;
            Instance = this;
            _frameSamples = Math.Max(160, cfg.SampleRate * cfg.FrameMs / 1000);
            _frame = new float[_frameSamples];
            _mic = new MicCapture(cfg.SampleRate, _frameSamples);
            _net = new VoiceNetClient();
            _hud.Visible = cfg.HudEnabled;
            GameObject root = new GameObject("ProximityVoiceAudioRoot");
            UnityEngine.Object.DontDestroyOnLoad(root);
            _audioRoot = root.transform;
            DontDestroyOnLoad(gameObject);
        }

        public void OnGameReady()
        {
            TryConnect(true);
            if (!_mic.IsRunning)
            {
                if (!_mic.Start(Config.MicDevice))
                {
                    _status = "mic fail: " + _mic.Error;
                    if (Api != null) Api.ShowMessage("ProximityVoice: " + _status);
                }
                else
                {
                    if (Api != null) Api.Log("mic started: " + _mic.Device);
                }
            }
        }

        private void Update()
        {
            if (Config == null) return;
            HandleHotkeys();
            PumpNetwork();
            UpdateTransmit();
            UpdatePeerPlayback();
            _keepAliveTimer += Time.deltaTime;
            if (_keepAliveTimer > 5f)
            {
                _keepAliveTimer = 0f;
                if (_net != null && _net.Connected) _net.SendKeepAlive();
            }
            if (_net != null && !_net.Connected)
            {
                _reconnectTimer += Time.deltaTime;
                if (_reconnectTimer > 5f)
                {
                    _reconnectTimer = 0f;
                    TryConnect(false);
                }
            }
        }

        private void OnGUI()
        {
            if (Config == null) return;
            string overlay = VoiceHud.BuildOverlay(this);
            if (!string.IsNullOrEmpty(overlay))
            {
                GUI.color = _transmitting ? Color.red : Color.white;
                GUI.Label(new Rect(12, 12, 520, 24), overlay);
                GUI.color = Color.white;
            }
            _hud.Draw(this);
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        public void Shutdown()
        {
            try
            {
                if (_mic != null) _mic.Stop();
                if (_net != null) _net.Disconnect();
                foreach (KeyValuePair<ushort, RemoteVoicePlayer> kv in _peers)
                    kv.Value.Destroy();
                _peers.Clear();
            }
            catch { }
        }

        private void HandleHotkeys()
        {
            if (Input.GetKeyDown(Config.HudKey)) _hud.Toggle();
            if (Input.GetKeyDown(Config.MuteKey)) ToggleSelfMute();
            if (Input.GetKeyDown(Config.PartyKey))
            {
                _partyMode = !_partyMode;
                if (_partyMode) _radioMode = false;
                NotifyChannel();
            }
            if (Input.GetKeyDown(Config.RadioKey))
            {
                _radioMode = !_radioMode;
                if (_radioMode) _partyMode = false;
                NotifyChannel();
            }
        }

        private void UpdateTransmit()
        {
            bool ptt = Input.GetKey(Config.PttKey);
            VoiceChannel channel = VoiceChannel.Proximity;
            if (_partyMode) channel = VoiceChannel.Party;
            else if (_radioMode) channel = VoiceChannel.Radio;
            else if (Input.GetKey(Config.WhisperKey)) channel = VoiceChannel.Whisper;
            else if (Input.GetKey(Config.ShoutKey)) channel = VoiceChannel.Shout;

            _txChannel = channel;
            bool wantTx = ptt && !_selfMuted && _net != null && _net.Connected;
            if (!wantTx)
            {
                if (_transmitting)
                {
                    _transmitting = false;
                    _net.SendState(AllowedChannels(), _selfMuted, _deafened, false);
                }
                // still drain mic buffer to avoid backlog
                if (_mic != null && _mic.IsRunning) while (_mic.Read(_frame) > 0) { }
                return;
            }

            if (_mic == null || !_mic.IsRunning)
            {
                if (_mic != null && !_mic.Start(Config.MicDevice) && Api != null)
                    Api.ShowMessage("ไมค์ใช้ไม่ได้: " + _mic.Error);
                return;
            }

            string entityId;
            string name;
            Vector3 pos;
            if (!PlayerDirectory.TryGetLocal(out entityId, out name, out pos)) return;

            int got;
            while ((got = _mic.Read(_frame)) > 0)
            {
                int use = Math.Min(got, _frameSamples);
                float rms = ULawCodec.Rms(_frame, use);
                if (rms < Config.VadRmsThreshold) continue; // VAD: don't send silence
                // pad/truncate to frame
                if (use < _frameSamples)
                {
                    for (int i = use; i < _frameSamples; i++) _frame[i] = 0f;
                }
                byte[] payload = ULawCodec.Encode(_frame, _frameSamples);
                _seq++;
                _net.SendAudio(_seq, channel, payload, pos.x, pos.y, pos.z);
                if (!_transmitting)
                {
                    _transmitting = true;
                    _net.SendState(AllowedChannels(), _selfMuted, _deafened, true);
                }
            }
        }

        private void PumpNetwork()
        {
            if (_net == null) return;
            _audioDrain.Clear();
            _stateDrain.Clear();
            _msgDrain.Clear();
            _net.Drain(_audioDrain, _stateDrain, _msgDrain);
            for (int i = 0; i < _msgDrain.Count; i++)
            {
                _status = _msgDrain[i];
                if (Api != null) Api.Log("relay: " + _msgDrain[i]);
            }
            for (int i = 0; i < _stateDrain.Count; i++)
            {
                IncomingState st = _stateDrain[i];
                RemoteVoicePlayer peer = GetOrCreatePeer(st.PeerId);
                peer.ServerMuted = st.Muted;
                peer.Talking = st.Talking;
            }
            PlayerDirectory.GetNearby(_nearby);
            for (int i = 0; i < _audioDrain.Count; i++)
            {
                IncomingAudio a = _audioDrain[i];
                RemoteVoicePlayer peer = GetOrCreatePeer(a.PeerId);
                Vector3 pos = new Vector3(a.X, a.Y, a.Z);
                // Prefer live transform if we can map by proximity name later; packet pos is authoritative enough.
                TryBindPeerName(peer);
                peer.PushAudio(a.Payload, a.Codec, a.Channel, pos);
            }
        }

        private void UpdatePeerPlayback()
        {
            string entityId;
            string name;
            Vector3 listener;
            if (!PlayerDirectory.TryGetLocal(out entityId, out name, out listener)) return;
            List<ushort> stale = null;
            foreach (KeyValuePair<ushort, RemoteVoicePlayer> kv in _peers)
            {
                kv.Value.UpdatePlayback(Config, listener, _deafened, Config.OcclusionEnabled);
                if (Time.realtimeSinceStartup - kv.Value.LastHeardTime > 60f)
                {
                    if (stale == null) stale = new List<ushort>();
                    stale.Add(kv.Key);
                }
            }
            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    RemoteVoicePlayer p;
                    if (_peers.TryGetValue(stale[i], out p))
                    {
                        p.Destroy();
                        _peers.Remove(stale[i]);
                    }
                }
            }
        }

        private RemoteVoicePlayer GetOrCreatePeer(ushort peerId)
        {
            RemoteVoicePlayer peer;
            if (!_peers.TryGetValue(peerId, out peer))
            {
                peer = new RemoteVoicePlayer();
                peer.PeerId = peerId;
                peer.EnsureAudio(_audioRoot, Config.SampleRate);
                _peers[peerId] = peer;
            }
            return peer;
        }

        private void TryBindPeerName(RemoteVoicePlayer peer)
        {
            if (!string.IsNullOrEmpty(peer.PlayerName)) return;
            // heuristic: nearest nearby player to packet position
            float best = 3.5f;
            string bestName = null;
            string bestId = null;
            for (int i = 0; i < _nearby.Count; i++)
            {
                float d = Vector3.Distance(_nearby[i].Position, peer.WorldPos);
                if (d < best)
                {
                    best = d;
                    bestName = _nearby[i].Name;
                    bestId = _nearby[i].EntityId;
                }
            }
            if (bestName != null)
            {
                peer.PlayerName = bestName;
                peer.PlayerId = bestId;
                _nameToPeer[bestName] = peer.PeerId;
            }
        }

        private void TryConnect(bool announce)
        {
            string entityId;
            string name;
            Vector3 pos;
            if (!PlayerDirectory.TryGetLocal(out entityId, out name, out pos))
            {
                _status = "waiting player";
                return;
            }
            string room = PlayerDirectory.InferRoomId(Config.RoomId);
            Config.RoomId = room;
            bool ok = _net.Connect(Config.RelayHost, Config.RelayPort, entityId, name, room, Config.Secret, AllowedChannels());
            if (ok)
            {
                _status = "connected #" + _net.PeerId + " @" + room;
                _net.SendState(AllowedChannels(), _selfMuted, _deafened, false);
                if (announce && Api != null) Api.ShowMessage("ProximityVoice พร้อม (กด " + Config.PttKey + " พูด)");
            }
            else
            {
                _status = "relay fail: " + _net.LastError;
                if (announce && Api != null) Api.ShowMessage("ProximityVoice: ต่อ relay ไม่ได้ — " + _net.LastError);
            }
        }

        private VoiceChannel AllowedChannels()
        {
            VoiceChannel c = VoiceChannel.Proximity | VoiceChannel.Whisper | VoiceChannel.Shout;
            if (_partyMode) c |= VoiceChannel.Party;
            if (_radioMode) c |= VoiceChannel.Radio;
            c |= VoiceChannel.Clan;
            return c;
        }

        private void NotifyChannel()
        {
            string mode = _partyMode ? "PARTY" : (_radioMode ? "RADIO" : "PROXIMITY");
            _status = "mode " + mode;
            if (Api != null) Api.ShowMessage("Voice mode: " + mode);
            if (_net != null && _net.Connected)
                _net.SendState(AllowedChannels(), _selfMuted, _deafened, _transmitting);
        }

        public void ToggleSelfMute()
        {
            _selfMuted = !_selfMuted;
            if (Api != null) Api.ShowMessage(_selfMuted ? "ปิดไมค์แล้ว" : "เปิดไมค์แล้ว");
            if (_net != null && _net.Connected)
                _net.SendState(AllowedChannels(), _selfMuted, _deafened, false);
        }

        public void ToggleDeafen()
        {
            _deafened = !_deafened;
            if (Api != null) Api.ShowMessage(_deafened ? "ปิดหูแล้ว" : "เปิดหูแล้ว");
            if (_net != null && _net.Connected)
                _net.SendState(AllowedChannels(), _selfMuted, _deafened, _transmitting);
        }

        public void AdminMute(RemoteVoicePlayer peer)
        {
            if (peer == null || _net == null || !_net.Connected) return;
            string target = !string.IsNullOrEmpty(peer.PlayerId) ? peer.PlayerId : peer.PlayerName;
            if (string.IsNullOrEmpty(target)) return;
            _net.SendAdmin("mute", target, "hud");
            peer.ServerMuted = true;
        }
    }
}
