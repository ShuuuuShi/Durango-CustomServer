using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Durango.Utils;
using Durango.Utils.Extensions;
using Messages;
using Newtonsoft.Json.Linq;
using Shared.Region;
using Yaml;
using Yaml.Util;

namespace Durango.Online;

// พอร์ตจาก nexonSRC/Durango.Online/Gateway.cs (HTTP 8190) + กาวมือถือที่จำเป็น
// ส่วนที่คงต้นฉบับ: /knock /notice /sessions /admission /entry /players /terrains/* + UnhandledUrl chunks
// ส่วนกาว (deviations — เอกสารเต็มใน docs/server/ServerNx.md):
//  1) /knock: ต้นฉบับชี้ CDN ของ Nexon (assetbundles.k.nexon.com / akamaized.net) — ที่นี่ชี้โฮสต์ตัวเอง
//     และเสิร์ฟไฟล์ bundle จากดิสก์ (client มือถือยังต้องโหลด asset bundles จริง)
//  2) /sessions: ต้นฉบับรับเฉพาะฟิลด์ "player" (LAN joiner) — เพิ่ม session token + สล็อตผู้เล่น
//     ให้รองรับคนหลายคนโดยคงรูปร่าง response ต้นฉบับ (user_id + session_token)
//  3) /entry: frontend_addresses ใช้ host จาก --public-host หรือ Host header (ต้นฉบับ: 127.0.0.1 คงที่)
public class Gateway
{
    public const int DefaultPort = 8190;

    private WebServer _webServer;

    private readonly GameServer _gameServer;

    private readonly WorldContext _worldCtx;

    private readonly PlayerContext _playerCtx;

    private readonly Host _host;

    public int Port { get; private set; }

    public string PublicHost { get; set; }

    public string AssetBundleAndroidDir { get; set; }

    /// <summary>
    /// [5 ก.ย. 2026] โฟลเดอร์ JSON ที่เสิร์ฟให้ /assets/* (ปกติ &lt;data&gt;/assets)
    ///
    /// ทำไมต้องมี: ตัวเกมโหลดตารางข้อมูลคนละทางตาม ClusterMode — ดู client/Yaml.Util/Loader.cs:164
    ///   Mode.Online  → HTTP GameManager.GatewayUrl + "/assets/&lt;ชื่อ&gt;"  (มาที่นี่)
    ///   โหมดอื่น     → Resources ในตัวเกม "offline/assets/&lt;ชื่อ&gt;"
    /// เซิร์ฟในตัวของเกมไม่มีเส้นนี้เพราะมันไม่เคยรันเป็น Online ⇒ พอเปิด Online แล้วต้องมี
    /// ไม่งั้นเกมค้างที่ CheckDataLoaded (Loader รีทราย 5 รอบต่อไฟล์ แล้วไม่ไปต่อ)
    /// </summary>
    public string AssetsDir { get; set; }

    private string _bundleIndexAndroidCache;

    public Gateway(Host host, GameServer gameServer, WorldContext worldCtx, PlayerContext playerCtx)
    {
        Port = 8190;
        _host = host;
        _gameServer = gameServer;
        _worldCtx = worldCtx;
        _playerCtx = playerCtx;
    }

    public void Start(int port)
    {
        Port = port;
        _webServer = new WebServer(port);
        RegisterRoutes();
    }

    public void Close() => _webServer?.Close();

    public void Process() => _webServer?.Process();

