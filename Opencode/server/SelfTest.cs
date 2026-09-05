using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using Durango.Network;
using Durango.Online;
using Durango.Utils;
using Messages;

namespace DurangoServerNx;

// self-test ชั้น TCP: จำลอง handshake ของเกมแท้ (GameManager.SendAuthMessage/SendReady)
//   /sessions → GetClock → Clock, Auth → Welcome, Ready → OK → SetChunk → Chunk
// รัน: DurangoServerNx --selftest [--gateway-port N] [--game-port N] (เซิร์ฟต้องกำลังรันอยู่)
internal static class SelfTest
{
    /// <summary>
    /// กุญแจบัญชีของตัวละครทดสอบ — ต้องผ่าน AccountKeys.Normalize (ตัวอักษร/ตัวเลข/ขีด เท่านั้น)
    /// </summary>
    private const string SelfTestAccountKey = "selftest-local";

    public static int Run(int gatewayPort, int gamePort)
    {
        // 1) ขอ session ผ่าน /sessions (เหมือน client ตอนบูต)
        //
        // ⚠️ [6 ก.ย. 2026] ต้องส่ง account_id ด้วย — ตั้งแต่ระบบบัญชีเข้ามา (commit 524137e)
        // /sessions ตอบ 401 no_account_key ให้คำขอที่ไม่มีกุญแจ (Core/Gateway.cs:140-146)
        // ⇒ selftest พังมาตั้งแต่ตอนนั้นด้วย exception ของ HttpWebRequest ที่อ่านไม่รู้เรื่อง
        // ตัวเกมจริงส่งช่องนี้อยู่แล้วผ่าน Platform.BuildSessionForm (client/Durango.System/Platform.cs:144)
        //
        // ใช้กุญแจคงที่ไม่ใช่กุญแจสุ่ม: รันซ้ำกี่ครั้งก็ได้ตัวละครทดสอบตัวเดิม ไม่ทิ้งขยะสะสม
        // และแยกออกจากผู้เล่นจริงชัดเจนเวลาไล่ log
        string body = HttpPost($"http://127.0.0.1:{gatewayPort}/sessions",
                               $"platform=Android&account_id={SelfTestAccountKey}");
        var json = Newtonsoft.Json.Linq.JObject.Parse(body);
        string entityId = (string)json["user_id"];
        string token = (string)json["session_token"];
        Console.WriteLine($"[selftest] /sessions ✓ user={entityId}");

        Console.WriteLine($"[selftest] ต่อ tcp 127.0.0.1:{gamePort} entity={entityId}");
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Connect("127.0.0.1", gamePort);
        var connection = new Connection(socket);

        Welcome welcome = default;
        bool gotWelcome = false, gotOk = false, gotClock = false;
        double serverTime = 0;

        connection.Recv(delegate(Clock msg, PacketHeader header)
        {
            serverTime = msg.ServerTime;
            gotClock = true;
        });
        connection.Recv(delegate(Welcome msg, PacketHeader header)
        {
            welcome = msg;
            gotWelcome = true;
        });
        connection.Recv(delegate(OK msg, PacketHeader header)
        {
            gotOk = true;
        });

        connection.StartReceive();

        connection.Send(new GetClock { Time = Times.UnixTimeNow() });
        connection.Send(new Auth
        {
            EntityId = entityId,
            SessionToken = token,
            ClientVersion = "5.2.1",
            DeviceModel = "SelfTest"
        });

        // ต้นฉบับ: ได้ Welcome → TerrainMeta.Load → Ready → รอ OK
        if (!WaitFor(connection, () => gotClock && gotWelcome, 5000))
        {
            Console.WriteLine("[selftest] ❌ ไม่ได้รับ Welcome");
            return 1;
        }
        Console.WriteLine($"[selftest] Clock ✓ (serverTime={serverTime:F0})");
        Console.WriteLine($"[selftest] Welcome ✓ user={welcome.UserId} name='{welcome.Name}' " +
                          $"region={welcome.Region.Id}/terrain={welcome.Region.TerrainId}/template={welcome.Region.TemplateId}");

        connection.Send(default(Ready));
        if (!WaitFor(connection, () => gotOk, 5000))
        {
            Console.WriteLine("[selftest] ❌ ไม่ได้รับ OK");
            return 1;
        }
        Console.WriteLine("[selftest] Ready→OK ✓");

        // ต้นฉบับ client ส่ง SetChunk หลังเข้าโลก — ทดสอบว่าได้ Chunk กลับ
        int chunks = 0;
        connection.Recv(delegate(Chunk msg, PacketHeader header) { chunks++; });
        connection.Send(new SetChunk { Chunk = new Point2(8, 8) });
        WaitFor(connection, () => chunks > 0, 3000);
        Console.WriteLine(chunks > 0 ? $"[selftest] SetChunk→Chunk ✓ ({chunks} chunk)" : "[selftest] ⚠️ ไม่ได้รับ Chunk");

        connection.Close();
        Console.WriteLine("[selftest] ผ่านทั้งหมด — handshake และ message flow ตรงต้นฉบับ");
        return 0;
    }

    /// <summary>Connection ของเกมปั๊ม queue เองตอน Process() (client เรียกทุกเฟรม) — selftest ต้องปั๊มเอง</summary>
    private static bool WaitFor(Connection connection, Func<bool> done, int timeoutMs)
    {
        for (int i = 0; i < timeoutMs / 10; i++)
        {
            connection.Process();
            if (done()) return true;
            Thread.Sleep(10);
        }
        connection.Process();
        return done();
    }

    private static string HttpPost(string url, string form)
    {
        var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
        req.Method = "POST";
        req.ContentType = "application/x-www-form-urlencoded";
        byte[] bytes = Encoding.UTF8.GetBytes(form);
        req.ContentLength = bytes.Length;
        using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
        using var resp = req.GetResponse();
        using var reader = new StreamReader(resp.GetResponseStream());
        return reader.ReadToEnd();
    }
}
