using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using Durango.Network;
using Durango.Online;
using Durango.Utils;
using Messages;

namespace DurangoServerNx;

// --probe: ตรวจว่า handler ของแพ็กเกจ "ตอบจริง" ไม่ใช่แค่ "ลงทะเบียนไว้"
//   จำลอง client ต่อ game port จริง (เหมือน --selftest) แล้วยิง message ทุกชนิดใน Messages
//   ทีละชุด วัดผลจากว่าเซิร์ฟตอบกลับมาบน ReplyOf เดียวกันไหม (PacketHeader.ReplyOf = seq ของคำถาม)
//
// ทำไมต้องมี: build ผ่าน + log ไม่มี "[conn] ไม่มี handler" พิสูจน์แค่ว่า "ครบ" — ไม่ได้พิสูจน์ว่า
//   handler ตอบกลับจริง (เช่นเมธอดที่โยน exception กลางทางจะเงียบโดยไม่มีใครรู้)
//
// ตีความผลต่อ TypeCode:
//   ok:<ชื่อ>          = มี handler ตอบข้อมูลกลับมาจริง
//   abort             = ได้ Abort/Error กลับ — เป็นได้ 2 กรณี: (ก) handler ปฏิเสธ payload ว่าง (ปกติ)
//                       (ข) ไม่มี handler แล้ว ServerPlayer ตอบ Abort แทน ⇒ ให้เทียบ log เซิร์ฟหา
//                       "[conn] ไม่มี handler สำหรับ type=N" (พิมพ์ครั้งเดียวต่อ type)
//   ไม่ตอบ             = handler เงียบ (มักโยน exception กลางทางก่อนได้ Send) — ต้องไล่แก้
//   ตัดการเชื่อมต่อ      = ข้อความนี้ทำ session ตาย (เซิร์ฟปิด connection หลังรับ) — ต้องไล่แก้
//
// รัน: DurangoServerNx --probe [--gateway-port N] [--game-port N] — เซิร์ฟต้องกำลังรันอยู่
//      ใช้บัญชี selftest-local เดียวกับ --selftest ⇒ ผลข้างเคียงจาก payload ว่างลงบัญชีทดสอบ ไม่กระทบผู้เล่นจริง
//
// ตัวแปรดีบั๊ก: PROBE_LIMIT=N (ยิงแค่ N ตัวแรก) · PROBE_VERBOSE=1 (พ่นทุก message ที่รับ)
internal static class SelfTestPackages
{
    private const string AccountKey = "selftest-local";

    // ห้ามยิงกลาง session — default(Auth) token ว่าง ⇒ เซิร์ฟปฏิเสธแล้วปิด connection ทิ้ง
    // (GetClock/Ready ใช้เองตอน handshake ยิงซ้ำก็ไม่มีประโยชน์)
    private static readonly HashSet<string> NeverSend = new HashSet<string> { "Auth", "GetClock", "Ready" };

    private static int _gatewayPort, _gamePort;
    private static int _sentCount;                                        // จำลองตัวนับของ Connection (ครั้งที่ n ได้ seq n)
    private static Dictionary<uint, List<string>> _inbox;                 // ReplyOf → ชื่อ message ที่ตอบกลับมา
    private static readonly Dictionary<Type, MethodInfo> SendCache = new();
    private static readonly Dictionary<Type, MethodInfo> WatchCache = new();