    private void RegisterRoutes()
    {
        _webServer.GetRoute["/knock"] = delegate(HttpListenerRequest request, Dictionary<string, string> postData)
        {
            string platform = PlatformKey(request.QueryString.Get("platform"));
            JObject jObject = new()
            {
                // ต้นฉบับ: CurrentBundleVersion.GetClientVersion() = "5.2.1"
                ["server_version"] = "5.2.1",
                ["compatible"] = true,
                ["assetbundle_index_url"] = $"{RootUrl(request)}/live/{platform}/Info.5.2.1.json",
                ["assetbundle_url_root"] = $"{RootUrl(request)}/live/{platform}/"
            };
            return new WebServer.JsonResponse(jObject.ToString());
        };

        _webServer.GetRoute["/notice"] = (HttpListenerRequest request, Dictionary<string, string> _) =>
            new WebServer.JsonResponse("{}");

        _webServer.PostRoute["/sessions"] = delegate(HttpListenerRequest request, Dictionary<string, string> postData)
        {
            // เส้นทางต้นฉบับ: LAN joiner ส่ง PlayerContext JSON ของตัวเองมาในฟิลด์ "player"
            string player = postData.Get("player");
            PlayerContext context = null;
            if (!string.IsNullOrEmpty(player))
            {
                try
                {
                    context = Json.Read<PlayerContext>(player);
                    context?.Initialize(null); // Path = null ⇒ ยังไม่เซฟ จนกว่า /players จะเลื่อนเป็นสล็อตจริง
                }
                catch (Exception e)
                {
                    Console.WriteLine("[gateway] /sessions player parse failed: " + e.Message);
                }
            }

            string remoteIp = request?.RemoteEndPoint?.Address?.ToString() ?? "?";
            if (context == null)
            {
                // ต้นฉบับ: ไม่มี player → ใช้ _playerCtx (โฮสต์) — เซิร์ฟเราไม่มี "โฮสต์" จึงสร้าง context ชั่วคราว
                context = _host.CreateTemporaryContext(null, null, null);
            }
            else if (_host.FindContextByEntityId(context.EntityId) is { } known)
            {
                // ตัวละครที่เคยเซฟไว้ — ใช้เซฟบนดิสก์เป็นหลัก (client ส่งมาแค่บางฟิลด์)
                context = known;
            }

            _gameServer.Register(context);
            string token = Guid.NewGuid().ToString("N");
            _gameServer.IssueSession(context.EntityId, token);
            Console.WriteLine($"[gateway] /sessions {remoteIp} → {context.PlayerInfo.PlayerName} ({context.EntityId})" +
                              (string.IsNullOrEmpty(context.Path) ? " [ชั่วคราว]" : ""));
            return new WebServer.JsonResponse(new JObject
            {
                ["user_id"] = context.EntityId,
                ["session_token"] = token
            }.ToString());
        };

        _webServer.GetRoute["/admission"] = (HttpListenerRequest request, Dictionary<string, string> _) =>
            new WebServer.JsonResponse(new JObject { ["admitted"] = true }.ToString());

        _webServer.GetRoute["/entry"] = delegate(HttpListenerRequest request, Dictionary<string, string> _)
        {
            // [5 ก.ย. 2026] ตัวเกมบอกที่นี่ว่าจะเล่นตัวละครไหน — /entry?entity_id=…&platform=…
            // (client/Durango.UI/TitleMenuGroup.cs:1039-1046) และยิงมาแบบ auth:true คือมี header
            // Authorization = session token ⇒ ย้าย token ให้ชี้ตัวละครนั้น ไม่งั้น Auth ฝั่ง TCP
            // จะปฏิเสธ เพราะ /sessions ออก token ให้ context ชั่วคราวไปก่อน (โหมด Online ไม่ส่ง "player")
            string entryEntity = request?.QueryString?["entity_id"];
            string entryToken = request?.Headers?["Authorization"];
            if (!string.IsNullOrEmpty(entryEntity) && _gameServer.BindSessionToEntity(entryToken, entryEntity))
            {
                Console.WriteLine($"[gateway] /entry ผูก session เข้ากับตัวละคร {entryEntity}");
            }

            string tcpHost = !string.IsNullOrEmpty(PublicHost)
                ? PublicHost
                : (request.UserHostName?.Split(':').FirstOrDefault() ?? "127.0.0.1");
            return new WebServer.JsonResponse(new JObject
            {
                ["frontend_addresses"] = new JArray($"{tcpHost}:{_gameServer.Port}"),
                ["cluster_mode"] = Host.ClusterMode.ToString()
            }.ToString());
        };

        _webServer.PostRoute["/players"] = delegate(HttpListenerRequest request, Dictionary<string, string> postData)
        {
            // ต้นฉบับ (prologue สร้างตัวละคร): name/region/job/gender/model_info → อัปเดต context + ใส่ชุดตามอาชีพ
            PlayerContext context = ResolveBySession(request) ?? _playerCtx;
            if (string.IsNullOrEmpty(context.PlayerInfo.PlayerEntityId))
            {
                context = _host.CreateTemporaryContext(null, null, null);
                _gameServer.Register(context);
            }
            context.PlayerInfo.PlayerName = postData.Get("name");
            List<string> regionTemplateIds = Singleton<Constants>.Instance?.PersonalRegion?.RegionTemplateIds
                                             ?? new List<string> { TerrainLoader.DefaultTerrainFile };
            if (regionTemplateIds.Count == 0) regionTemplateIds.Add(TerrainLoader.DefaultTerrainFile);
            int index = UnityEngine.Random.Range(0, regionTemplateIds.Count);
            string text = postData.Get("region");
            _worldCtx.TerrainId = !regionTemplateIds.Contains(text) ? regionTemplateIds[index] : text;
            UpdateAppearPlayer(context, postData);
            string[] bodyColor = context.AppearPlayer.Display.BodyColor;
            string[] array = { "clothes_engineer", "clothes_officeworker", "clothes_student", "clothes_farmer", "clothes_waiter", "clothes_soldier", "clothes_homeworker", "clothes_jobless" };
            int value = postData.Get("job").ToInt();
            value = Math.Clamp(value, 0, array.Length - 1);
            string prototypeId = array[value];
            Item? item = Cheats.MakeItem(prototypeId, 1);
            if (item.HasValue)
            {
                Item value2 = item.Value;
                if (bodyColor.Length >= 3)
                {
                    value2.ColorR = bodyColor[0];
                    value2.ColorG = bodyColor[1];
                    value2.ColorB = bodyColor[2];
                }
                context.InventoryItems.Add(value2);
                context.EquippedItems["body"] = value2.Id;
            }
            context = _host.PersistAsSlot(context);
            _worldCtx.Save();
            context.Save();
            Console.WriteLine($"[gateway] /players '{context.PlayerInfo.PlayerName}' job={prototypeId} → {context.EntityId}");
            return new WebServer.JsonResponse(new JObject { ["entity_id"] = context.EntityId }.ToString());
        };

        _webServer.PostRoute["/accounts"] = (HttpListenerRequest request, Dictionary<string, string> _) =>
            new WebServer.JsonResponse(Json.Write(_host.BuildAccount()));

        // /terrains/* ทั้งหมดจัดการใน UnhandledUrl เพราะชื่อเกาะเป็นตัวแปร (ดู TerrainRoute)

        _webServer.UnhandledUrl += UnhandledUrl;
    }

