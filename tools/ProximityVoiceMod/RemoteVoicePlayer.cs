using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProximityVoiceMod
{
    internal sealed class RemoteVoicePlayer
    {
        public ushort PeerId;
        public string PlayerId = "";
        public string PlayerName = "";
        public Vector3 WorldPos;
        public VoiceChannel LastChannel = VoiceChannel.Proximity;
        public bool Talking;
        public bool MutedByMe;
        public bool ServerMuted;
        public float LastHeardTime;

        private GameObject _go;
        private AudioSource _source;
        private AudioClip _clip;
        private readonly object _bufGate = new object();
        private readonly Queue<float> _pcm = new Queue<float>();
        private int _sampleRate;
        private float[] _decodeScratch;

        public void EnsureAudio(Transform parent, int sampleRate)
        {
            _sampleRate = sampleRate;
            if (_go != null) return;
            _go = new GameObject("VoicePeer_" + PeerId);
            if (parent != null) _go.transform.SetParent(parent, false);
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _source = _go.AddComponent<AudioSource>();
            _source.loop = true;
            _source.playOnAwake = false;
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 2f;
            _source.maxDistance = 80f;
            _source.dopplerLevel = 0f;
            _source.volume = 0f;
            _clip = AudioClip.Create("voice_" + PeerId, sampleRate, 1, sampleRate, true, OnAudioRead);
            _source.clip = _clip;
            _source.Play();
            _decodeScratch = new float[sampleRate];
        }

        public void PushAudio(byte[] payload, VoiceCodec codec, VoiceChannel channel, Vector3 pos)
        {
            WorldPos = pos;
            LastChannel = channel;
            Talking = true;
            LastHeardTime = Time.realtimeSinceStartup;
            if (payload == null || payload.Length == 0) return;
            if (_decodeScratch == null || _decodeScratch.Length < payload.Length)
                _decodeScratch = new float[payload.Length];
            if (codec == VoiceCodec.ULaw || codec == VoiceCodec.Pcm16)
            {
                // PCM16 path unused for now; treat bytes as ulaw
                ULawCodec.Decode(payload, _decodeScratch);
                lock (_bufGate)
                {
                    for (int i = 0; i < payload.Length; i++)
                    {
                        if (_pcm.Count > _sampleRate) _pcm.Dequeue(); // drop oldest on overflow (~1s)
                        _pcm.Enqueue(_decodeScratch[i]);
                    }
                }
            }
        }

        public void UpdatePlayback(VoiceConfig cfg, Vector3 listenerPos, bool deafened, bool occlusionEnabled)
        {
            if (_go == null || _source == null) return;
            _go.transform.position = WorldPos;
            if (Time.realtimeSinceStartup - LastHeardTime > 0.45f) Talking = false;
            if (deafened || MutedByMe || ServerMuted)
            {
                _source.volume = 0f;
                return;
            }
            float dist = Vector3.Distance(listenerPos, WorldPos);
            bool occluded = false;
            if (occlusionEnabled) occluded = ProximityMath.RayOccluded(listenerPos, WorldPos);
            float vol = ProximityMath.Attenuation(cfg, LastChannel, dist, occluded);
            _source.volume = vol;
            _source.maxDistance = Mathf.Max(8f, ProximityMath.MaxDistanceForChannel(cfg, LastChannel));
            // Party/Clan are non-spatial for clarity
            if ((LastChannel & VoiceChannel.Party) != 0 || (LastChannel & VoiceChannel.Clan) != 0 || (LastChannel & VoiceChannel.Radio) != 0)
                _source.spatialBlend = 0f;
            else
                _source.spatialBlend = 1f;
        }

        public void Destroy()
        {
            try
            {
                if (_source != null) _source.Stop();
                if (_go != null) UnityEngine.Object.Destroy(_go);
            }
            catch { }
            _go = null;
            _source = null;
            _clip = null;
            lock (_bufGate) { _pcm.Clear(); }
        }

        private void OnAudioRead(float[] data)
        {
            lock (_bufGate)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    if (_pcm.Count > 0) data[i] = _pcm.Dequeue();
                    else data[i] = 0f;
                }
            }
        }
    }
}
