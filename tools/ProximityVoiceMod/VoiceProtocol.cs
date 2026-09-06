using System;
using System.IO;
using System.Text;

namespace ProximityVoiceMod
{
    internal enum VoicePacketType : byte
    {
        Hello = 1,
        HelloAck = 2,
        Audio = 3,
        State = 4,
        Admin = 5,
        KeepAlive = 6,
        Bye = 7,
        Error = 8,
    }

    [Flags]
    internal enum VoiceChannel : byte
    {
        None = 0,
        Proximity = 1,
        Whisper = 2,
        Shout = 4,
        Party = 8,
        Clan = 16,
        Radio = 32,
    }

    internal enum VoiceCodec : byte
    {
        Pcm16 = 0,
        ULaw = 1,
    }

    internal static class VoiceProtocol
    {
        public const uint Magic = 0x786F7644;
        public const byte Version = 1;
        public const int HeaderSize = 8;

        public static byte[] BuildHello(string playerId, string playerName, string roomId, string secret, VoiceChannel channels)
        {
            MemoryStream ms = new MemoryStream();
            WriteHeader(ms, VoicePacketType.Hello, (byte)channels, VoiceCodec.ULaw);
            WriteString(ms, playerId);
            WriteString(ms, playerName);
            WriteString(ms, roomId);
            WriteString(ms, secret);
            return ms.ToArray();
        }

        public static byte[] BuildAudio(ushort peerId, uint seq, VoiceChannel channel, VoiceCodec codec, byte[] payload, float x, float y, float z)
        {
            MemoryStream ms = new MemoryStream();
            WriteHeader(ms, VoicePacketType.Audio, (byte)channel, codec);
            WriteU16(ms, peerId);
            WriteU32(ms, seq);
            WriteFloat(ms, x);
            WriteFloat(ms, y);
            WriteFloat(ms, z);
            ms.Write(payload, 0, payload.Length);
            return ms.ToArray();
        }

        public static byte[] BuildState(ushort peerId, VoiceChannel channels, bool muted, bool deafened, bool talking)
        {
            MemoryStream ms = new MemoryStream();
            WriteHeader(ms, VoicePacketType.State, (byte)channels, VoiceCodec.ULaw);
            WriteU16(ms, peerId);
            byte flags = 0;
            if (muted) flags |= 1;
            if (deafened) flags |= 2;
            if (talking) flags |= 4;
            ms.WriteByte(flags);
            return ms.ToArray();
        }

        public static byte[] BuildAdmin(string command, string targetPlayerId, string reason)
        {
            MemoryStream ms = new MemoryStream();
            WriteHeader(ms, VoicePacketType.Admin, 0, VoiceCodec.ULaw);
            WriteString(ms, command);
            WriteString(ms, targetPlayerId);
            WriteString(ms, reason);
            return ms.ToArray();
        }

        public static byte[] BuildKeepAlive(ushort peerId)
        {
            MemoryStream ms = new MemoryStream();
            WriteHeader(ms, VoicePacketType.KeepAlive, 0, VoiceCodec.ULaw);
            WriteU16(ms, peerId);
            return ms.ToArray();
        }

        public static byte[] BuildBye()
        {
            MemoryStream ms = new MemoryStream();
            WriteHeader(ms, VoicePacketType.Bye, 0, VoiceCodec.ULaw);
            return ms.ToArray();
        }

        public static bool TryParseHeader(byte[] data, out VoicePacketType type, out byte flags, out VoiceCodec codec, out int offset)
        {
            type = 0;
            flags = 0;
            codec = VoiceCodec.Pcm16;
            offset = 0;
            if (data == null || data.Length < HeaderSize) return false;
            uint magic = (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
            if (magic != Magic) return false;
            if (data[4] != Version) return false;
            type = (VoicePacketType)data[5];
            flags = data[6];
            codec = (VoiceCodec)data[7];
            offset = HeaderSize;
            return true;
        }

        public static string ReadString(byte[] data, ref int offset)
        {
            if (offset + 2 > data.Length) return "";
            int len = data[offset] | (data[offset + 1] << 8);
            offset += 2;
            if (len < 0 || offset + len > data.Length) return "";
            string s = Encoding.UTF8.GetString(data, offset, len);
            offset += len;
            return s;
        }

        public static ushort ReadU16(byte[] data, ref int offset)
        {
            ushort v = (ushort)(data[offset] | (data[offset + 1] << 8));
            offset += 2;
            return v;
        }

        public static uint ReadU32(byte[] data, ref int offset)
        {
            uint v = (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
            offset += 4;
            return v;
        }

        public static float ReadFloat(byte[] data, ref int offset)
        {
            float v = BitConverter.ToSingle(data, offset);
            offset += 4;
            return v;
        }

        private static void WriteHeader(Stream ms, VoicePacketType type, byte flags, VoiceCodec codec)
        {
            ms.WriteByte((byte)(Magic & 0xff));
            ms.WriteByte((byte)((Magic >> 8) & 0xff));
            ms.WriteByte((byte)((Magic >> 16) & 0xff));
            ms.WriteByte((byte)((Magic >> 24) & 0xff));
            ms.WriteByte(Version);
            ms.WriteByte((byte)type);
            ms.WriteByte(flags);
            ms.WriteByte((byte)codec);
        }

        private static void WriteString(Stream ms, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
            if (bytes.Length > 2048)
            {
                byte[] clipped = new byte[2048];
                Buffer.BlockCopy(bytes, 0, clipped, 0, 2048);
                bytes = clipped;
            }
            ms.WriteByte((byte)(bytes.Length & 0xff));
            ms.WriteByte((byte)((bytes.Length >> 8) & 0xff));
            ms.Write(bytes, 0, bytes.Length);
        }

        private static void WriteU16(Stream ms, ushort v)
        {
            ms.WriteByte((byte)(v & 0xff));
            ms.WriteByte((byte)(v >> 8));
        }

        private static void WriteU32(Stream ms, uint v)
        {
            ms.WriteByte((byte)(v & 0xff));
            ms.WriteByte((byte)((v >> 8) & 0xff));
            ms.WriteByte((byte)((v >> 16) & 0xff));
            ms.WriteByte((byte)((v >> 24) & 0xff));
        }

        private static void WriteFloat(Stream ms, float v)
        {
            byte[] b = BitConverter.GetBytes(v);
            ms.Write(b, 0, 4);
        }
    }
}
