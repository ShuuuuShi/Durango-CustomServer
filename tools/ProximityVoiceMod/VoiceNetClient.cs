using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace ProximityVoiceMod
{
    internal sealed class IncomingAudio
    {
        public ushort PeerId;
        public uint Seq;
        public VoiceChannel Channel;
        public VoiceCodec Codec;
        public float X, Y, Z;
        public byte[] Payload;
    }

    internal sealed class IncomingState
    {
        public ushort PeerId;
        public VoiceChannel Channels;
        public bool Muted;
        public bool Deafened;
        public bool Talking;
    }

    internal sealed class VoiceNetClient
    {
        private UdpClient _udp;
        private IPEndPoint _relay;
        private Thread _recvThread;
        private volatile bool _running;
        private readonly object _queueGate = new object();
        private readonly Queue<IncomingAudio> _audio = new Queue<IncomingAudio>();
        private readonly Queue<IncomingState> _states = new Queue<IncomingState>();
        private readonly Queue<string> _messages = new Queue<string>();

        public ushort PeerId { get; private set; }
        public bool Connected { get; private set; }
        public string LastError { get; private set; }

        public bool Connect(string host, int port, string playerId, string playerName, string roomId, string secret, VoiceChannel channels)
        {
            Disconnect();
            LastError = "";
            try
            {
                _relay = new IPEndPoint(IPAddress.Parse(host), port);
                _udp = new UdpClient();
                _udp.Client.SendTimeout = 1000;
                _udp.Client.ReceiveTimeout = 1000;
                _udp.Connect(_relay);
                byte[] hello = VoiceProtocol.BuildHello(playerId, playerName, roomId, secret, channels);
                _udp.Send(hello, hello.Length);

                // wait briefly for ack on this thread (mod startup)
                IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
                DateTime until = DateTime.UtcNow.AddSeconds(2);
                while (DateTime.UtcNow < until)
                {
                    if (_udp.Available <= 0)
                    {
                        Thread.Sleep(20);
                        continue;
                    }
                    byte[] data = _udp.Receive(ref any);
                    VoicePacketType type;
                    byte flags;
                    VoiceCodec codec;
                    int offset;
                    if (!VoiceProtocol.TryParseHeader(data, out type, out flags, out codec, out offset)) continue;
                    if (type == VoicePacketType.HelloAck)
                    {
                        ushort peer = VoiceProtocol.ReadU16(data, ref offset);
                        string msg = VoiceProtocol.ReadString(data, ref offset);
                        if (flags == 0)
                        {
                            LastError = string.IsNullOrEmpty(msg) ? "hello rejected" : msg;
                            Disconnect();
                            return false;
                        }
                        PeerId = peer;
                        Connected = true;
                        _running = true;
                        _recvThread = new Thread(RecvLoop);
                        _recvThread.IsBackground = true;
                        _recvThread.Start();
                        return true;
                    }
                    if (type == VoicePacketType.Error)
                    {
                        LastError = VoiceProtocol.ReadString(data, ref offset);
                        Disconnect();
                        return false;
                    }
                }
                LastError = "relay timeout";
                Disconnect();
                return false;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Disconnect();
                return false;
            }
        }

        public void Disconnect()
        {
            _running = false;
            Connected = false;
            try
            {
                if (_udp != null && PeerId != 0)
                {
                    byte[] bye = VoiceProtocol.BuildBye();
                    _udp.Send(bye, bye.Length);
                }
            }
            catch { }
            try { if (_udp != null) _udp.Close(); } catch { }
            _udp = null;
            PeerId = 0;
        }

        public void SendAudio(uint seq, VoiceChannel channel, byte[] payload, float x, float y, float z)
        {
            if (!Connected || _udp == null || payload == null || payload.Length == 0) return;
            try
            {
                byte[] packet = VoiceProtocol.BuildAudio(PeerId, seq, channel, VoiceCodec.ULaw, payload, x, y, z);
                _udp.Send(packet, packet.Length);
            }
            catch (Exception e)
            {
                LastError = e.Message;
            }
        }

        public void SendState(VoiceChannel channels, bool muted, bool deafened, bool talking)
        {
            if (!Connected || _udp == null) return;
            try
            {
                byte[] packet = VoiceProtocol.BuildState(PeerId, channels, muted, deafened, talking);
                _udp.Send(packet, packet.Length);
            }
            catch { }
        }

        public void SendKeepAlive()
        {
            if (!Connected || _udp == null) return;
            try
            {
                byte[] packet = VoiceProtocol.BuildKeepAlive(PeerId);
                _udp.Send(packet, packet.Length);
            }
            catch { }
        }

        public void SendAdmin(string command, string targetPlayerId, string reason)
        {
            if (!Connected || _udp == null) return;
            try
            {
                byte[] packet = VoiceProtocol.BuildAdmin(command, targetPlayerId, reason);
                _udp.Send(packet, packet.Length);
            }
            catch { }
        }

        public void Drain(List<IncomingAudio> audioOut, List<IncomingState> stateOut, List<string> messagesOut)
        {
            lock (_queueGate)
            {
                while (_audio.Count > 0 && audioOut != null) audioOut.Add(_audio.Dequeue());
                while (_states.Count > 0 && stateOut != null) stateOut.Add(_states.Dequeue());
                while (_messages.Count > 0 && messagesOut != null) messagesOut.Add(_messages.Dequeue());
            }
        }

        private void RecvLoop()
        {
            IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    if (_udp == null) break;
                    if (_udp.Available <= 0)
                    {
                        Thread.Sleep(5);
                        continue;
                    }
                    byte[] data = _udp.Receive(ref any);
                    VoicePacketType type;
                    byte flags;
                    VoiceCodec codec;
                    int offset;
                    if (!VoiceProtocol.TryParseHeader(data, out type, out flags, out codec, out offset)) continue;
                    if (type == VoicePacketType.Audio)
                    {
                        IncomingAudio a = new IncomingAudio();
                        a.PeerId = VoiceProtocol.ReadU16(data, ref offset);
                        a.Seq = VoiceProtocol.ReadU32(data, ref offset);
                        a.X = VoiceProtocol.ReadFloat(data, ref offset);
                        a.Y = VoiceProtocol.ReadFloat(data, ref offset);
                        a.Z = VoiceProtocol.ReadFloat(data, ref offset);
                        a.Channel = (VoiceChannel)flags;
                        a.Codec = codec;
                        int len = data.Length - offset;
                        a.Payload = new byte[len];
                        Buffer.BlockCopy(data, offset, a.Payload, 0, len);
                        lock (_queueGate)
                        {
                            if (_audio.Count > 64) _audio.Dequeue();
                            _audio.Enqueue(a);
                        }
                    }
                    else if (type == VoicePacketType.State)
                    {
                        IncomingState s = new IncomingState();
                        s.PeerId = VoiceProtocol.ReadU16(data, ref offset);
                        s.Channels = (VoiceChannel)flags;
                        byte st = offset < data.Length ? data[offset] : (byte)0;
                        s.Muted = (st & 1) != 0;
                        s.Deafened = (st & 2) != 0;
                        s.Talking = (st & 4) != 0;
                        lock (_queueGate)
                        {
                            _states.Enqueue(s);
                        }
                    }
                    else if (type == VoicePacketType.Error)
                    {
                        string msg = VoiceProtocol.ReadString(data, ref offset);
                        lock (_queueGate) { _messages.Enqueue(msg); }
                    }
                }
                catch (SocketException)
                {
                    Thread.Sleep(15);
                }
                catch
                {
                    Thread.Sleep(30);
                }
            }
        }
    }
}
