using System;
using UnityEngine;

namespace ProximityVoiceMod
{
    internal sealed class MicCapture
    {
        private AudioClip _clip;
        private string _device;
        private int _sampleRate;
        private int _lastPos;
        private readonly float[] _scratch;

        public bool IsRunning { get { return _clip != null && Microphone.IsRecording(_device); } }
        public string Device { get { return _device; } }
        public string Error { get; private set; }

        public MicCapture(int sampleRate, int maxFrameSamples)
        {
            _sampleRate = sampleRate;
            _scratch = new float[Math.Max(maxFrameSamples * 4, sampleRate)];
            Error = "";
        }

        public bool Start(string preferredDevice)
        {
            Stop();
            Error = "";
            try
            {
                string[] devices = Microphone.devices;
                if (devices == null || devices.Length == 0)
                {
                    Error = "ไม่พบไมโครโฟน — ตรวจสิทธิ์ไมค์ของ Windows";
                    return false;
                }
                _device = preferredDevice;
                if (string.IsNullOrEmpty(_device))
                    _device = devices[0];
                else
                {
                    bool found = false;
                    for (int i = 0; i < devices.Length; i++)
                    {
                        if (string.Equals(devices[i], _device, StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found) _device = devices[0];
                }
                _clip = Microphone.Start(_device, true, 1, _sampleRate);
                _lastPos = 0;
                return _clip != null;
            }
            catch (Exception e)
            {
                Error = e.Message;
                return false;
            }
        }

        public void Stop()
        {
            try
            {
                if (!string.IsNullOrEmpty(_device) && Microphone.IsRecording(_device))
                    Microphone.End(_device);
            }
            catch { }
            _clip = null;
            _lastPos = 0;
        }

        public int Read(float[] buffer)
        {
            if (_clip == null || buffer == null || buffer.Length == 0) return 0;
            int pos = Microphone.GetPosition(_device);
            if (pos < 0 || pos == _lastPos) return 0;
            int clipSamples = _clip.samples;
            int available;
            if (pos > _lastPos) available = pos - _lastPos;
            else available = clipSamples - _lastPos + pos;
            if (available <= 0) return 0;
            int toRead = Math.Min(available, buffer.Length);
            // Read contiguous chunks into scratch then copy
            int first = Math.Min(toRead, clipSamples - _lastPos);
            if (first > 0)
            {
                _clip.GetData(_scratch, _lastPos);
                Array.Copy(_scratch, 0, buffer, 0, first);
            }
            int remain = toRead - first;
            if (remain > 0)
            {
                _clip.GetData(_scratch, 0);
                Array.Copy(_scratch, 0, buffer, first, remain);
            }
            _lastPos = (_lastPos + toRead) % clipSamples;
            return toRead;
        }

        public static string[] ListDevices()
        {
            return Microphone.devices ?? new string[0];
        }
    }
}