    public static int Run(int gatewayPort, int gamePort)
    {
        _gatewayPort = gatewayPort;
        _gamePort = gamePort;

        var all = CollectTypes();
        var types = ApplyLimit(all);
        var results = new Dictionary<uint, string>();

        var connection = Connect(all);
        if (connection == null)
        {
            Console.WriteLine("[probe] ❌ ต่อเซิร์ฟไม่สำเร็จ (handshake ไม่ผ่าน)");
            return 1;
        }
        Console.WriteLine($"[probe] ต่อเข้าเกมพอร์ต {gamePort} — จะยิง {types.Count} ชนิด (ข้าม: {string.Join(", ", NeverSend)})");

        // ยิงเป็นชุด ๆ — ตัวที่เงียบยิงซ้ำอีกรอบ (กันตกหล่นจากจังหวะที่เซิร์ฟกำลังโหลดข้อมูล)
        const int rounds = 2;
        const int batchSize = 12;
        for (int round = 1; round <= rounds; round++)
        {
            var todo = new List<(uint Code, Type T)>();
            foreach (var item in types)
            {
                // รอบถัดไปยิงซ้ำเฉพาะตัวที่ยังไม่มีคำตอบเป็นข้อมูล
                if (!results.TryGetValue(item.Code, out var prev) || !prev.StartsWith("ok")) todo.Add(item);
            }
            if (todo.Count == 0) break;
            Console.WriteLine($"[probe] รอบ {round}/{rounds} — เหลือ {todo.Count} ชนิดที่ยังไม่มีคำตอบเป็นข้อมูล");

            for (int i = 0; i < todo.Count; i += batchSize)
            {
                int count = Math.Min(batchSize, todo.Count - i);
                var slice = todo.GetRange(i, count);

                if (!connection.Connected()) connection = Connect(all);
                if (connection == null)
                {
                    Console.WriteLine("[probe] ❌ เชื่อมใหม่ไม่สำเร็จ — หยุดตรวจ");
                    return 1;
                }

                var seqs = new uint[count];
                for (int j = 0; j < count; j++) seqs[j] = SendTrackedType(connection, slice[j].T);
                WaitFor(connection, () =>
                {
                    foreach (uint s in seqs) if (!Has(s)) return false;
                    return true;
                }, 1500);
                for (int j = 0; j < count; j++) KeepBest(results, slice[j].Code, Classify(seqs[j]));

                if (connection.Connected()) continue;

                // เซิร์ฟปิด connection ระหว่างชุดนี้ ⇒ ยิงทีละตัวบน connection ใหม่เพื่อหาตัวร้าย
                Console.WriteLine("[probe] ⚠️ เซิร์ฟปิดการเชื่อมต่อระหว่างชุดนี้ — ยิงเดี่ยวเพื่อหาตัวร้าย");
                foreach (var (code, t) in slice)
                {
                    if (results.TryGetValue(code, out var got) && got.StartsWith("ok")) continue;
                    connection = Connect(all);
                    if (connection == null)
                    {
                        Console.WriteLine($"[probe] ❌ เชื่อมใหม่ไม่สำเร็จหลัง {t.Name} — หยุดตรวจ");
                        return 1;
                    }
                    uint s = SendTrackedType(connection, t);
                    WaitFor(connection, () => Has(s), 800);
                    string verdict = Classify(s) ?? "ไม่ตอบ";
                    if (!connection.Connected()) verdict = "ตัดการเชื่อมต่อ";
                    KeepBest(results, code, verdict);
                }
            }
        }

        // รายงานทีละ TypeCode (พ่นออก stdout — ชี้ไฟล์เก็บไว้ได้)
        int ok = 0, abort = 0, silent = 0, killer = 0;
        foreach (var (code, t) in types)
        {
            string v = results.TryGetValue(code, out var got) ? got : "ไม่ตอบ";
            if (v.StartsWith("ok")) ok++;
            else if (v == "abort") abort++;
            else if (v == "ตัดการเชื่อมต่อ") killer++;
            else silent++;
            Console.WriteLine($"[probe] {code}\t{t.Name}\t{v}");
        }
        Console.WriteLine($"[probe] สรุป: ตอบข้อมูล {ok} · ตอบ Abort/Error {abort} · เงียบ {silent} · ตัดการเชื่อมต่อ {killer} · รวม {types.Count}");
        Console.WriteLine("[probe] ตัว abort ให้เทียบ log เซิร์ฟ \"ไม่มี handler สำหรับ type=\" — ถ้าไม่มีบรรทัดนั้น = handler ปฏิเสธ payload ว่างตามปกติ");
        connection?.Close();
        return silent + killer == 0 ? 0 : 2;
    }

    /// <summary>เก็บคำตอบที่ดีที่สุดไว้ (ok &gt; อย่างอื่น) — ตัวที่ยิงหลายรอบใช้คำตอบที่ดีที่สุดเป็นผล</summary>
    private static void KeepBest(Dictionary<uint, string> results, uint code, string verdict)
    {
        if (verdict == null) return;
        if (!results.TryGetValue(code, out var prev) || Rank(verdict) > Rank(prev)) results[code] = verdict;
    }

    private static int Rank(string verdict) => verdict.StartsWith("ok") ? 2 : 1;

    // ── การเชื่อมต่อ ────────────────────────────────────────────────────────────

    /// <summary>
    /// ต่อใหม่ทั้งชุด: /sessions → tcp → ลง watcher ทุกชนิด → GetClock/Auth/Ready
    /// คืน null ถ้า handshake ไม่ผ่าน (ตอนเรียก connection เดิมถือว่าตายแล้ว)
    /// </summary>
    private static Connection Connect(List<(uint Code, Type T)> types)
    {
        try
        {
            string body = HttpPost($"http://127.0.0.1:{_gatewayPort}/sessions",
                                   $"platform=Android&account_id={AccountKey}");
            var json = Newtonsoft.Json.Linq.JObject.Parse(body);
            string entityId = (string)json["user_id"];
            string token = (string)json["session_token"];

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect("127.0.0.1", _gamePort);
            var connection = new Connection(socket);
            _inbox = new Dictionary<uint, List<string>>();
            _sentCount = 0;   // Connection ใหม่นับ seq ใหม่จาก 1 — ตัวนับของเราต้องเริ่มใหม่ตาม

            foreach (var (_, t) in types)
            {
                try { RegisterWatch(t, connection); }
                catch (Exception e) { Console.WriteLine($"[probe] ⚠️ ผูก watcher {t.Name} ไม่ได้: {e.InnerException?.Message ?? e.Message}"); }
            }
            connection.StartReceive();

            uint sClock = SendTracked(connection, new GetClock { Time = Times.UnixTimeNow() });
            uint sAuth = SendTracked(connection, new Auth
            {
                EntityId = entityId,
                SessionToken = token,
                ClientVersion = "5.2.1",
                DeviceModel = "Probe"
            });
            if (!WaitFor(connection, () => Has(sClock) && Has(sAuth), 5000)) { connection.Close(); return null; }
            uint sReady = SendTracked(connection, default(Ready));
            if (!WaitFor(connection, () => Has(sReady), 5000)) { connection.Close(); return null; }
            return connection;
        }
        catch (Exception e)
        {
            Console.WriteLine("[probe] ต่อไม่ได้: " + e.Message);
            return null;
        }
    }

