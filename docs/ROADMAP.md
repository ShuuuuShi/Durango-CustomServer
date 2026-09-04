# แผนเติมเกมเพลย์ — Durango LastHuman

ทุกตัวเลขในเอกสารนี้มาจากการสแกนซอร์สจริง ไม่ได้เดา
สร้างรายการล่าสุดด้วย `python tools/scan-protocol.py --md` → `docs/protocol-coverage.md`

---

## จุดยืนตอนนี้ (5 ก.ย. 2026)

| | สถานะ |
|---|---|
| `server/` | โค้ดเซิร์ฟของ NEXON พอร์ตเป็น .NET 9 · **build ผ่าน · selftest ผ่าน** · รับ message ได้ **42 ชนิด** |
| `client/` | ซอร์ส NEXON แท้ 3,755 ไฟล์ · **build ผ่าน** · DLL วางลงเกมแล้ว · เกมเปิดได้ ไม่มี exception |
| namespace | `Durango.Offline` → **`Durango.Online`** ทั้งโปรเจกต์ (server 22 + client 24 ไฟล์) |
| โปรโตคอล | 852 message มี TypeCode ครบ พร้อม `Pack`/`Unpack` — **ไม่ต้องเขียน serializer เอง** |
| ช่องว่าง | เกมยิงออกมาจริง **391 ชนิด** → เซิร์ฟรับได้ **33** · **ยังขาด 358** |

### วิธีรู้ว่าขาดอะไร (ไม่ต้องเดา)

1. **สแกนซอร์ส** — `python tools/scan-protocol.py`
   อ่าน `Send(new X{..})` ในซอร์สเกม เทียบกับ `Recv(delegate(X msg, ..))` ในเซิร์ฟ
2. **อ่าน log ตอนเล่นจริง** — เซิร์ฟพิมพ์เองทุกครั้งที่เกมยิงของที่ยังไม่ได้ทำ:
   ```
   [conn] ไม่มี handler สำหรับ type=2040 (bytes=23) — จะไม่เตือนซ้ำอีก
   ```
   หาชื่อจากเลข: `grep -l "TypeCode = 2040" server/GameCode/Messages/*.cs` → `Statistics.cs`

ทุก message ใน `server/GameCode/Messages/` บอกครบว่ามี field อะไร ชนิดอะไร เรียงยังไง

---

## แพ็กเกจ 0 — ปลดเกมออกจาก "เซิร์ฟในตัว" ⚠️ ต้องทำก่อนทุกอย่าง

**อาการจริงที่เจอ** (เปิดเกมพร้อมเซิร์ฟรันอยู่ที่ 8190):
```
EndPointListener..ctor → พอร์ต 8190 ถูกใช้อยู่
InvalidOperationException: You must call the Bind method before performing this operation.
  at Durango.Online.Listener.Accept () → Server.Process () → GameManager.Update ()
```

**สาเหตุ:** เกมของ NEXON เป็น offline build — พอเลือก save slot มันเรียก `Server.BeginServer()`
(`client/Durango.Online/Server.cs:126`) เปิด `GameServer` + `Gateway` **ของตัวเอง** ที่พอร์ต 8190/8191
ชนกับเซิร์ฟเรา ต้นเหตุอยู่ที่ `client/Durango.Logic.Clusters/Clusters.cs:55`
```csharp
public static bool Offline => true;   // hardcode → บรรทัด 127 สั่ง _clusters.Clear() ลบเซิร์ฟจริงทิ้ง
```

**ทางแก้ที่ NEXON เตรียมไว้ให้แล้ว:** `Server.ConnectTo(string ip)` (`Server.cs:160-183`)
เซ็ต `GameManager.ConnectCluster` แล้วรอบถัดไป `Clusters.LoadFromJson` เข้า branch บรรทัด 117 แทน branch offline

