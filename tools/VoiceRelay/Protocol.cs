using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace Durango.VoiceRelay;

public enum VoicePacketType : byte
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
public enum VoiceChannel : byte
{
    None = 0,
    Proximity = 1,
    Whisper = 2,
    Shout = 4,
    Party = 8,
    Clan = 16,
    Radio = 32,
}

public enum VoiceCodec : byte
{
    Pcm16 = 0,
    ULaw = 1,
}

public static class VoiceProtocol
{
    public const uint Magic = 0x786F7644; // 'Dvox' LE
    public const byte Version = 1;
    public const int HeaderSize = 8; // magic4 + ver1 + type1 + flags1 + codec1

    public static byte[] BuildHello(string playerId, string playerName, string roomId, string secret, VoiceChannel channels)
    {
        using var ms = new MemoryStream();
        WriteHeader(ms, VoicePacketType.Hello, (byte)channels, VoiceCodec.ULaw);
        WriteString(ms, playerId);
        WriteString(ms, playerName);
        WriteString(ms, roomId);
        WriteString(ms, secret);
        return ms.ToArray();
    }

    public static byte[] BuildHelloAck(bool ok, string message, ushort peerId)
    {
        using var ms = new MemoryStream();
        WriteHeader(ms, VoicePacketType.HelloAck, ok ? (byte)1 : (byte)0, VoiceCodec.ULaw);
        ms.WriteByte((byte)(peerId & 0xff));
        ms.WriteByte((byte)(peerId >> 8));
        WriteString(ms, message ?? "");
        return ms.ToArray();
    }

    public static byte[] BuildAudio(ushort peerId, uint seq, VoiceChannel channel, VoiceCodec codec, byte[] payload, float x, float y, float z)
    {
        using var ms = new MemoryStream(HeaderSize + 18 + payload.Length);
        WriteHeader(ms, VoicePacketType.Audio, (byte)channel, codec);
        ms.WriteByte((byte)(peerId & 0xff));
        ms.WriteByte((byte)(peerId >> 8));
        Span<byte> u32 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(u32, seq);
        ms.Write(u32);
        WriteFloat(ms, x);
        WriteFloat(ms, y);
        WriteFloat(ms, z);
        ms.Write(payload, 0, payload.Length);
        return ms.ToArray();
    }

    public static byte[] BuildState(ushort peerId, VoiceChannel activeChannels, bool muted, bool deafened, bool talking)
    {
        using var ms = new MemoryStream();
        WriteHeader(ms, VoicePacketType.State, (byte)activeChannels, VoiceCodec.ULaw);
        ms.WriteByte((byte)(peerId & 0xff));
        ms.WriteByte((byte)(peerId >> 8));
        byte flags = 0;
        if (muted) flags |= 1;
        if (deafened) flags |= 2;
        if (talking) flags |= 4;
        ms.WriteByte(flags);
        return ms.ToArray();
    }

    public static byte[] BuildAdmin(string command, string targetPlayerId, string reason)
    {
        using var ms = new MemoryStream();
        WriteHeader(ms, VoicePacketType.Admin, 0, VoiceCodec.ULaw);
        WriteString(ms, command ?? "");
        WriteString(ms, targetPlayerId ?? "");
        WriteString(ms, reason ?? "");
        return ms.ToArray();
    }

    public static byte[] BuildKeepAlive(ushort peerId)
    {
        using var ms = new MemoryStream();
        WriteHeader(ms, VoicePacketType.KeepAlive, 0, VoiceCodec.ULaw);
        ms.WriteByte((byte)(peerId & 0xff));
        ms.WriteByte((byte)(peerId >> 8));
        return ms.ToArray();
    }

    public static byte[] BuildError(string message)
    {
        using var ms = new MemoryStream();
        WriteHeader(ms, VoicePacketType.Error, 0, VoiceCodec.ULaw);
        WriteString(ms, message ?? "");
        return ms.ToArray();
    }

    public static bool TryParseHeader(ReadOnlySpan<byte> data, out VoicePacketType type, out byte flags, out VoiceCodec codec, out int offset)
    {
        type = 0;
        flags = 0;
        codec = VoiceCodec.Pcm16;
        offset = 0;
        if (data.Length < HeaderSize) return false;
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (magic != Magic) return false;
        if (data[4] != Version) return false;
        type = (VoicePacketType)data[5];
        flags = data[6];
        codec = (VoiceCodec)data[7];
        offset = HeaderSize;
        return true;
    }

    public static string ReadString(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset + 2 > data.Length) return "";
        int len = data[offset] | (data[offset + 1] << 8);
        offset += 2;
        if (len < 0 || offset + len > data.Length) return "";
        string s = Encoding.UTF8.GetString(data.Slice(offset, len));
        offset += len;
        return s;
    }

    public static ushort ReadU16(ReadOnlySpan<byte> data, ref int offset)
    {
        ushort v = (ushort)(data[offset] | (data[offset + 1] << 8));
        offset += 2;
        return v;
    }

    public static uint ReadU32(ReadOnlySpan<byte> data, ref int offset)
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset));
        offset += 4;
        return v;
    }

    public static float ReadFloat(ReadOnlySpan<byte> data, ref int offset)
    {
        float v = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(offset));
        offset += 4;
        return v;
    }

    private static void WriteHeader(Stream ms, VoicePacketType type, byte flags, VoiceCodec codec)
    {
        Span<byte> magic = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(magic, Magic);
        ms.Write(magic);
        ms.WriteByte(Version);
        ms.WriteByte((byte)type);
        ms.WriteByte(flags);
        ms.WriteByte((byte)codec);
    }

    private static void WriteString(Stream ms, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
        if (bytes.Length > 2048) Array.Resize(ref bytes, 2048);
        ms.WriteByte((byte)(bytes.Length & 0xff));
        ms.WriteByte((byte)(bytes.Length >> 8));
        ms.Write(bytes, 0, bytes.Length);
    }

    private static void WriteFloat(Stream ms, float value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(b, value);
        ms.Write(b);
    }
}

public sealed class VoicePeer
{
    public ushort PeerId;
    public string PlayerId = "";
    public string PlayerName = "";
    public string RoomId = "";
    public IPEndPoint EndPoint = new(IPAddress.Any, 0);
    public VoiceChannel Channels = VoiceChannel.Proximity;
    public bool Muted;
    public bool Deafened;
    public bool AdminMuted;
    public bool Talking;
    public DateTime LastSeenUtc = DateTime.UtcNow;
    public float X, Y, Z;
    public int PacketsThisSecond;
    public DateTime PacketWindowUtc = DateTime.UtcNow;
}
