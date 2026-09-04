using System;
using System.IO;
using System.Threading;
using Durango.Online;
using Durango.Utils;
using Durango.Utils.Extensions;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

// DurangoServerNx — เซิร์ฟเกมพอร์ตตรงจากเซิร์ฟในตัวของตัวเกม (nexonSRC/Durango.Online)
// รองรับ client มือถือ (Android 5.2.1 แท้) เป็นหลัก — โครงร่างและรูปแบบข้อมูลตามต้นฉบับ
//
// การใช้งาน:
//   DurangoServerNx [--name <ชื่อ cluster>] [--gateway-port 8190] [--game-port 8191]
//                   [--data <โฟลเดอร์ data>] [--terrains <โฟลเดอร์ terrain zip>]
//                   [--assetbundles-android <โฟลเดอร์ bundle>] [--public-host <ip/ชื่อ>]
//                   [--max-players N] [--cluster-mode Offline|Online|Editable]
internal static class Program
{
    private static int _ticksPerSecond = 120;

    /// <summary>host ที่กำลังรัน — ให้ตัวจัดการปิดเครื่องเซฟได้ก่อนออก</summary>
    private static Host _host;

    private static int _shutdownDone;

    /// <summary>เซฟทุกอย่างแล้วปิดให้เรียบร้อย — เรียกซ้ำได้ (ทำจริงครั้งเดียว)</summary>
    private static void ShutdownSafely(string reason)
    {
        if (System.Threading.Interlocked.Exchange(ref _shutdownDone, 1) != 0) return;
        try
        {
            Console.WriteLine($"[boot] ปิดเซิร์ฟ ({reason}) — เซฟก่อน...");
            _host?.SaveAll();
            _host?.Close();
            Console.WriteLine("[boot] เซฟเรียบร้อย");
        }
        catch (Exception e)
        {
            Console.WriteLine("[boot] ⚠️ เซฟตอนปิดไม่สำเร็จ: " + e.Message);
        }
    }