**สิ่งที่ต้องทำ**
1. `Clusters.Offline` — เปลี่ยนจาก `=> true` เป็นอ่านจากไฟล์/env (default = ต่อออนไลน์)
2. อ่านที่อยู่เซิร์ฟจาก `server.txt` ข้างตัวเกม (ตอนนี้ `Server.cs:33` hardcode `127.0.0.1:8190`)
   — เปลี่ยนเซิร์ฟได้โดยไม่ต้อง build ใหม่ · มือถือ/เพื่อนต่อเข้ามาได้
3. ไม่เรียก `BeginServer()` เมื่ออยู่โหมดออนไลน์
4. เติม `SingleMode`, `MultiMode` ใน `server/Support/ClusterTypes.cs` (ดูตารางล่าง)

**เรื่อง cluster_mode:** `/entry` ตอบ `"cluster_mode": "Offline"` อยู่ ค่านี้คุมว่า UI เปิด/ปิดอะไร
และ **ซอร์สเกมรู้จัก Mode มากกว่าที่เซิร์ฟมี** (`client/Durango.Online/GameServer.cs:159-160`)

| Mode | ในเกมคือ | ผล |
|---|---|---|
| `Editable` | 창작섬 เกาะสร้างสรรค์ | สร้าง/รื้อได้เต็มที่ · `Role.Sandbox` |
| `Offline` | 기록섬 เกาะบันทึก | ดูอย่างเดียว · `Role.Invalid` |
| `SingleMode` | 개인섬 เกาะส่วนตัว | `Role.Personal` |
| **`MultiMode`** | **멀티 플레이 모드** | **`Role.Rural`** — โหมดผู้เล่นหลายคน |
| `Online` | 온라인 서버 | `Role.Urban` · เปิดเมนู MMO ครบ |

`server/Support/ClusterTypes.cs` มีแค่ `Online, Offline, Editable` — ขาด 2 ตัว
⚠️ อย่ารีบตั้ง `Online` ถ้ายังไม่มี handler เมนู MMO จะโผล่แล้วกดค้าง
ลำดับปลอดภัย: `Editable` → `MultiMode` → `Online`

---

# แพ็กเกจ 1 — ระบบล่องเรือ / หมู่เกาะ ⛵ (งานหลักของรอบนี้)

ระบบนี้ใหญ่ที่สุดจริง — **70+ message** และมันบังคับให้ต้องรื้อสถาปัตยกรรมของเซิร์ฟก่อน

**ทำไมใหญ่:** ตอนนี้ `Core/Host.cs` ถือ `_worldCtx` **ตัวเดียว** และ `Core/GameServer.cs:186-190`
hardcode `Region.Id = "1"` / `Region.TerrainId = "1"` — คือมีเกาะเดียวในจักรวาล
ส่วนระบบล่องเรือทั้งระบบตั้งอยู่บนสมมติฐานว่า **มีหลายเกาะพร้อมกัน แล้วเดินทางไปมาได้**

**ของที่มีอยู่แล้ว:** `server/data/terrains/` มี **14 แผนที่** พร้อมใช้ —
`pe10gr_1..5` (ทุ่งหญ้า) · `ri35de` · `ri35te` · `ri40tr` · `ri45sa` · `ri50sn` · `ri55tu` ·
`ra60sw` · `sn20snow` · `ua60vol` (ภูเขาไฟ) → มีของพอเปิด 14 เกาะได้ทันทีที่รองรับหลาย region

## เฟส 1.1 — หลาย region ในเซิร์ฟเดียว (แกนกลาง ต้องทำก่อน)

นี่คือ 80% ของความยากทั้งแพ็กเกจ

| ไฟล์ | ต้องเปลี่ยนเป็น |
|---|---|
| `Core/Host.cs:25` `_worldCtx` | `Dictionary<string regionId, World>` — โหลด terrain ต่อ region |
| `Core/GameServer.cs:186-190` | `Region.Id` / `TerrainId` มาจาก region ที่ผู้เล่นอยู่จริง ไม่ใช่ `"1"` |
| `Core/WorldContext.cs` | path เซฟแยกต่อ region (`<cluster>/<regionId>/N.world`) |
| `Core/Player.cs` | ผูกกับ `World` ปัจจุบัน · ย้าย region = ถอดออกจาก World เก่า ใส่ World ใหม่ |
| `Program.cs:124-136` | loop 120 TPS ต้อง `Process()` ทุก region ไม่ใช่โลกเดียว |

