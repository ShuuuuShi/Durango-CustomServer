using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Durango.VoiceRelay;

string bindHost = GetArg("--bind", "0.0.0.0");
int bindPort = int.Parse(GetArg("--port", "8192"));
string secret = GetArg("--secret", Environment.GetEnvironmentVariable("DURANGO_VOICE_SECRET") ?? "lasthuman-voice");
int maxPacketBytes = int.Parse(GetArg("--max-packet", "1200"));
int maxPacketsPerSec = int.Parse(GetArg("--rate", "60"));
TimeSpan idleTimeout = TimeSpan.FromSeconds(int.Parse(GetArg("--idle-sec", "45")));

var peersByEndpoint = new ConcurrentDictionary<string, VoicePeer>();
var peersById = new ConcurrentDictionary<ushort, VoicePeer>();
var peersByPlayerId = new ConcurrentDictionary<string, VoicePeer>(StringComparer.OrdinalIgnoreCase);
ushort nextPeerId = 1;
var peerIdLock = new object();

using var udp = new UdpClient(new IPEndPoint(IPAddress.Parse(bindHost), bindPort));
Console.WriteLine($"[voice-relay] listening udp://{bindHost}:{bindPort} secret={(secret.Length > 0 ? "set" : "empty")}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

_ = Task.Run(() => SweepLoop(cts.Token));

while (!cts.IsCancellationRequested)
{
    UdpReceiveResult packet;
    try
    {
        packet = await udp.ReceiveAsync(cts.Token);
    }
    catch (OperationCanceledException) { break; }
    catch (Exception ex)
    {
        Console.WriteLine("[voice-relay] recv error: " + ex.Message);
        continue;
    }

    if (packet.Buffer.Length < VoiceProtocol.HeaderSize || packet.Buffer.Length > maxPacketBytes)
        continue;

    if (!VoiceProtocol.TryParseHeader(packet.Buffer, out var type, out var flags, out var codec, out int offset))
        continue;

    string epKey = packet.RemoteEndPoint.ToString();
    try
    {
        switch (type)
        {
            case VoicePacketType.Hello:
                HandleHello(packet.Buffer.AsSpan(), offset, flags, packet.RemoteEndPoint, epKey);
                break;
            case VoicePacketType.Audio:
                HandleAudio(packet.Buffer, offset, flags, codec, packet.RemoteEndPoint, epKey);
                break;
            case VoicePacketType.State:
                HandleState(packet.Buffer.AsSpan(), offset, flags, epKey);
                break;
            case VoicePacketType.Admin:
                HandleAdmin(packet.Buffer.AsSpan(), offset, epKey);
                break;
            case VoicePacketType.KeepAlive:
                Touch(epKey);
                break;
            case VoicePacketType.Bye:
                RemovePeer(epKey, "bye");
                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[voice-relay] handle {type} from {epKey}: {ex.Message}");
    }
}

Console.WriteLine("[voice-relay] stopped");

void HandleHello(ReadOnlySpan<byte> data, int offset, byte flags, IPEndPoint remote, string epKey)
{
    string playerId = VoiceProtocol.ReadString(data, ref offset);
    string playerName = VoiceProtocol.ReadString(data, ref offset);
    string roomId = VoiceProtocol.ReadString(data, ref offset);
    string offeredSecret = VoiceProtocol.ReadString(data, ref offset);
    if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(roomId))
    {
        Send(remote, VoiceProtocol.BuildHelloAck(false, "missing player/room", 0));
        return;
    }
    if (!string.Equals(offeredSecret, secret, StringComparison.Ordinal))
    {
        Send(remote, VoiceProtocol.BuildHelloAck(false, "bad secret", 0));
        return;
    }

    if (peersByEndpoint.TryGetValue(epKey, out var existing))
    {
        existing.PlayerId = playerId;
        existing.PlayerName = string.IsNullOrWhiteSpace(playerName) ? playerId : playerName;
        existing.RoomId = roomId;
        existing.Channels = (VoiceChannel)flags == VoiceChannel.None ? VoiceChannel.Proximity : (VoiceChannel)flags;
        existing.LastSeenUtc = DateTime.UtcNow;
        peersByPlayerId[playerId] = existing;
        Send(remote, VoiceProtocol.BuildHelloAck(true, "ok", existing.PeerId));
        Console.WriteLine($"[voice-relay] rehello {existing.PlayerName}#{existing.PeerId} room={roomId}");
        return;
    }

    // drop old endpoint for same player id
    if (peersByPlayerId.TryGetValue(playerId, out var old) && old.EndPoint.ToString() != epKey)
        RemovePeer(old.EndPoint.ToString(), "replaced");

    ushort id;
    lock (peerIdLock)
    {
        id = nextPeerId++;
        if (nextPeerId == 0) nextPeerId = 1;
    }

    var peer = new VoicePeer
    {
        PeerId = id,
        PlayerId = playerId,
        PlayerName = string.IsNullOrWhiteSpace(playerName) ? playerId : playerName,
        RoomId = roomId,
        EndPoint = remote,
        Channels = (VoiceChannel)flags == VoiceChannel.None ? VoiceChannel.Proximity : (VoiceChannel)flags,
        LastSeenUtc = DateTime.UtcNow,
    };
    peersByEndpoint[epKey] = peer;
    peersById[id] = peer;
    peersByPlayerId[playerId] = peer;
    Send(remote, VoiceProtocol.BuildHelloAck(true, "ok", id));
    Console.WriteLine($"[voice-relay] hello {peer.PlayerName}#{id} room={roomId} from {epKey}");
}

void HandleAudio(byte[] buffer, int offset, byte flags, VoiceCodec codec, IPEndPoint remote, string epKey)
{
    if (!peersByEndpoint.TryGetValue(epKey, out var sender))
    {
        Send(remote, VoiceProtocol.BuildError("hello required"));
        return;
    }
    if (sender.AdminMuted || sender.Muted) return;
    if (!RateOk(sender)) return;

    var span = buffer.AsSpan();
    ushort claimedPeer = VoiceProtocol.ReadU16(span, ref offset);
    uint seq = VoiceProtocol.ReadU32(span, ref offset);
    float x = VoiceProtocol.ReadFloat(span, ref offset);
    float y = VoiceProtocol.ReadFloat(span, ref offset);
    float z = VoiceProtocol.ReadFloat(span, ref offset);
    if (offset >= buffer.Length) return;
    byte[] payload = buffer.AsSpan(offset).ToArray();
    if (payload.Length == 0 || payload.Length > 1000) return;

    sender.X = x; sender.Y = y; sender.Z = z;
    sender.Talking = true;
    sender.LastSeenUtc = DateTime.UtcNow;
    var channel = (VoiceChannel)flags;
    if (channel == VoiceChannel.None) channel = VoiceChannel.Proximity;

    // rewrite with authoritative peer id
    byte[] forward = VoiceProtocol.BuildAudio(sender.PeerId, seq, channel, codec, payload, x, y, z);
    foreach (var peer in peersByEndpoint.Values)
    {
        if (peer.PeerId == sender.PeerId) continue;
        if (peer.Deafened) continue;
        if (!string.Equals(peer.RoomId, sender.RoomId, StringComparison.OrdinalIgnoreCase)) continue;
        // channel intersection: receiver must allow this channel bit
        if ((peer.Channels & channel) == 0 && channel != VoiceChannel.Party && channel != VoiceChannel.Clan && channel != VoiceChannel.Radio)
            continue;
        Send(peer.EndPoint, forward);
    }
    _ = seq; // reserved for future jitter diagnostics
}

void HandleState(ReadOnlySpan<byte> data, int offset, byte flags, string epKey)
{
    if (!peersByEndpoint.TryGetValue(epKey, out var peer)) return;
    _ = VoiceProtocol.ReadU16(data, ref offset);
    byte stateFlags = offset < data.Length ? data[offset] : (byte)0;
    peer.Channels = (VoiceChannel)flags == 0 ? peer.Channels : (VoiceChannel)flags;
    peer.Muted = (stateFlags & 1) != 0;
    peer.Deafened = (stateFlags & 2) != 0;
    peer.Talking = (stateFlags & 4) != 0;
    peer.LastSeenUtc = DateTime.UtcNow;

    byte[] forward = VoiceProtocol.BuildState(peer.PeerId, peer.Channels, peer.Muted || peer.AdminMuted, peer.Deafened, peer.Talking);
    foreach (var other in peersByEndpoint.Values)
    {
        if (other.PeerId == peer.PeerId) continue;
        if (!string.Equals(other.RoomId, peer.RoomId, StringComparison.OrdinalIgnoreCase)) continue;
        Send(other.EndPoint, forward);
    }
}

void HandleAdmin(ReadOnlySpan<byte> data, int offset, string epKey)
{
    if (!peersByEndpoint.TryGetValue(epKey, out var actor)) return;
    // MVP admin: anyone who knows secret already joined; commands are soft moderation for room hosts.
    string command = VoiceProtocol.ReadString(data, ref offset);
    string targetPlayerId = VoiceProtocol.ReadString(data, ref offset);
    string reason = VoiceProtocol.ReadString(data, ref offset);
    if (!peersByPlayerId.TryGetValue(targetPlayerId, out var target))
    {
        Send(actor.EndPoint, VoiceProtocol.BuildError("target offline"));
        return;
    }
    switch ((command ?? "").Trim().ToLowerInvariant())
    {
        case "mute":
            target.AdminMuted = true;
            BroadcastState(target);
            Console.WriteLine($"[voice-relay] admin mute {target.PlayerName} by {actor.PlayerName}: {reason}");
            break;
        case "unmute":
            target.AdminMuted = false;
            BroadcastState(target);
            Console.WriteLine($"[voice-relay] admin unmute {target.PlayerName} by {actor.PlayerName}");
            break;
        case "deaf":
            target.Deafened = true;
            BroadcastState(target);
            break;
        case "undeaf":
            target.Deafened = false;
            BroadcastState(target);
            break;
        case "kick":
            RemovePeer(target.EndPoint.ToString(), "kicked:" + reason);
            break;
        default:
            Send(actor.EndPoint, VoiceProtocol.BuildError("unknown admin command"));
            break;
    }
}

void BroadcastState(VoicePeer peer)
{
    byte[] forward = VoiceProtocol.BuildState(peer.PeerId, peer.Channels, peer.Muted || peer.AdminMuted, peer.Deafened, peer.Talking);
    foreach (var other in peersByEndpoint.Values)
    {
        if (!string.Equals(other.RoomId, peer.RoomId, StringComparison.OrdinalIgnoreCase)) continue;
        Send(other.EndPoint, forward);
    }
}

bool RateOk(VoicePeer peer)
{
    var now = DateTime.UtcNow;
    if ((now - peer.PacketWindowUtc).TotalSeconds >= 1)
    {
        peer.PacketWindowUtc = now;
        peer.PacketsThisSecond = 0;
    }
    peer.PacketsThisSecond++;
    return peer.PacketsThisSecond <= maxPacketsPerSec;
}

void Touch(string epKey)
{
    if (peersByEndpoint.TryGetValue(epKey, out var peer))
        peer.LastSeenUtc = DateTime.UtcNow;
}

void RemovePeer(string epKey, string reason)
{
    if (!peersByEndpoint.TryRemove(epKey, out var peer)) return;
    peersById.TryRemove(peer.PeerId, out _);
    if (peersByPlayerId.TryGetValue(peer.PlayerId, out var cur) && ReferenceEquals(cur, peer))
        peersByPlayerId.TryRemove(peer.PlayerId, out _);
    Console.WriteLine($"[voice-relay] leave {peer.PlayerName}#{peer.PeerId} ({reason})");
}

async Task SweepLoop(CancellationToken token)
{
    while (!token.IsCancellationRequested)
    {
        try { await Task.Delay(5000, token); } catch { break; }
        var now = DateTime.UtcNow;
        foreach (var kv in peersByEndpoint)
        {
            if (now - kv.Value.LastSeenUtc > idleTimeout)
                RemovePeer(kv.Key, "idle");
            else if (now - kv.Value.LastSeenUtc > TimeSpan.FromSeconds(2))
                kv.Value.Talking = false;
        }
    }
}

void Send(IPEndPoint ep, byte[] data)
{
    try { _ = udp.SendAsync(data, data.Length, ep); }
    catch { /* ignore */ }
}

static string GetArg(string name, string fallback)
{
    string[] args = Environment.GetCommandLineArgs();
    for (int i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    return fallback;
}