    private static int Main(string[] args)
    {
        // [5 ก.ย. 2026] เดิม Ctrl+C เรียก Environment.Exit(0) ทันทีโดยไม่เซฟ และ Host.Close()
        // (ตัวที่เซฟโลกก่อนปิด) ไม่เคยถูกเรียกจากที่ไหนเลย ⇒ **รีสตาร์ทเซิร์ฟทุกครั้ง ของหายได้ถึง 60 วิ**
        // (รอบ autosave) ตอนนี้เซฟก่อนออกเสมอ ทั้งทาง Ctrl+C, ปิด process ปกติ และ crash
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            ShutdownSafely("Ctrl+C");
            Environment.Exit(0);
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ShutdownSafely("ปิดโปรเซส");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Console.WriteLine("[boot] ❌ exception ที่ไม่มีใครรับ: " + (args.ExceptionObject as Exception)?.Message);
            ShutdownSafely("crash");
        };
        try
        {
            // คอนโซล Windows default codepage อ่านไทยไม่ได้
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (Exception) { }

        // ---- CLI ----
        string name = "nx";
        int gatewayPort = Gateway.DefaultPort;   // 8190 — ค่าแท้ที่ client มือถือฝังมาในตัว
        int gamePort = GameServer.DefaultPort;   // 8191
        string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        string androidBundles = null;
        string publicHost = null;
        int maxPlayers = 200;
        // token ของ /health — เอาจาก env ได้ด้วย จะได้ไม่ต้องโผล่ในบรรทัดคำสั่ง (ps เห็นหมด)
        string adminToken = Environment.GetEnvironmentVariable("DURANGO_ADMIN_TOKEN");

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--selftest":
                {
                    int stGateway = 18290, stGame = 18291;
                    while (i + 1 < args.Length)
                    {
                        if (args[i + 1] == "--gateway-port") stGateway = int.Parse(args[i + 2]);
                        if (args[i + 1] == "--game-port") stGame = int.Parse(args[i + 2]);
                        i++;
                    }
                    return SelfTest.Run(stGateway, stGame);
                }
                case "--name": name = args[++i]; break;
                case "--gateway-port": gatewayPort = int.Parse(args[++i]); break;
                case "--game-port": gamePort = int.Parse(args[++i]); break;
                case "--data": dataDir = args[++i]; break;
                case "--terrains": TerrainLoader.TerrainDir = args[++i]; break;
                case "--terrain": TerrainLoader.DefaultTerrainFile = args[++i]; break;
                case "--assetbundles-android": androidBundles = args[++i]; break;
                case "--public-host": publicHost = args[++i]; break;
                case "--url-prefix":
                {
                    // [4 ก.ย. 2026] client มือถือฝัง "8190" มาต่อท้าย literal เอง (int แก้ค่าไม่ได้) ⇒ ถ้าจะรัน
                    // เซิร์ฟนี้บนพอร์ตอื่น (เช่นแชร์เครื่องเดียวกับ server/ เดิมที่คุม 8190 อยู่แล้ว) ต้องแพตช์ literal
                    // เป็น "http://ip:<พอร์ตจริง>/p" แล้วตัด prefix "/p8190" ทิ้งก่อน route (ดู docs/server/Android.md)
                    string prefix = args[++i].Trim();
                    if (!prefix.StartsWith("/")) prefix = "/" + prefix;
                    Durango.Online.WebServer.PathPrefix = prefix.TrimEnd('/');
                    break;
                }
                case "--max-players": maxPlayers = int.Parse(args[++i]); break;
                case "--admin-token": adminToken = args[++i]; break;
                case "--tps": _ticksPerSecond = int.Parse(args[++i]); break;
                case "--cluster-mode":
                    Host.ClusterMode = args[++i].ToEnum(Durango.Logic.Clusters.Mode.Offline);
                    break;
                case "--help":
                case "-h":
                    Console.WriteLine("DurangoServerNx — เซิร์ฟแท้พอร์ตตรง · มือถือก่อน");
                    Console.WriteLine("  --name, --gateway-port, --game-port, --data, --terrains, --terrain,");
                    Console.WriteLine("  --assetbundles-android, --public-host, --url-prefix, --max-players, --tps, --cluster-mode,");
                    Console.WriteLine("  --admin-token <t>   token ของ /health (หรือ env DURANGO_ADMIN_TOKEN) — ไม่ตั้ง = เรียกได้เฉพาะเครื่องตัวเอง");
                    return 0;
            }
        }

        Console.WriteLine("=== DurangoServerNx (เซิร์ฟแท้พอร์ตตรง · มือถือก่อน) ===");
        Console.WriteLine($"[boot] data={dataDir} terrains={TerrainLoader.TerrainDir}");

        // ---- game data (เทียบเท่า Loader ของ client) ----
        DataStore.Load(dataDir);

        // สารบัญเกาะ — ระบบล่องเรือใช้ตอบว่าจากท่าเรือนี้ไปไหนได้บ้าง (ต้องหลัง TerrainLoader.TerrainDir)
        RegionCatalog.Load(Path.Combine(dataDir, "assets"));

        // ---- host + saves ----
        // AppData (เซฟ .player/.world) อยู่ข้าง ๆ data เหมือนเกมเก็บ AppData ของมันเอง
        AppData.BasePath = Path.GetFullPath(Path.Combine(dataDir, "..", "AppData-nx"));
        var host = new Host(name);
        _host = host;
        // [5 ก.ย. 2026] ค่าพวกนี้ต้องตั้ง **ก่อน** host.Start() เพราะ Start เป็นคนสร้าง Gateway
        // แล้วส่ง AdminToken ต่อให้ตอนนั้น (เดิม --max-players ถูกพิมพ์ออกจอเฉย ๆ ไม่มีใครใช้)
        host.MaxPlayers = maxPlayers;
        host.AdminToken = adminToken;
        host.Load();

        try
        {
            // assets = ตารางข้อมูลเกมที่ client โหลดผ่าน HTTP เมื่อ cluster_mode = Online
            // (client/Yaml.Util/Loader.cs:164 — โหมดอื่นมันอ่านจาก Resources ในตัวเกมแทน)
            host.Start(gamePort, gatewayPort, publicHost, androidBundles, Path.Combine(dataDir, "assets"));
        }
        catch (Exception e)
        {
            Console.WriteLine($"[boot] ❌ เปิดพอร์ตไม่สำเร็จ: {e.Message}");
            Console.WriteLine("       (8190 ต้อง netsh urlacl หรือรันด้วยสิทธิ์ที่พอ — ดู docs/server/ServerNx.md)");
            return 1;
        }

        Console.WriteLine($"[boot] พร้อม — gateway http://0.0.0.0:{gatewayPort} · game tcp:{gamePort} · " +
                          $"cluster_mode={Host.ClusterMode} · max-players={maxPlayers}");
        Console.WriteLine("[boot] มือถือ: ต่อ gateway port 8190 ตามที่ APK ฝังมา (หรือ --url-prefix ถ้าเปลี่ยนพอร์ต)");
        Console.WriteLine(string.IsNullOrEmpty(adminToken)
            ? "[boot] /health เปิดเฉพาะ 127.0.0.1 (ยังไม่ได้ตั้ง --admin-token)"
            : "[boot] /health ต้องมี ?token=… (ตั้งจาก --admin-token/env แล้ว)");

        // ---- main loop (ต้นฉบับ: GameManager.Update → Server.Process ทุกเฟรม; เซิร์ฟรันคงที่ 120 TPS) ----
        int frameMs = 1000 / _ticksPerSecond;
        long lastSave = 0;
        int loopErrors = 0;
        ServerMetrics.MarkBoot();
        while (true)
        {
            // [5 ก.ย. 2026] จับเวลาต่อรอบให้ /health อ่าน — ใช้ Stopwatch.GetTimestamp() ซึ่งเป็นการ
            // อ่านตัวนับของ CPU ตรง ๆ (ระดับ 20 ns) ไม่ได้สร้าง object อะไร ⇒ ใส่ในลูป 120 รอบ/วิ ได้
            // เก็บ 2 ค่า: เวลาทำงานจริง (host.Process) กับเวลารอบเต็ม (รวม sleep + เซฟ)
            // เพราะอาการ "tps ตก" อาจมาจากงานล้น หรือจาก GC ที่หยุดโลกตอนไหนก็ได้
            long tickBegin = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                host.Process();
            }
            catch (Exception e)
            {
                // ต้องดังพอให้เห็น ไม่ใช่กลืนเงียบ ๆ — และถ้าพังรัว ๆ ให้ยอมตายเพื่อไม่ให้วนเสียหาย
                loopErrors++;
                ServerMetrics.RecordLoopError();
                Console.WriteLine($"[loop] ⚠️ ข้อผิดพลาดรอบที่ {loopErrors}: {e}");
                if (loopErrors >= 100)
                {
                    Console.WriteLine("[loop] ❌ ผิดพลาดถี่เกินไป — ปิดเซิร์ฟ");
                    ShutdownSafely("ข้อผิดพลาดถี่เกินไป");
                    return 1;
                }
            }
            long workEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            Thread.Sleep(frameMs);

            // เซฟโลกทุก 60 วิ (ต้นฉบับเซฟทันทีทุก event — เพิ่มเข็มขัดนิรภัยเหมือนเซิร์ฟเดิม)
            long now = Environment.TickCount64;
            if (now - lastSave > 60_000)
            {
                lastSave = now;
                host.SaveAll();
            }
            ServerMetrics.RecordTick(System.Diagnostics.Stopwatch.GetTimestamp() - tickBegin, workEnd - tickBegin);
        }
    }

    private static Durango.Logic.Clusters.Mode ToEnum(this string s, Durango.Logic.Clusters.Mode def) =>
        Enum.TryParse(s, ignoreCase: true, out Durango.Logic.Clusters.Mode v) ? v : def;
}