จุดที่ต้องระวัง: `World.BroadCast<T>` (`Core/World.cs:146`) กระจายให้ทุกคน **ในโลกนั้น** อยู่แล้ว
พอแยกหลาย World แล้วมันจะถูกต้องเอง — แต่ `AppearPlayer`/`DisappearEntity` ต้องยิงตอนข้าม region ด้วย

**เกณฑ์ว่าเฟสนี้ผ่าน:** เปิดเซิร์ฟแล้วมี 2 region ทำงานพร้อมกัน ผู้เล่น A อยู่เกาะ 1 ผู้เล่น B อยู่เกาะ 2
ต่างคนต่างไม่เห็นกัน เซฟแยกไฟล์กัน

## เฟส 1.2 — ท่าเรือ + ล่องเรือพื้นฐาน

| message | TypeCode | field | ความหมาย |
|---|---:|---|---|
| `TravelByRegion` | 2029 | `EntityId` (ท่าเรือ), `Tile`, `RegionId`, `PartierId` | ล่องเรือไปเกาะที่เลือก |
| `TravelByRegionInArchipelago` | — | `EntityId`, `Tile`, `RegionId` | ไปเกาะข้างเคียงในหมู่เกาะเดียวกัน |
| `SailingBack` | 3130 | `EntityId`, `Tile` | ล่องเรือกลับ |
| `GetSailingBackCost` → `SailingBackCost` | — | `Cost` (long) | ค่าล่องเรือกลับ |
| `TravelToStableRegion` | — | | ไปเกาะถาวร |
| `TravelToRandomPersonalRegion` | — | | สุ่มไปเกาะส่วนตัว |
| `Teleported` | 2037 | `Tile`, `Type` | ย้ายตำแหน่ง **ในเกาะเดียวกัน** |

ฝั่งเกมเรียกจาก `client/ExploreSystem.cs:151-199` — อ่าน `CoTravelRegion()` เป็นสเปกได้เลย
ท่าเรือ (`Port`) คือ artifact ในโลกที่มี `Id` + `Tile` ⇒ ต้องมี artifact ประเภทท่าเรือใน terrain ก่อน

**เกณฑ์ผ่าน:** เดินไปท่าเรือ → เลือกเกาะ → กดล่อง → โผล่อีกเกาะ → กดกลับ → กลับมาที่เดิม ของในกระเป๋าไม่หาย

## เฟส 1.3 — หมู่เกาะ (Archipelago)

| message | TypeCode | field สำคัญ |
|---|---:|---|
| `Archipelago` | 2053 | `Id`, `TemplateId`, `UnstableFactor`, `Name`, `ExpiresAt`, `IncludedRegions[]` |
| `GetArchipelago` / `RecommendArchipelago` | — | `Level`, `Biome`, `UnstableFactor` |
| `ArchipelagoRoute` | — | `Level`, `Biome`, `ArchipelagoId`, `IncludedRoutes[]`, `IsEpic`, `EpicRegionId` |
| `GetRouteOfArchipelago` → `RoutesOfArchipelago` | — | เส้นทางที่เลือกได้ |
| `WarpToNextArchipelagoRegion` | — | ไปเกาะถัดไปตามเส้นทาง |
| `ArchipelagoRegionInfo` · `ArchipelagoTodos` · `CurrentArchipelagoTodos` | — | ภารกิจประจำหมู่เกาะ |

หมู่เกาะ = ชุดเกาะที่ผูกกันด้วย `Level` + `Biome` + มี `ExpiresAt` (หมดอายุแล้วสร้างใหม่)
ฝั่งเกม: `client/ExploreSystem.cs:391-413`, `client/Durango.UI/Archipelago.cs`