    /// <summary>
    /// [5 ก.ย. 2026] แผนที่ของเกาะ — <c>/terrains/&lt;ชื่อเกาะ&gt;</c> และ chunk ใต้เส้นนั้น
    ///
    /// ต้นฉบับจดเส้นทางเป็น "/terrains/1" ตายตัวได้เพราะมีโลกเดียว แต่ตัวเกมประกอบ URL จาก
    /// <c>Region.TerrainId</c> ที่เซิร์ฟส่งไปกับ Welcome ตรง ๆ โดยไม่ตรวจอะไร
    /// (client/Durango.Terrain/TerrainMeta.cs:130 · TerrainBase.cs:337 · MapSystem.cs:752)
    /// ⇒ พอส่งชื่อเกาะจริงไป เส้นทางก็กลายเป็น /terrains/ri35te/… ตามนั้น
    ///
    /// ⚠️ ต้องอ่านชื่อเกาะจาก URL ไม่ใช่จาก session token: chunk กับ terrain info ถูกขอแบบ
    /// **ไม่มี header Authorization** (มีเฉพาะ /whole_biomes) จึงระบุตัวผู้ขอไม่ได้
    /// ⚠️ และชื่อต้องต่างกันต่อเกาะจริง ๆ เพราะ chunk ขอด้วย disableCache:false
    /// (TerrainBase.cs:332) ⇒ ถ้าใช้ชื่อซ้ำ เกาะใหม่จะได้แผนที่เกาะเก่าจากแคชของ client
    ///
    /// รูปแบบ:  /terrains/&lt;id&gt;            → info.yml (TerrainInfoJson)
    ///          /terrains/&lt;id&gt;/whole_biomes → biome ทั้งแผ่น
    ///          /terrains/&lt;id&gt;/ocean|rivers/&lt;x&gt;,&lt;y&gt; → chunk เฉพาะชั้น
    ///          /terrains/&lt;id&gt;/&lt;x&gt;,&lt;y&gt;      → chunk รวม (biome+ocean+river+landmark)
    /// </summary>
    private WebServer.RouteFunction TerrainRoute(string url)
    {
        string rest = url.Substring("/terrains/".Length).Split('?')[0];
        int slash = rest.IndexOf('/');
        string regionId = slash < 0 ? rest : rest.Substring(0, slash);
        string tail = slash < 0 ? "" : rest.Substring(slash + 1);

        World world = _gameServer.Worlds?.GetOrCreate(regionId) ?? _gameServer.World;

        if (tail.Length == 0)
        {
            return (HttpListenerRequest _, Dictionary<string, string> __) =>
                new WebServer.JsonResponse(Json.Write(world.TerrainInfo));
        }
        if (tail.StartsWith("whole_biomes", StringComparison.OrdinalIgnoreCase))
        {
            return (HttpListenerRequest _, Dictionary<string, string> __) =>
                new WebServer.BinaryReponse { Content = world.Biomes };
        }
        if (tail.StartsWith("ocean", StringComparison.OrdinalIgnoreCase))
        {
            return (HttpListenerRequest _, Dictionary<string, string> __) =>
                new WebServer.BinaryReponse { Content = world.GetChunkOcean(GetPoint2FromUrl(url)) };
        }
        if (tail.StartsWith("rivers", StringComparison.OrdinalIgnoreCase))
        {
            return (HttpListenerRequest _, Dictionary<string, string> __) =>
                new WebServer.BinaryReponse { Content = world.GetChunkRiver(GetPoint2FromUrl(url)) };
        }
        return (HttpListenerRequest _, Dictionary<string, string> __) =>
        {
            Point2 chunk = GetPoint2FromUrl(url);
            byte[] biomes = world.GetChunkBiomes(chunk);
            byte[] ocean = world.GetChunkOcean(chunk);
            byte[] river = world.GetChunkRiver(chunk);
            byte[] landmark = world.GetChunkLandmark(chunk);
            var ms = new MemoryStream();
            ms.Write(biomes, 0, biomes.Length);
            ms.Write(ocean, 0, ocean.Length);
            ms.Write(river, 0, river.Length);
            if (landmark != null)
            {
                ms.Write(landmark, 0, landmark.Length);
            }
            return new WebServer.BinaryReponse { Content = ms.ToArray() };
        };
    }

