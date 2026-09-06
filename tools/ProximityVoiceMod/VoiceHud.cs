using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ProximityVoiceMod
{
    internal sealed class VoiceHud
    {
        private bool _visible = true;
        private Rect _win = new Rect(12, 120, 320, 220);
        private Vector2 _scroll;

        public bool Visible
        {
            get { return _visible; }
            set { _visible = value; }
        }

        public void Toggle() { _visible = !_visible; }

        public void Draw(VoiceRuntime rt)
        {
            if (!_visible || rt == null) return;
            _win = GUI.Window(592817, _win, id => DrawWindow(id, rt), "Proximity Voice (GTA)");
        }

        private void DrawWindow(int id, VoiceRuntime rt)
        {
            GUILayout.Label(rt.StatusLine);
            GUILayout.Label("PTT: " + rt.Config.PttKey + " | Whisper: " + rt.Config.WhisperKey + " | Shout: " + rt.Config.ShoutKey);
            GUILayout.Label("Party: " + rt.Config.PartyKey + " | Radio: " + rt.Config.RadioKey + " | Mute: " + rt.Config.MuteKey);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(rt.SelfMuted ? "Unmute Mic" : "Mute Mic")) rt.ToggleSelfMute();
            if (GUILayout.Button(rt.Deafened ? "Undeafen" : "Deafen")) rt.ToggleDeafen();
            GUILayout.EndHorizontal();
            GUILayout.Label("Master Volume");
            rt.Config.MasterVolume = GUILayout.HorizontalSlider(rt.Config.MasterVolume, 0f, 1.5f);
            GUILayout.Label("Speaking peers");
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(90));
            foreach (KeyValuePair<ushort, RemoteVoicePlayer> kv in rt.Peers)
            {
                RemoteVoicePlayer p = kv.Value;
                string mark = p.Talking ? "[พูด]" : "     ";
                GUILayout.BeginHorizontal();
                GUILayout.Label(mark + " " + (string.IsNullOrEmpty(p.PlayerName) ? ("#" + p.PeerId) : p.PlayerName));
                if (GUILayout.Button(p.MutedByMe ? "Unmute" : "Mute", GUILayout.Width(70)))
                    p.MutedByMe = !p.MutedByMe;
                if (GUILayout.Button("A-Mute", GUILayout.Width(60)))
                    rt.AdminMute(p);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        public static string BuildOverlay(VoiceRuntime rt)
        {
            if (rt == null) return "";
            StringBuilder sb = new StringBuilder();
            if (rt.IsTransmitting) sb.Append("MIC ON (").Append(rt.TransmitChannel).Append(")  ");
            if (rt.SelfMuted) sb.Append("MUTED  ");
            if (rt.Deafened) sb.Append("DEAF  ");
            int talking = 0;
            foreach (KeyValuePair<ushort, RemoteVoicePlayer> kv in rt.Peers)
                if (kv.Value.Talking) talking++;
            if (talking > 0) sb.Append("hearing ").Append(talking);
            return sb.ToString();
        }
    }
}