**เกณฑ์ผ่าน:** หน้าสำรวจในเกมโชว์หมู่เกาะพร้อมเกาะย่อยและเส้นทาง เลือกแล้วไปได้จริง

## เฟส 1.4 — Warphole (รูวาร์ป)

| message | TypeCode | ใช้ตอน |
|---|---:|---|
| `GetWarpCosts` → `WarpCosts` | — | เปิดหน้าแผนที่ (`WorldMapGroup.cs:961`) |
| `Warp` | — | วาร์ปไปเกาะ (`MapSystem.cs:435`) |
| `WarpBack` / `GetWarpBackCost` | — | วาร์ปกลับ |
| `WarpToPort` | — | วาร์ปไปท่าเรือ |
| `WarpToUrbanRegion` / `WarpToPersonalRegion` | — | ไปเมือง / เกาะส่วนตัว (`ArtifactInteractions.cs:127,135`) |
| `IsWarpholeAvailable` | — | เช็คก่อนวาร์ป (`WorldMapGroup.cs:1217`) |
| `GetWarpCostToNextRegion` | — | ภารกิจหมู่เกาะ |
| `RequestEpicWarp` | — | วาร์ปสายเนื้อเรื่อง (`QuestSystem.cs:159`) |

ต่างจากล่องเรือ: วาร์ปใช้จากแผนที่โดยตรง ไม่ต้องเดินไปท่าเรือ แต่มีค่าใช้จ่าย

## เฟส 1.5 — สำรวจ POI + ของเสริม

- `ExplorePOI` (908) / `GetExploredPOIs` (902) / `ExploredPOIs` — เปิดพื้นที่บนแผนที่ (`POIUpdater.cs:214`)
- `WarpAccelerator` ชุด (`Accelerate`, `ParticipateAcceleration`, `WarpAcceleratorInfo`,
  `ReceiveAcceleratorRewards`) — ระบบเร่งวาร์ปแบบร่วมมือกัน
- `TutorialBoat` ชุด (`AppearTutorialBoat`, `ParticipateTutorialBoat`,
  `PutMaterialsIntoTutorialBoat`, `TutorialBoatSessions`) — เรือบทเรียนตอนเริ่มเกม
- `CargoWarphole` ชุด — รูวาร์ปขนของของแคลน (ทำท้ายสุด ต้องมีแคลนก่อน)

## ลำดับที่แนะนำภายในแพ็กเกจนี้

```
1.1 หลาย region  ──►  1.2 ท่าเรือ+ล่องเรือ  ──►  1.3 หมู่เกาะ  ──►  1.4 วาร์ป  ──►  1.5 POI/ของเสริม
    (รื้อ Host)        (เล่นได้แล้ว!)          (มีเป้าหมาย)      (สะดวก)       (ครบ)
```
จบ 1.2 ก็ได้เกมที่ "ล่องเรือข้ามเกาะได้จริง" แล้ว — ที่เหลือคือทำให้ลึกขึ้น

---

## แพ็กเกจถัดไป (หลังล่องเรือ)

| # | ระบบ | ขนาด | หมายเหตุ |
|---|---|---|---|
| 2 | สถานะตัวละคร | 4 msg | `GetStatistics`(2039) `Statistics`(2040) `GetStatusEffects`(2016) `Inventory`(110) `Equipments`(111) — ตอนนี้ hardcode ที่ `Core/Player.cs:403-412` |
| 3 | เอาชีวิตรอด | tick | `Gauge` ถูก set ตอน init แล้วไม่แตะอีก (`Core/PlayerContext.cs:67-86`) หลอดไม่เดิน |
| 4 | ต่อสู้/ล่า | 4 msg | `UseBattleAction`(3440) `ExitBattle`(3496) `Revive`(2101) `ReviveImmediately`(210201) |
| 5 | คราฟต์จริง | 9 msg | ตอนนี้ตอบได้แค่รายการสูตร · ข้อมูล 720 สูตรโหลดไว้แล้ว |
| 6 | สัตว์/สัตว์เลี้ยง | 25 msg | ก้อนใหญ่ · `data/assets/pet/pets_for_client` มี 74 รายการพร้อม |
| 7 | สกิล/เลเวล | — | `PlayerLevel = 60` ตายตัว (`Core/PlayerContext.cs:51`) |
| 8 | ที่ดิน/Estate | 6+ msg | `GetEstateLicenses` ตอบ struct ว่าง (`Core/Player.cs:230-233`) |
| 9 | เควส | — | `GetQuests` ตอบ `Finished = true` หมด (`Core/Player.cs:296-311`) |
| 10 | สังคม/ตลาด | หลายสิบ | ทำท้ายสุด · `MarketManager` ตอนนี้ราคา 0 ของไม่หมด |

