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

    /// <summary>
    /// [5 ก.ย. 2026] รหัสผ่านของเส้นทางสำหรับคนดูแล (ตอนนี้มีแต่ /health) — Host ส่งค่าให้ตอนสร้าง
    /// ว่าง = ให้เรียกได้เฉพาะจากเครื่องตัวเอง (loopback) ดู <see cref="IsAdminAllowed"/>
    /// </summary>
    public string AdminToken { get; set; }

    /// <summary>
    /// โฟลเดอร์ data ของเซิร์ฟ — ใช้สำหรับอ่าน/เขียน config files ใน admin API
    /// </summary>
    public string DataDir { get; set; }

    /// <summary>
    /// เวอร์ชันตัวเกมต่ำสุดที่ยอมให้เข้า — <c>null</c> = รับทุกเวอร์ชัน (ค่าตั้งต้น)
    /// ตั้งด้วย <c>--min-client-version</c> · ดูเหตุผลที่ยังไม่บังคับโดยปริยายที่เส้น /knock
    /// </summary>
    public string MinClientVersion { get; set; }

    /// <summary>ลิงก์ให้ผู้เล่นไปโหลดตัวเกมใหม่ — ต้องมีถ้าจะเปิดด่านเวอร์ชัน</summary>
    public string DownloadUrl { get; set; }

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

            // [6 ก.ย. 2026] ด่านเวอร์ชัน — ตอบ compatible ตามที่ตัวเกมส่งมาจริง ไม่ใช่ true ตายตัว
            //
            // ⚠️ เดิมตอบ true เสมอและไม่เคยอ่าน ?version= ที่เกมส่งมาเลย ⇒ ตัวเกมเวอร์ชันเก่า
            // ต่อเข้ามาได้ แล้วค่าที่ unpack เพี้ยนถูกเขียนลงไฟล์เซฟเงียบ ๆ
            //
            // ⚠️ **ค่าตั้งต้นยังเป็น "รับทุกเวอร์ชัน"** เพราะทุก build ของเราปัจจุบันรายงานตัวเองว่า
            // 5.2.1 เหมือนกันหมด (ยังไม่มีเลข build แยก) ⇒ เปิดด่านตอนนี้จะกันคนที่ควรเข้าได้ด้วย
            // ตั้ง --min-client-version เมื่อไรค่อยเริ่มบังคับ (ดู Program.cs)
            string clientVersion = request.QueryString.Get("version");
            bool compatible = MinClientVersion == null
                              || string.IsNullOrEmpty(clientVersion)
                              || string.CompareOrdinal(clientVersion, MinClientVersion) >= 0;
            if (!compatible)
            {
                Console.WriteLine($"[เวอร์ชัน] ปฏิเสธตัวเกม {clientVersion} (ต้องอย่างน้อย {MinClientVersion})");
            }

            JObject jObject = new()
            {
                // ต้นฉบับ: CurrentBundleVersion.GetClientVersion() = "5.2.1"
                ["server_version"] = "5.2.1",
                ["compatible"] = compatible,
                // ⚠️ ต้องมีลิงก์โหลดคู่กับ compatible=false เสมอ ไม่งั้นผู้เล่นตันที่หน้า error
                // โดยไม่รู้ว่าต้องไปโหลดที่ไหน (audit ระบุไว้เป็นข้อ high แยกต่างหาก)
                ["download_url"] = DownloadUrl ?? "",
                ["assetbundle_index_url"] = $"{RootUrl(request)}/live/{platform}/Info.5.2.1.json",
                ["assetbundle_url_root"] = $"{RootUrl(request)}/live/{platform}/"
            };
            return new WebServer.JsonResponse(jObject.ToString());
        };

        _webServer.GetRoute["/notice"] = (HttpListenerRequest request, Dictionary<string, string> _) =>
            new WebServer.JsonResponse("{}");

        _webServer.PostRoute["/sessions"] = delegate(HttpListenerRequest request, Dictionary<string, string> postData)
        {
            string remoteIp = request?.RemoteEndPoint?.Address?.ToString() ?? "?";

            // [5 ก.ย. 2026] กุญแจบัญชี — ตัวเกมส่งมาในช่อง account_id อยู่แล้วทุกคำขอ
            // (client/Durango.System/Platform.cs:118 BuildSessionForm) แต่ต้นฉบับคืนค่าว่างเสมอ
            // จึงแพตช์ฝั่งเกมให้คืนกุญแจประจำเครื่อง (ดูเหตุผลเต็มที่ Support/AccountKeys)
            // ⚠️ ไม่มีกุญแจ = ปฏิเสธ ไม่ใช่ปล่อยผ่านแบบเดิม — ตัวเกมรุ่นเก่าที่ยังไม่แพตช์ต้องเข้าไม่ได้
            string ownerKey = AccountKeys.Normalize(postData.Get("account_id"));
            if (ownerKey == null)
            {
                Console.WriteLine($"[gateway] /sessions ปฏิเสธ {remoteIp} — ไม่มีกุญแจบัญชี (ตัวเกมเก่า?)");
                return new WebServer.JsonResponse(
                    new JObject { ["error"] = "no_account_key" }.ToString(), HttpStatusCode.Unauthorized);
            }

            if (BanList.IsBanned(ownerKey))
            {
                Console.WriteLine($"[แบน] ปฏิเสธ {remoteIp} — บัญชี {AccountKeys.ForLog(ownerKey)} ถูกแบน");
                return new WebServer.JsonResponse(new JObject
                {
                    ["error"] = "banned",
                    ["reason"] = BanList.ReasonOf(ownerKey) ?? ""
                }.ToString(), HttpStatusCode.Forbidden);
            }

            if (Host.Maintenance)
            {
                Console.WriteLine($"[ดูแล] ปฏิเสธ {remoteIp} — กำลังปิดปรับปรุง");
                return new WebServer.JsonResponse(
                    new JObject { ["error"] = "maintenance" }.ToString(), HttpStatusCode.ServiceUnavailable);
            }

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

            if (context == null)
            {
                // ต้นฉบับ: ไม่มี player → ใช้ _playerCtx (โฮสต์) — เซิร์ฟเราไม่มี "โฮสต์" จึงสร้าง context ชั่วคราว
                context = _host.CreateTemporaryContext(null, null, null);
            }
            else if (_host.FindContextByEntityId(context.EntityId) is { } known)
            {
                // ⚠️ ช่องโหว่เดิม: เชื่อ entity id ที่ผู้ขอพิมพ์มาเอง แล้วออก token ให้สล็อตจริงบนดิสก์ทันที
                // ⇒ POST เดียว body player={"player_info":{"player_entity_id":"ของเหยื่อ"}} = ยึดตัวละครได้
                // ตอนนี้ต้องเป็นเจ้าของก่อนถึงจะหยิบสล็อตจริงมาใช้ได้
                if (AccountKeys.Same(ownerKey, known.OwnerKey))
                {
                    context = known;   // ตัวละครของเราเอง — ใช้เซฟบนดิสก์เป็นหลัก
                }
                else
                {
                    Console.WriteLine($"[gateway] /sessions {remoteIp} ขอสวมตัวละคร {known.EntityId} " +
                                      $"ที่ไม่ใช่ของบัญชี {AccountKeys.ForLog(ownerKey)} — ให้ context ใหม่แทน");
                    context = _host.CreateTemporaryContext(null, null, null);
                }
            }

            // context ชั่วคราวเป็นของบัญชีที่ขอมาตั้งแต่ต้น — /players จะเซฟค่านี้ลงไฟล์ตอนสร้างจริง
            context.OwnerKey ??= ownerKey;

            _gameServer.Register(context);
            string token = Guid.NewGuid().ToString("N");
            _gameServer.IssueSession(context.EntityId, token, ownerKey);
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
            // [5 ก.ย. 2026] เพดานผู้เล่น (--max-players) — เดิมรับค่ามาแล้วพิมพ์ทิ้ง ไม่มีที่ไหนใช้เลย
            //
            // ทำไมมาห้ามที่ /entry: นี่คือด่านสุดท้ายก่อน client จะรู้ที่อยู่ TCP ของโลก
            // (frontend_addresses) ⇒ ห้ามที่นี่ = ตัวละครยังไม่ทันโผล่ในโลก ไม่ต้องเตะใครออก
            //
            // ⚠️ เป็นด่าน "อ่อน" ไม่ใช่ด่านแข็ง: ใครที่ถือ frontend_addresses อยู่แล้วยังต่อ TCP ได้
            // ด่านแข็งต้องอยู่ที่ Auth ใน Core/GameServer.cs:153 ซึ่งอยู่นอกขอบเขตที่งานรอบนี้แก้ได้
            // ⚠️ นับไม่ได้ (PlayersOnline คืน -1) ⇒ ปล่อยผ่าน ดีกว่ากันคนเข้าเพราะตัวนับพัง
            int cap = _host.MaxPlayers;
            if (cap > 0)
            {
                int online = _host.PlayersOnline();
                if (online >= cap)
                {
                    Console.WriteLine($"[gateway] /entry ปฏิเสธ — เซิร์ฟเต็ม ({online}/{cap})");
                    return new WebServer.JsonResponse(new JObject
                    {
                        ["error"] = "server_full",
                        ["players_online"] = online,
                        ["max_players"] = cap
                    }.ToString(), HttpStatusCode.ServiceUnavailable);
                }
            }

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
            // [7 ก.ย. 2026] เติม radiotower_addresses — ช่องแชท/แจ้งเตือนของเผ่ากับสังคม
            //
            // client/Durango.UI/TitleMenuGroup.cs:949-952 อ่านคีย์นี้แล้วส่งให้
            // SocialSystem.SetEndpoints ⇒ ไม่มีคีย์ = รายการ endpoint ว่าง = สาย Radiotower
            // ไม่เคยต่อติดเลยสักครั้ง ⇒ ToggleClanNotification(4025) ·
            // GetClanNotificationEnabled(4027) · ResubscribeClanChannel(24) ที่ลงทะเบียนไว้
            // ใน Player.Clan.cs ไม่มีทางถูกเรียกถึง
            //
            // เซิร์ฟนี้มี Connection เดียวต่อผู้เล่น (ไม่ได้แยกโปรเซส radiotower แบบ NEXON)
            // ⇒ ชี้มาพอร์ตเกมเดียวกัน handler ชุดเดิมรับได้เลย ไม่ต้องเปิดพอร์ตใหม่
            return new WebServer.JsonResponse(new JObject
            {
                ["frontend_addresses"] = new JArray($"{tcpHost}:{_gameServer.Port}"),
                ["radiotower_addresses"] = new JArray($"{tcpHost}:{_gameServer.Port}"),
                ["cluster_mode"] = Host.ClusterMode.ToString()
            }.ToString());
        };

        _webServer.PostRoute["/players"] = delegate(HttpListenerRequest request, Dictionary<string, string> postData)
        {
            // ต้นฉบับ (prologue สร้างตัวละคร): name/region/job/gender/model_info → อัปเดต context + ใส่ชุดตามอาชีพ
            // ⚠️ เดิมถอยไปที่ _playerCtx เมื่อไม่มีหัว Authorization ⇒ คนนอกยิง POST /players ว่าง ๆ
            // ก็เขียนทับชื่อ/เพศ/หน้าตาของตัวละครสล็อตแรกได้ถาวร และเปลี่ยน TerrainId ของโลกหลักด้วย
            // ⇒ ต้องมี session จริงเท่านั้น
            string ownerKey = _gameServer.OwnerOfSession(request?.Headers?["Authorization"]);
            PlayerContext context = ResolveBySession(request);
            if (context == null || ownerKey == null)
            {
                Console.WriteLine("[gateway] /players ปฏิเสธ — ไม่มี session ที่ถูกต้อง");
                return new WebServer.JsonResponse(
                    new JObject { ["error"] = "unauthorized" }.ToString(), HttpStatusCode.Unauthorized);
            }

            // ⚠️ เส้นนี้มีไว้ "สร้างตัวใหม่" เท่านั้น — ตัวที่มี Path แล้วคือตัวที่สร้างเสร็จไปแล้ว
            // ห้ามให้เขียนทับ (ของเราเองก็ตาม) ไม่งั้นยิงซ้ำ = ตัวละครเดิมโดนรีเซ็ต
            if (!string.IsNullOrEmpty(context.Path) || string.IsNullOrEmpty(context.PlayerInfo.PlayerEntityId))
            {
                context = _host.CreateTemporaryContext(null, null, null);
                _gameServer.Register(context);
            }
            context.OwnerKey = ownerKey;
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
                // ⚠️ BodyColor เป็น null ได้เมื่อคำขอไม่ได้ส่ง model_info มา — ตัวเกมจริงส่งเสมอ
                // แต่คำขอที่ประกอบเองไม่ส่งก็ได้ แล้วเดิมจะ NullReference ทั้ง route (ตอบ 500)
                // เมื่อก่อนไม่เคยเห็นเพราะ route ถอยไปใช้ _playerCtx ซึ่งมีสีค้างจากตัวละครก่อนหน้า
                if (bodyColor != null && bodyColor.Length >= 3)
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

        // [5 ก.ย. 2026] รายชื่อตัวละคร — **ของบัญชีที่ถามเท่านั้น**
        //
        // ⚠️ เดิมคืนตัวละครทุกตัวบนเซิร์ฟให้ใครก็ได้ แล้วหน้าเลือกตัวละครในเกมเอามาทำเป็นปุ่ม
        // (client/Durango.UI/TitlePlayerSelectionGroupBase.cs:94,120) ⇒ ผู้เล่นคนที่ 2 เปิดเกม
        // เห็นตัวละครของคนที่ 1 ในสล็อตตัวเอง กดเข้าเล่นได้เลยโดยไม่ต้องแฮกอะไร
        // แถม client ยังตั้งตัวที่ "เพิ่งออกจากเกมล่าสุดของทั้งเซิร์ฟ" เป็นตัวแนะนำให้อัตโนมัติ
        // (client/Durango.Logic.Clusters/Account.cs:34 MaxBy(DisconnectedAt)) ⇒ กด Confirm รวดเดียวก็ติด
        //
        // ตัวเกมส่ง account_id มากับคำขอนี้อยู่แล้ว (Clusters.RequestAccounts ใช้ BuildSessionForm)
        _webServer.PostRoute["/accounts"] = delegate(HttpListenerRequest request, Dictionary<string, string> postData)
        {
            string key = AccountKeys.Normalize(postData.Get("account_id"));
            if (key == null)
            {
                // ไม่มีกุญแจ = ไม่มีบัญชี ⇒ ไม่มีตัวละคร (ไม่ใช่ "เห็นทุกตัว" แบบเดิม)
                return new WebServer.JsonResponse(Json.Write(Host.EmptyAccount()));
            }
            return new WebServer.JsonResponse(Json.Write(_host.BuildAccount(key)));
        };

        // [5 ก.ย. 2026] /health — ตัวเลขสุขภาพเซิร์ฟสำหรับคนดูแล (ตัวเกมไม่ได้เรียกเส้นนี้)
        //
        // ทำไมต้องมี: เวลาผู้เล่นบอกว่า "เซิร์ฟหน่วง" เดิมไม่มีอะไรให้ดูเลย ต้องเดาล้วน ๆ
        // ตอนนี้ดูได้ทันทีว่ารอบเกมช้าจริงไหม (tick_ms) · เซฟล่าสุดเมื่อไร · พังไปกี่ครั้ง
        // · มีแพ็กเก็ตชนิดไหนที่เกมส่งมาแล้วเรายังไม่รองรับ (unhandled_packet_types)
        //
        // ⚠️ route นี้รันบนลูปเกม (Gateway.Process ถูกเรียกใน Host.Process) ⇒ ต้องเบา
        // งานหนักสุดคือ sort ตัวอย่างเวลา 512 ตัว ซึ่งทำเฉพาะตอนมีคนเรียกเท่านั้น
        // ══ เครื่องมือดูแลเซิร์ฟ ══════════════════════════════════════════════════
        // audit: "ไม่มีเครื่องมือเตะ/แบน/ปิดปากเลยแม้แต่ตัวเดียว — เจอคนป่วนแล้วทำได้อย่างเดียว
        // คือปิดทั้งเซิร์ฟ" · ทุกเส้นใช้ด่านเดียวกับ /health (--admin-token หรือเรียกจากเครื่องเซิร์ฟเอง)

        // ใครออนไลน์อยู่บ้าง — ต้องรู้ก่อนถึงจะเตะถูกคน
        _webServer.GetRoute["/admin/who"] = delegate(HttpListenerRequest request, Dictionary<string, string> _)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            return new WebServer.JsonResponse(Json.Write(_host.DescribeOnline()));
        };

        // เตะออกจากเกม (ยังเข้าใหม่ได้) — ใช้ตอนคนค้างหรือมีปัญหาชั่วคราว
        _webServer.PostRoute["/admin/kick"] = delegate(HttpListenerRequest request, Dictionary<string, string> postData)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            string entityId = postData.Get("entity_id");
            string reason = postData.Get("reason") ?? "ถูกเตะโดยผู้ดูแล";
            bool done = _host.KickPlayer(entityId, reason);
            return new WebServer.JsonResponse(new JObject { ["kicked"] = done }.ToString());
        };

        // แบนบัญชี (เตะออกด้วย) — แบนที่กุญแจบัญชี ไม่ใช่ตัวละคร เพราะสร้างตัวใหม่ได้ฟรี
        _webServer.PostRoute["/admin/ban"] = delegate(HttpListenerRequest request, Dictionary<string, string> postData)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            string entityId = postData.Get("entity_id");
            string reason = postData.Get("reason") ?? "ถูกแบนโดยผู้ดูแล";
            PlayerContext target = _host.FindContextByEntityId(entityId);
            if (target == null || string.IsNullOrEmpty(target.OwnerKey))
            {
                return new WebServer.JsonResponse(
                    new JObject { ["error"] = "ไม่พบตัวละคร หรือตัวละครยังไม่มีเจ้าของ" }.ToString(),
                    HttpStatusCode.NotFound);
            }
            BanList.Add(target.OwnerKey, reason);
            _host.KickPlayer(entityId, reason);
            return new WebServer.JsonResponse(new JObject { ["banned"] = true }.ToString());
        };

        _webServer.PostRoute["/admin/unban"] = delegate(HttpListenerRequest request, Dictionary<string, string> postData)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            string entityId = postData.Get("entity_id");
            PlayerContext target = _host.FindContextByEntityId(entityId);
            bool done = target != null && BanList.Remove(target.OwnerKey);
            return new WebServer.JsonResponse(new JObject { ["unbanned"] = done }.ToString());
        };

        // เงินทั้งเซิร์ฟ — ไว้เฝ้าเงินเฟ้อ (เซิร์ฟนี้ใช้สกุลเดียว ดู Core/Player.Wallet.cs)
        // เรียกซ้ำแล้วเทียบ total_tstone ตามเวลา ถ้ามันโตเร็วกว่าจำนวนตัวละคร = ก๊อกแรงเกินท่อระบาย
        // ?top=N เพื่อดูผู้ถือรายใหญ่มากกว่า 20 อันดับ
        _webServer.GetRoute["/admin/economy"] = delegate(HttpListenerRequest request, Dictionary<string, string> _)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            int top = 20;
            string raw = request.QueryString?["top"];
            if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out int parsed) && parsed > 0) top = Math.Min(parsed, 500);
            return new WebServer.JsonResponse(Json.Write(_host.DescribeEconomy(top)));
        };

        // ตั้งยอดเงินของตัวละครหนึ่งตัว — ไว้ปรับสมดุล/เทส
        // ตั้ง "ค่าที่ต้องการ" ไม่ใช่ "บวกเพิ่ม" เพราะกดซ้ำแล้วผลไม่เพี้ยน (idempotent)
        // ⚠️ นี่คือก๊อกน้ำที่ใหญ่ที่สุดในเซิร์ฟ — ผ่านด่านแอดมินเดียวกับ ban/kick
        //    และเขียน log ทุกครั้งเพื่อให้ยอดที่เห็นใน /admin/economy อธิบายที่มาได้เสมอ
        _webServer.PostRoute["/admin/economy/set"] = delegate(HttpListenerRequest request, Dictionary<string, string> postData)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            string entityId = postData.Get("entity_id");
            string rawAmount = postData.Get("amount");
            if (!long.TryParse(rawAmount, out long amount) || amount < 0)
            {
                return new WebServer.JsonResponse(
                    new JObject { ["error"] = "amount ต้องเป็นจำนวนเต็มไม่ติดลบ" }.ToString(),
                    HttpStatusCode.BadRequest);
            }
            PlayerContext target = _host.FindContextByEntityId(entityId);
            if (target == null)
            {
                return new WebServer.JsonResponse(
                    new JObject { ["error"] = "ไม่พบตัวละคร" }.ToString(), HttpStatusCode.NotFound);
            }
            long before = target.TStone;
            target.TStone = amount;
            // ⚠️ ต้องสั่งเซฟเอง — คนที่ไม่ได้ออนไลน์ไม่มี Player ให้เรียก OnContextChanged
            //    ถ้าไม่เซฟ ยอดจะอยู่แค่ในหน่วยความจำแล้วหายตอนรีสตาร์ตเซิร์ฟ
            target.Save();
            Console.WriteLine($"[เงิน] แอดมินตั้งยอดของ {entityId} จาก {before:N0} เป็น {amount:N0} T Stone");
            // ถ้าคนนั้นออนไลน์อยู่ ต้องดันยอดใหม่ไปให้เห็นทันที ไม่งั้นหน้าจอค้างยอดเก่าจนกว่าจะเข้าใหม่
            _host.PushWalletTo(entityId);
            return new WebServer.JsonResponse(
                new JObject { ["entity_id"] = entityId, ["before"] = before, ["after"] = amount }.ToString());
        };

        _webServer.GetRoute["/admin/bans"] = delegate(HttpListenerRequest request, Dictionary<string, string> _)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            return new WebServer.JsonResponse(Json.Write(BanList.Describe()));
        };

        // ประกาศถึงทุกคนที่ออนไลน์ — ใช้บอกก่อนปิดปรับปรุง
        _webServer.PostRoute["/admin/announce"] = delegate(HttpListenerRequest request, Dictionary<string, string> postData)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            string text = postData.Get("text");
            if (string.IsNullOrEmpty(text))
            {
                return new WebServer.JsonResponse(
                    new JObject { ["error"] = "ต้องมี text" }.ToString(), HttpStatusCode.BadRequest);
            }
            int sent = _host.Announce(text);
            return new WebServer.JsonResponse(new JObject { ["sent"] = sent }.ToString());
        };

        // ปิดปรับปรุง — คนที่เล่นอยู่ยังเล่นต่อได้ แต่คนใหม่เข้าไม่ได้
        // (ไม่เตะคนที่เล่นอยู่ทันที เพื่อให้ประกาศก่อนแล้วรอคนทยอยออกได้)
        _webServer.PostRoute["/admin/maintenance"] = delegate(HttpListenerRequest request, Dictionary<string, string> postData)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            Host.Maintenance = postData.Get("on") == "1";
            Console.WriteLine(Host.Maintenance
                ? "[ดูแล] เปิดโหมดปิดปรับปรุง — คนใหม่เข้าไม่ได้ (คนที่เล่นอยู่ยังเล่นต่อได้)"
                : "[ดูแล] ปิดโหมดปิดปรับปรุง — เปิดรับคนใหม่ตามปกติ");
            return new WebServer.JsonResponse(new JObject { ["maintenance"] = Host.Maintenance }.ToString());
        };

        // ══ Admin Web Tool — จัดการ config / islands / whitelist ═══════════════════════════════════

        // อ่าน config.json ทั้งหมด
        _webServer.GetRoute["/admin/config"] = delegate(HttpListenerRequest request, Dictionary<string, string> _)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            string path = Path.Combine(DataDir ?? Json.DataDir, "config.json");
            if (!File.Exists(path))
                return new WebServer.JsonResponse(new JObject { ["error"] = "ไม่พบ config.json" }.ToString(), HttpStatusCode.NotFound);
            return new WebServer.JsonResponse(File.ReadAllText(path));
        };

        // เขียน config.json (partial update — ส่ง key/value ที่ต้องการเปลี่ยน)
        _webServer.PostRoute["/admin/config"] = delegate(HttpListenerRequest request, Dictionary<string, string> postData)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            string json = postData.Get("json");
            if (string.IsNullOrEmpty(json))
                return new WebServer.JsonResponse(new JObject { ["error"] = "ต้องมี json" }.ToString(), HttpStatusCode.BadRequest);
            // ตรวจว่า JSON ถูกต้องก่อนเขียน
            try { JObject.Parse(json); }
            catch (Exception e)
            {
                return new WebServer.JsonResponse(
                    new JObject { ["error"] = "JSON ผิดรูปแบบ: " + e.Message }.ToString(), HttpStatusCode.BadRequest);
            }
            string path = Path.Combine(DataDir ?? Json.DataDir, "config.json");
            File.WriteAllText(path, json);
            Console.WriteLine("[admin] config.json ถูกอัปเดตแล้ว");
            return new WebServer.JsonResponse(new JObject { ["saved"] = true }.ToString());
        };

        // อ่าน config-meta.json (schema/descriptions สำหรับ admin UI)
        _webServer.GetRoute["/admin/config/meta"] = delegate(HttpListenerRequest request, Dictionary<string, string> _)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            string path = Path.Combine(DataDir ?? Json.DataDir, "config-meta.json");
            if (!File.Exists(path))
                return new WebServer.JsonResponse(new JObject { ["error"] = "ไม่พบ config-meta.json" }.ToString(), HttpStatusCode.NotFound);
            return new WebServer.JsonResponse(File.ReadAllText(path));
        };

        // อ่าน islands.json
        _webServer.GetRoute["/admin/islands"] = delegate(HttpListenerRequest request, Dictionary<string, string> _)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            string path = Path.Combine(DataDir ?? Json.DataDir, "islands.json");
            if (!File.Exists(path))
                return new WebServer.JsonResponse(new JObject { ["error"] = "ไม่พบ islands.json" }.ToString(), HttpStatusCode.NotFound);
            return new WebServer.JsonResponse(File.ReadAllText(path));
        };

        // เขียน islands.json
        _webServer.PostRoute["/admin/islands"] = delegate(HttpListenerRequest request, Dictionary<string, string> postData)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            string json = postData.Get("json");
            if (string.IsNullOrEmpty(json))
                return new WebServer.JsonResponse(new JObject { ["error"] = "ต้องมี json" }.ToString(), HttpStatusCode.BadRequest);
            try { JObject.Parse(json); }
            catch (Exception e)
            {
                return new WebServer.JsonResponse(
                    new JObject { ["error"] = "JSON ผิดรูปแบบ: " + e.Message }.ToString(), HttpStatusCode.BadRequest);
            }
            string path = Path.Combine(DataDir ?? Json.DataDir, "islands.json");
            File.WriteAllText(path, json);
            Console.WriteLine("[admin] islands.json ถูกอัปเดตแล้ว");
            return new WebServer.JsonResponse(new JObject { ["saved"] = true }.ToString());
        };

        // อ่าน whitelist.txt
        _webServer.GetRoute["/admin/whitelist"] = delegate(HttpListenerRequest request, Dictionary<string, string> _)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            string path = Path.Combine(DataDir ?? Json.DataDir, "whitelist.txt");
            if (!File.Exists(path))
                return new WebServer.JsonResponse(new JObject { ["error"] = "ไม่พบ whitelist.txt" }.ToString(), HttpStatusCode.NotFound);
            string[] lines = File.ReadAllLines(path);
            JArray arr = new();
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0 && !trimmed.StartsWith("#"))
                    arr.Add(trimmed);
            }
            return new WebServer.JsonResponse(new JObject { ["entries"] = arr }.ToString());
        };

        // เขียน whitelist.txt
        _webServer.PostRoute["/admin/whitelist"] = delegate(HttpListenerRequest request, Dictionary<string, string> postData)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            string entries = postData.Get("entries");
            if (string.IsNullOrEmpty(entries))
                return new WebServer.JsonResponse(new JObject { ["error"] = "ต้องมี entries" }.ToString(), HttpStatusCode.BadRequest);
            string path = Path.Combine(DataDir ?? Json.DataDir, "whitelist.txt");
            File.WriteAllText(path, "# รายชื่อที่อนุญาตให้เข้าเซิร์ฟ (entity id หรือชื่อตัวละคร บรรทัดละ 1)\n" + entries);
            Console.WriteLine("[admin] whitelist.txt ถูกอัปเดตแล้ว");
            return new WebServer.JsonResponse(new JObject { ["saved"] = true }.ToString());
        };

        // อ่าน per-island config
        _webServer.GetRoute["/admin/island/config"] = delegate(HttpListenerRequest request, Dictionary<string, string> _)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            string islandId = request.QueryString.Get("id");
            if (string.IsNullOrEmpty(islandId))
                return new WebServer.JsonResponse(new JObject { ["error"] = "ต้องมี ?id=" }.ToString(), HttpStatusCode.BadRequest);
            // กัน path traversal
            if (islandId.Contains("..") || islandId.Contains('/') || islandId.Contains('\\'))
                return new WebServer.BadRequestResponse();
            string path = Path.Combine(DataDir ?? Json.DataDir, "islands", islandId, "config.json");
            if (!File.Exists(path))
                return new WebServer.JsonResponse(new JObject { ["error"] = $"ไม่พบ config ของ {islandId}" }.ToString(), HttpStatusCode.NotFound);
            return new WebServer.JsonResponse(File.ReadAllText(path));
        };

        // เขียน per-island config
        _webServer.PostRoute["/admin/island/config"] = delegate(HttpListenerRequest request, Dictionary<string, string> postData)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            string islandId = postData.Get("id");
            string json = postData.Get("json");
            if (string.IsNullOrEmpty(islandId) || string.IsNullOrEmpty(json))
                return new WebServer.JsonResponse(new JObject { ["error"] = "ต้องมี id และ json" }.ToString(), HttpStatusCode.BadRequest);
            if (islandId.Contains("..") || islandId.Contains('/') || islandId.Contains('\\'))
                return new WebServer.BadRequestResponse();
            try { JObject.Parse(json); }
            catch (Exception e)
            {
                return new WebServer.JsonResponse(
                    new JObject { ["error"] = "JSON ผิดรูปแบบ: " + e.Message }.ToString(), HttpStatusCode.BadRequest);
            }
            string dir = Path.Combine(DataDir ?? Json.DataDir, "islands", islandId);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, json);
            Console.WriteLine($"[admin] islands/{islandId}/config.json ถูกอัปเดตแล้ว");
            return new WebServer.JsonResponse(new JObject { ["saved"] = true }.ToString());
        };

        // Reload config (hot reload)
        _webServer.PostRoute["/admin/reload"] = delegate(HttpListenerRequest request, Dictionary<string, string> _)
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            try
            {
                DataStore.Load(DataDir ?? Json.DataDir);
                Console.WriteLine("[admin] Reload config สำเร็จ");
                return new WebServer.JsonResponse(new JObject { ["reloaded"] = true }.ToString());
            }
            catch (Exception e)
            {
                return new WebServer.JsonResponse(
                    new JObject { ["error"] = "Reload ไม่สำเร็จ: " + e.Message }.ToString(), HttpStatusCode.InternalServerError);
            }
        };

        // ══ Admin Web UI — เสิร์ฟไฟล์ admin/index.html, style.css, app.js ══════════════════════════
        // เข้า /admin/ จะได้ index.html, /admin/style.css ได้ CSS, /admin/app.js ได้ JS

        _webServer.GetRoute["/health"] = delegate(HttpListenerRequest request, Dictionary<string, string> _)
        {
            if (!IsAdminAllowed(request))
            {
                return new WebServer.TextResponse("text/plain", "403 Forbidden", HttpStatusCode.Forbidden);
            }

            ServerMetrics.TickStats(out double tickP50, out double tickP99, out double tickMax);
            ServerMetrics.WorkStats(out double workP50, out double workP99, out double workMax);

            // แพ็กเก็ตที่ยังไม่มี handler — ตัวนับของจริงอยู่ที่ Connection.UnhandledCounts แล้ว
            // (GameCode/Durango.Online/Connection.cs:430) เธรดรับ TCP เป็นคนเขียน ⇒ ต้องอ่านใต้ล็อกเดียวกัน
            JObject unhandled = new();
            lock (Connection.UnhandledCounts)
            {
                foreach (KeyValuePair<uint, int> kv in Connection.UnhandledCounts)
                {
                    unhandled[kv.Key.ToString()] = kv.Value;
                }
            }

            JObject health = new()
            {
                ["uptime_sec"] = ServerMetrics.UptimeSec,
                ["tick_ms"] = new JObject
                {
                    ["p50"] = tickP50,
                    ["p99"] = tickP99,
                    ["max"] = tickMax,
                    ["samples"] = ServerMetrics.Samples
                },
                // เวลาที่ใช้ทำงานจริงต่อรอบ (ไม่รวม sleep) — แยกไว้เพราะ tick_ms รวมเวลานอนไปด้วย
                ["work_ms"] = new JObject { ["p50"] = workP50, ["p99"] = workP99, ["max"] = workMax },
                ["players_online"] = _host.PlayersOnline(),
                ["max_players"] = _host.MaxPlayers,
                ["worlds_loaded"] = _host.WorldsLoaded(),
                ["regions_in_catalog"] = RegionCatalog.All?.Count ?? 0,
                ["last_save_ago_sec"] = ServerMetrics.LastSaveAgoSec,
                ["save_failures"] = ServerMetrics.SaveFailures,
                ["loop_errors"] = ServerMetrics.LoopErrors,
                ["unhandled_packet_types"] = unhandled
            };
            return new WebServer.JsonResponse(health.ToString());
        };

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

    /// <summary>
    /// [5 ก.ย. 2026] ด่านกันคนนอกของเส้นทางสำหรับคนดูแล (/health)
    ///
    /// เซิร์ฟนี้ bind แบบ wildcard (WebServer.cs:302) ⇒ ทุกเส้นทางเปิดออกอินเทอร์เน็ตหมด
    /// ตัวเลขใน /health บอกจำนวนคนออนไลน์/สถานะเซิร์ฟ ไม่ควรให้ใครก็อ่านได้
    ///
    /// ตั้ง token แล้ว → ต้องส่ง ?token=… (หรือหัว X-Admin-Token) มาให้ตรง เรียกจากที่ไหนก็ได้
    /// ไม่ได้ตั้ง      → ยอมเฉพาะ loopback (curl บนเครื่องเซิร์ฟเอง) เพื่อให้ไล่บั๊กได้โดยไม่เผลอเปิดให้คนนอก
    ///
    /// เทียบแบบใช้เวลาคงที่ ไม่ให้เดา token ทีละตัวอักษรจากเวลาตอบกลับได้
    /// </summary>
    private bool IsAdminAllowed(HttpListenerRequest request)
    {
        string want = AdminToken;
        if (string.IsNullOrEmpty(want))
        {
            IPAddress from = request?.RemoteEndPoint?.Address;
            return from != null && IPAddress.IsLoopback(from);
        }
        string got = request?.QueryString?["token"];
        if (string.IsNullOrEmpty(got))
        {
            got = request?.Headers?["X-Admin-Token"];
        }
        if (string.IsNullOrEmpty(got) || got.Length != want.Length)
        {
            return false;
        }
        int diff = 0;
        for (int i = 0; i < want.Length; i++)
        {
            diff |= got[i] ^ want[i];
        }
        return diff == 0;
    }

    /// <summary>หา context จาก Authorization header (session token — client ใส่ทุก request แบบ auth)</summary>
    private static WebServer.Response Forbidden() =>
        new WebServer.TextResponse("text/plain", "403 Forbidden", HttpStatusCode.Forbidden);

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

        // ⚠️ ร่างเปล่า/ชุดชั้นในต้องตามเพศด้วย ไม่งั้นตัวละครหญิงที่ถอดเสื้อจะได้ร่างผู้ชาย
        // สวมทับโครงตัวหญิง (client/PlayerBehavior.cs:327-329 ใช้ DefaultBody เมื่อช่อง body ว่าง)
        // ต้นฉบับทำถูกอยู่แล้วที่ client/Durango.Online/PlayerContext.cs:91-93
        player.AppearPlayer.Display.DefaultBody = flag
            ? "Models/PC/Male/Body/m_body_nothing.FBX"
            : "Models/PC/Female/Body/f_body_nothing.FBX";
        player.AppearPlayer.Display.DefaultInner = flag
            ? "Models/PC/Male/Inner/m_inner_basic.FBX"
            : "Models/PC/Female/Inner/f_inner_basic.FBX";
        player.AppearPlayer.Display.Body = player.AppearPlayer.Display.DefaultBody;
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
        // ══ Admin Web UI — เสิร์ฟไฟล์จาก server/admin/ ═══════════════════════════════════════════
        // /admin/ → index.html, /admin/style.css → CSS, /admin/app.js → JS
        // ต้องเปิด admin token ถึงจะเข้าได้ (กันคนนอกเห็นหน้าจัดการเซิร์ฟ)
        if (url.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
        {
            string adminDir = Path.Combine(AppContext.BaseDirectory, "admin");
            // ถ้าไม่มีโฟลเดอร์ admin ข้าง executable ให้ลองหาใน DataDir
            if (!Directory.Exists(adminDir))
            {
                adminDir = Path.Combine(DataDir ?? Json.DataDir, "..", "admin");
                adminDir = Path.GetFullPath(adminDir);
            }
            if (!Directory.Exists(adminDir))
            {
                return (HttpListenerRequest _, Dictionary<string, string> __) =>
                    new WebServer.TextResponse("text/plain", "Admin UI ไม่พบ — วางไฟล์ admin/ ไว้ข้าง executable", HttpStatusCode.NotFound);
            }

            string adminFile = url.Split('?')[0];
            string targetFile;
            if (adminFile == "/admin" || adminFile == "/admin/" || adminFile == "/admin/index.html")
            {
                targetFile = Path.Combine(adminDir, "index.html");
            }
            else
            {
                // เสิร์ฟไฟล์ static ใน admin/ (style.css, app.js, login.html, etc.)
                string fileName = adminFile.Substring("/admin/".Length);
                if (fileName.Contains("..") || Path.IsPathRooted(fileName))
                    return (HttpListenerRequest _, Dictionary<string, string> __) => new WebServer.BadRequestResponse();
                targetFile = Path.Combine(adminDir, fileName.Replace('/', Path.DirectorySeparatorChar));
            }

            if (!File.Exists(targetFile))
                return (HttpListenerRequest _, Dictionary<string, string> __) =>
                    new WebServer.TextResponse("text/plain", "ไม่พบไฟล์", HttpStatusCode.NotFound);

            // ใช้ TextResponse แทน FileResponse เพื่อกำหนด Content-Type ถูกต้อง
            // (FileResponse ใช้ DirectLength ซึ่งข้าม Content-Type header → browser ดาวน์โหลดแทน render)
            string fileContentType = "application/octet-stream";
            if (targetFile.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) fileContentType = "text/html; charset=utf-8";
            else if (targetFile.EndsWith(".css", StringComparison.OrdinalIgnoreCase)) fileContentType = "text/css; charset=utf-8";
            else if (targetFile.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) fileContentType = "application/javascript; charset=utf-8";
            else if (targetFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) fileContentType = "application/json; charset=utf-8";
            else if (targetFile.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) fileContentType = "image/png";
            else if (targetFile.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) fileContentType = "image/x-icon";

            string ct = fileContentType;
            string fPath = targetFile;
            // RawBytesResponse อยู่ที่ server/Support/RawBytesResponse.cs — สืบทอด WebServer.Response
            // ไม่ได้แก้ไฟล์ต้นฉบับใน GameCode (ดูเหตุผลเต็มในไฟล์นั้น)
            //
            // ⚠️ ต้องเป็น ReadAllBytes ไม่ใช่ ReadAllText+GetBytes — รายการ content type ข้างบน
            // มี image/png กับ image/x-icon ด้วย ไฟล์ไบนารีที่ผ่าน string จะถูกแปลงอักขระจนพัง
            // (byte ที่ไม่ใช่ UTF-8 ที่ถูกต้องจะกลายเป็น U+FFFD แล้วเขียนกลับเป็น EF BF BD)
            return (HttpListenerRequest _, Dictionary<string, string> __) =>
                new RawBytesResponse(File.ReadAllBytes(fPath), ct);
        }

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
            // [แก้เอง] 5 ก.ย. 2026 — เสิร์ฟแบบสตรีม (FileResponse) แทน File.ReadAllBytes
            //
            // bundle ก้อนละหลาย MB: ReadAllBytes = byte[] ก้อนใหญ่ตกไป Large Object Heap ทุกคำขอ
            // มือถือหลายเครื่องโหลดพร้อมกัน ⇒ GC ถี่จนลูปเกมหยุดเดิน (วัดจริง 13 คน = 2 tps)
            // FileResponse อ่านทีละ 64 KB เขียนตรงลง OutputStream — หน่วยความจำคงที่ ไม่แตะ LOH
            // (คลาสนี้มีมาตั้งแต่ 4 ก.ย. แต่ไม่เคยถูกเรียกใช้จริงเลย)
            return (HttpListenerRequest request, Dictionary<string, string> postData) =>
            {
                if (File.Exists(aPath))
                {
                    return new WebServer.FileResponse(aPath);
                }
                string resolvedA = ResolveBundleIgnoringHash(aName, AssetBundleAndroidDir);
                if (resolvedA != null)
                {
                    return new WebServer.FileResponse(resolvedA);
                }
                // soundbank พากย์เสียงแยกภาษา: ชุด Android มีแค่ en_us — เสิร์ฟ en_us แทนทุกภาษา
                string fallbackA = ResolveVoiceBankFallback(aName, AssetBundleAndroidDir);
                if (fallbackA != null)
                {
                    Console.WriteLine("[assetbundle-android] {0} ไม่มี ⇒ เสิร์ฟ en_us แทน", aName);
                    return new WebServer.FileResponse(fallbackA);
                }
                Console.WriteLine("[assetbundle-android] 404 {0}", aName);
                return new WebServer.NotFountResponse();
            };
        }

        if (url.StartsWith("/terrains/", StringComparison.OrdinalIgnoreCase))
        {
            return TerrainRoute(url);
        }

        // [ลบเอง] 5 ก.ย. 2026 — ตรงนี้เคยมีบล็อก "/assetbundles/android/" ชุดที่สอง เหมือนกันทุกบรรทัด
        // แต่เข้าไม่ถึงเลย เพราะเงื่อนไขชุดแรก (ข้างบน) จับ url เดียวกันไปก่อนเสมอ ⇒ ลบทิ้ง
        // (แก้ที่ชุดแรกที่เดียวพอ ไม่ต้องแก้สองที่แล้วลืมที่ใดที่หนึ่ง)
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