    // ── รายชื่อ message ────────────────────────────────────────────────────────

    /// <summary>รวม message ทุกชนิดที่มี TypeCode (ใช้ลง watcher ทั้งหมด) — ตัดชนิดห้ามยิงออก</summary>
    private static List<(uint Code, Type T)> CollectTypes()
    {
        // GetTypes() โยน ReflectionTypeLoadException ถ้ามี type ที่โหลดไม่ได้ (EnumUnion32 =
        // generic ที่มี explicit layout — ไม่ใช่ message) ⇒ ใช้รายการที่โหลดได้บางส่วน
        Type[] loaded;
        try { loaded = typeof(SelfTestPackages).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { loaded = ex.Types; }

        var all = new List<(uint Code, Type T)>();
        foreach (Type t in loaded)
        {
            if (t == null || t.Namespace != "Messages" || !t.IsValueType) continue;
            FieldInfo f = t.GetField("TypeCode", BindingFlags.Public | BindingFlags.Static);
            if (f == null || !f.IsLiteral) continue;
            all.Add(((uint)f.GetRawConstantValue(), t));
        }
        all.Sort((a, b) => a.Code.CompareTo(b.Code));

        var types = new List<(uint Code, Type T)>();
        foreach (var item in all)
        {
            if (NeverSend.Contains(item.T.Name)) continue;
            types.Add(item);
        }
        return types;
    }

    /// <summary>PROBE_LIMIT — ตัดให้เหลือ N ตัวแรกเพื่อดีบั๊ก (ตัดจากลิสต์ที่ "ยิง" เท่านั้น — watcher ยังจับทุกชนิด)</summary>
    private static List<(uint Code, Type T)> ApplyLimit(List<(uint Code, Type T)> types)
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("PROBE_LIMIT"), out int limit) && limit > 0 && types.Count > limit)
            return types.GetRange(0, limit);
        return types;
    }

    // ── ส่ง/รับ ────────────────────────────────────────────────────────────────

    private static string Classify(uint seq)
    {
        lock (_inbox)
        {
            if (!_inbox.TryGetValue(seq, out var list) || list.Count == 0) return null;
            string name = list[0];
            return (name == "Abort" || name == "Error") ? "abort" : "ok:" + name;
        }
    }

    private static bool Has(uint seq)
    {
        lock (_inbox) return _inbox.ContainsKey(seq);
    }

    /// <summary>Connection ปั๊ม seq เริ่ม 1 เพิ่มทุกครั้งที่ Send ⇒ ครั้งที่ n ได้ seq n — นับเองให้ตรงกัน</summary>
    private static uint SendTrackedType(Connection conn, Type t)
    {
        if (!SendCache.TryGetValue(t, out var mi))
        {
            mi = typeof(SelfTestPackages).GetMethod(nameof(SendOne), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(t);
            SendCache[t] = mi;
        }
        return (uint)mi.Invoke(null, new[] { conn, Activator.CreateInstance(t) });
    }

    private static uint SendOne<T>(Connection conn, T msg)
    {
        return SendTracked(conn, msg);
    }

    private static uint SendTracked<T>(Connection conn, T msg)
    {
        uint seq = (uint)Interlocked.Increment(ref _sentCount);
        conn.Send(msg);
        return seq;
    }

    private static void RegisterWatch(Type t, Connection conn)
    {
        if (!WatchCache.TryGetValue(t, out var mi))
        {
            mi = typeof(SelfTestPackages).GetMethod(nameof(WatchOne), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(t);
            WatchCache[t] = mi;
        }
        mi.Invoke(null, new[] { conn });
    }

    private static void WatchOne<T>(Connection conn)
    {
        conn.Recv<T>((msg, header) =>
        {
            if (Environment.GetEnvironmentVariable("PROBE_VERBOSE") == "1")
                Console.WriteLine($"[watch] type={typeof(T).Name} seq={header.Seq} replyOf={header.ReplyOf}");
            lock (_inbox)
            {
                if (!_inbox.TryGetValue(header.ReplyOf, out var list)) _inbox[header.ReplyOf] = list = new List<string>();
                list.Add(typeof(T).Name);
            }
        });
    }

    /// <summary>Connection ของเกมปั๊ม queue เองตอน Process() — probe ต้องปั๊มเองเหมือน SelfTest</summary>
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