---

## ของที่มีอยู่แล้ว — ใช้ซ้ำ อย่าเขียนใหม่

| ของ | ที่อยู่ | ใช้ทำอะไร |
|---|---|---|
| `Messages/` 852 ตัว | `server/GameCode/Messages/` | สเปกโปรโตคอลครบ + `Pack`/`Unpack` พร้อมใช้ |
| ตารางข้อมูลเกม | `server/data/assets/` | prototype 2,407 · natural 711 · artifact 560 · recipe 720 · pet 74 |
| terrain 14 แผนที่ | `server/data/terrains/*.zip` | วัตถุดิบของระบบหลายเกาะ |
| ตัวโหลดข้อมูล | `server/Support/DataStore.cs` | `DataStore.Load(dataDir)` เรียกตอนบูตแล้ว |
| broadcast หลายคน | `Core/World.cs:146` | `BroadCast<T>` — โครง multiplayer มีแล้ว |
| entity appear | `Core/World.cs:118` `AddPlayer` | เห็นกันตอนเข้ามา |
| เซฟ/โหลด | `Core/WorldContext.cs` · `PlayerContext.cs` | ฟอร์แมต `.world`/`.player` แบบตัวแท้ |
| loop 120 TPS | `Program.cs:124-136` | ที่แขวน tick ระบบใหม่ + autosave 60 วิ |

## ข้อควรระวัง (บทเรียนจากโปรเจกต์เดิม)

1. **อย่าเดาแล้วเขียนมั่ว** — ไม่รู้ว่า field ไหนหมายถึงอะไร ให้ดู `client/` ว่าอ่านค่าไปทำอะไร
   โปรเจกต์เดิมเคยถอยงานสกิลทั้งชุด 3 รอบเพราะตั้งเกณฑ์ปลดล็อกเอง
2. **1 packet ต่อ 1 เฟรม** — `Connection.ProcessPacketQueue()` dequeue แค่ packet เดียวต่อรอบ
   คนเล่นเยอะจะหน่วง ต้องแก้ตอนขยาย
3. **buffer 12 MB ต่อผู้เล่น** — `Connection.cs` จอง static (2 MB × 6) · 100 คน = 1.2 GB ต้องทำ pool
4. **`Auth` ผูก token จาก `/sessions`** — `Core/GameServer.cs:122-131` อุดไว้แล้ว **ห้ามถอย**
   ต้นฉบับเชื่อ `EntityId` ที่ client ส่งมาตรง ๆ = สวมรอยใครก็ได้
5. **bind wildcard ต้อง urlacl** — ให้เครื่องอื่นเข้าได้ ต้องรันครั้งเดียวแบบ admin:
   `netsh http add urlacl url=http://*:8190/ user=Everyone` ไม่งั้นตกไป loopback (log บอกเอง)
6. **exp กู้ไม่ได้** — ข้อมูลเกมตั้ง `exp_amount` ของสัตว์ทุกตัวเป็น 0 ตัวเลขจริงอยู่ฝั่งเซิร์ฟ NEXON
   ต้องตั้งเอง ⇒ ตั้งไว้ที่เดียวใน config ปรับได้ ไม่กระจายใน code