    /// <summary>หา context จาก Authorization header (session token — client ใส่ทุก request แบบ auth)</summary>
    private PlayerContext ResolveBySession(HttpListenerRequest request)
    {
        string token = request?.Headers?["Authorization"];
        if (string.IsNullOrEmpty(token) || !_gameServer.TryGetSessionEntityId(token, out string entityId))
        {
            return null;
        }
        return _host.FindContextByEntityId(entityId) ?? _gameServer.GetPlayerContext(entityId);
    }

    /// <summary>ต้นฉบับ Gateway.UpdateAppearPlayer — เติมหน้าตาจาก model_info ที่ prologue ส่งมา</summary>
    private static void UpdateAppearPlayer(PlayerContext player, Dictionary<string, string> postData)
    {
        bool flag = postData.Get("gender") == "male";
        player.AppearPlayer.EntityType = (ushort)(!flag ? 1001 : 1000);
        string json = postData.Get("model_info");
        PlayerDisplay display = player.AppearPlayer.Display;
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                JObject model = JObject.Parse(json);
                display.Hair = (string)model["hair"];
                display.BodyColor = model["body_color"]?.ToObject<string[]>() ?? display.BodyColor;
                display.HeadColor = model["head_color"]?.ToObject<string[]>() ?? display.HeadColor;
                display.SkinColor = (string)model["skin_color"] ?? display.SkinColor;
                display.HairColor = (string)model["hair_color"] ?? display.HairColor;
                display.LipColor = (string)model["lip_color"] ?? display.LipColor;
                display.EyeColor = (string)model["eye_color"] ?? display.EyeColor;
                display.Portrait = (int?)model["portrait"] ?? display.Portrait;
                display.PortraitBg = (int?)model["portrait_bg"] ?? display.PortraitBg;
                display.PortraitBgColor = (string)model["portrait_bg_color"];
                display.Beard = (string)model["beard"];
                display.VoiceType = (int?)model["voice_type"] ?? display.VoiceType;
                display.BodySize = (float?)model["body_size"] ?? display.BodySize;
            }
            catch (Exception e)
            {
                Console.WriteLine($"[gateway] model_info parse failed: {e.Message} payload={json}");
            }
        }
        player.AppearPlayer.Display = display;
        player.AppearPlayer.Name = player.PlayerInfo.PlayerName;
        player.AppearPlayer.Level = player.PlayerInfo.PlayerLevel;
    }

    private string RootUrl(HttpListenerRequest request)
    {
        if (!string.IsNullOrEmpty(PublicHost))
        {
            return $"http://{PublicHost}:{Port}";
        }
        string host = request.UserHostName;
        if (string.IsNullOrEmpty(host))
        {
            return $"http://127.0.0.1:{Port}";
        }
        if (host.Contains(':')) return "http://" + host;
        return $"http://{host}:{Port}";
    }

    /// <summary>ต้นฉบับ Gateway.cs:46 — iPhonePlayer→ios, Android→android, อื่น ๆ→windows</summary>
    private static string PlatformKey(string platform)
    {
        if (platform == null) return "windows";
        if (platform.Equals("iPhonePlayer", StringComparison.OrdinalIgnoreCase)) return "ios";
        if (platform.Equals("Android", StringComparison.OrdinalIgnoreCase)) return "android";
        return "windows";
    }

    private WebServer.RouteFunction UnhandledUrl(string url)
    {
        // [5 ก.ย. 2026] ตารางข้อมูลเกมสำหรับโหมด Online — client/Yaml.Util/Loader.cs:155-185
        // ยิง GET <gateway>/assets/<ชื่อ> (ไม่มีนามสกุล) แล้ว Json.Read<T> ผลลัพธ์ตรง ๆ
        // ไฟล์จริงอยู่ที่ <AssetsDir>/<ชื่อ>.json — เกมขอ 71 เส้นทาง เรามีครบใน data/assets
        if (url.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
        {
            string assetsDir = AssetsDir;
            if (string.IsNullOrEmpty(assetsDir) || !Directory.Exists(assetsDir))
            {
                return null;
            }
            string relative = url.Substring("/assets/".Length);
            int qIdx = relative.IndexOf('?');
            if (qIdx != -1)
            {
                relative = relative.Substring(0, qIdx);
            }
            // กัน path traversal — client ขอแค่ <โฟลเดอร์>/<ชื่อ> ธรรมดา ไม่มี .. และไม่ใช่ path เต็ม
            if (relative.Length == 0 || relative.Contains("..") || Path.IsPathRooted(relative))
            {
                return (HttpListenerRequest _, Dictionary<string, string> __) => new WebServer.BadRequestResponse();
            }
            string assetPath = Path.GetFullPath(Path.Combine(assetsDir, relative.Replace('/', Path.DirectorySeparatorChar) + ".json"));
            string rootFull = Path.GetFullPath(assetsDir);
            if (!assetPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                return (HttpListenerRequest _, Dictionary<string, string> __) => new WebServer.BadRequestResponse();
            }
            return (HttpListenerRequest _, Dictionary<string, string> __) =>
            {
                if (!File.Exists(assetPath))
                {
                    // ⚠️ ขาดไฟล์ไหน เกมจะรีทราย 5 รอบแล้วค้างหน้าโหลด — ต้องดังพอให้เห็นใน log ทันที
                    Console.WriteLine($"[assets] 404 {relative}");
                    return new WebServer.NotFountResponse();
                }
                return new WebServer.BinaryReponse
                {
                    Content = File.ReadAllBytes(assetPath),
                    ContentType = "application/json"
                };
            };
        }

        // client ประกอบ URL ตามรูปแบบ CDN เดิม: /{live|release}/{platform}/<ไฟล์> (จาก /knock URLs)
        if (url.StartsWith("/live/", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("/release/", StringComparison.OrdinalIgnoreCase))
        {
            string[] seg = url.Split(new[] { '/' }, 4, StringSplitOptions.RemoveEmptyEntries);
            if (seg.Length == 3)
            {
                url = (PlatformKey(seg[1]) == "android" ? "/assetbundles/android/" : "/assetbundles/") + seg[2];
            }
        }

        if (url.StartsWith("/assetbundles/android/"))
        {
            if (string.IsNullOrEmpty(AssetBundleAndroidDir) || !Directory.Exists(AssetBundleAndroidDir))
            {
                return null;
            }
            string aName = Path.GetFileName(url.Substring("/assetbundles/android/".Length).Split('?')[0]);
            if (string.IsNullOrEmpty(aName) || aName.Contains(".."))
            {
                return (HttpListenerRequest request, Dictionary<string, string> postData) => new WebServer.BadRequestResponse();
            }
            string aPath = Path.Combine(AssetBundleAndroidDir, aName);
            return (HttpListenerRequest request, Dictionary<string, string> postData) =>
            {
                if (File.Exists(aPath))
                {
                    return new WebServer.BinaryReponse { Content = File.ReadAllBytes(aPath) };
                }
                string resolvedA = ResolveBundleIgnoringHash(aName, AssetBundleAndroidDir);
                if (resolvedA != null)
                {
                    return new WebServer.BinaryReponse { Content = File.ReadAllBytes(resolvedA) };
                }
                // soundbank พากย์เสียงแยกภาษา: ชุด Android มีแค่ en_us — เสิร์ฟ en_us แทนทุกภาษา
                string fallbackA = ResolveVoiceBankFallback(aName, AssetBundleAndroidDir);
                if (fallbackA != null)
                {
                    Console.WriteLine("[assetbundle-android] {0} ไม่มี ⇒ เสิร์ฟ en_us แทน", aName);
                    return new WebServer.BinaryReponse { Content = File.ReadAllBytes(fallbackA) };
                }
                Console.WriteLine("[assetbundle-android] 404 {0}", aName);
                return new WebServer.NotFountResponse();
            };
        }

        if (url.StartsWith("/terrains/", StringComparison.OrdinalIgnoreCase))
        {
            return TerrainRoute(url);
        }

        if (url.StartsWith("/assetbundles/android/"))
        {
            if (string.IsNullOrEmpty(AssetBundleAndroidDir) || !Directory.Exists(AssetBundleAndroidDir))
            {
                return null;
            }
            string aName = Path.GetFileName(url.Substring("/assetbundles/android/".Length).Split('?')[0]);
            if (string.IsNullOrEmpty(aName) || aName.Contains(".."))
            {
                return (HttpListenerRequest request, Dictionary<string, string> postData) => new WebServer.BadRequestResponse();
            }
            string aPath = Path.Combine(AssetBundleAndroidDir, aName);
            return (HttpListenerRequest request, Dictionary<string, string> postData) =>
            {
                if (File.Exists(aPath))
                {
                    return new WebServer.BinaryReponse { Content = File.ReadAllBytes(aPath) };
                }
                string resolvedA = ResolveBundleIgnoringHash(aName, AssetBundleAndroidDir);
                if (resolvedA != null)
                {
                    return new WebServer.BinaryReponse { Content = File.ReadAllBytes(resolvedA) };
                }
                // soundbank พากย์เสียงแยกภาษา: ชุด Android มีแค่ en_us — เสิร์ฟ en_us แทนทุกภาษา
                string fallbackA = ResolveVoiceBankFallback(aName, AssetBundleAndroidDir);
                if (fallbackA != null)
                {
                    Console.WriteLine("[assetbundle-android] {0} ไม่มี ⇒ เสิร์ฟ en_us แทน", aName);
                    return new WebServer.BinaryReponse { Content = File.ReadAllBytes(fallbackA) };
                }
                Console.WriteLine("[assetbundle-android] 404 {0}", aName);
                return new WebServer.NotFountResponse();
            };
        }

        return (HttpListenerRequest request, Dictionary<string, string> _) => new WebServer.BadRequestResponse();
    }

    /// <summary>client ขอ <ชื่อ>.<crc>.bundle — ถ้า crc ไม่ตรงไฟล์บนดิสก์ หาด้วย "ชื่อตัด hash"</summary>
    private static string ResolveBundleIgnoringHash(string requestedName, string dir)
    {
        const string suffix = ".bundle";
        if (!requestedName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
        string stem = requestedName.Substring(0, requestedName.Length - suffix.Length);
        int lastDot = stem.LastIndexOf('.');
        if (lastDot <= 0) return null;
        string prefix = stem.Substring(0, lastDot + 1);
        try
        {
            return Directory.GetFiles(dir, prefix + "*.bundle").FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>เสียงพากย์: soundbanks$android$<lang>$voice_*.bnk — เซิร์ฟมีแค่ en_us</summary>
    private static string ResolveVoiceBankFallback(string requestedName, string dir)
    {
        const string marker = "$android$";
        int idx = requestedName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        int langStart = idx + marker.Length;
        int langEnd = requestedName.IndexOf('$', langStart);
        if (langEnd < 0 || !requestedName.Contains("$voice_")) return null;
        string fallback = requestedName.Substring(0, langStart) + "en_us" + requestedName.Substring(langEnd);
        string path = Path.Combine(dir, fallback);
        return File.Exists(path) ? path : null;
    }

    private static Point2 GetPoint2FromUrl(string url)
    {
        int num = url.LastIndexOf("/", StringComparison.Ordinal) + 1;
        string[] array = url.Substring(num, url.Length - num).Split(',');
        return new Point2(array[0].ToInt(), array[1].ToInt());
    }
}
