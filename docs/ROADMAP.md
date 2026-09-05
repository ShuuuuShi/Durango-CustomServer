# แผนเติมเกมเพลย์ — Durango LastHuman

ทุกตัวเลขในเอกสารนี้มาจากการสแกนซอร์สจริง ไม่ได้เดา
สร้างรายการล่าสุดด้วย `python tools/scan-protocol.py --md` → `docs/protocol-coverage.md`

---

## จุดยืนตอนนี้ (5 ก.ย. 2026)

| | สถานะ |
|---|---|
| `server/` | โค้ดเซิร์ฟ NEXON พอร์ต .NET 9 · build ผ่าน · **รับ message ได้ 147 ชนิด** (เริ่มที่ 42) |
| `client/` | ซอร์ส NEXON แท้ · build ผ่าน · DLL วางลงเกมแล้ว · เข้าเล่นได้จริง |
| โปรโตคอล | 989 message มี TypeCode + `Pack`/`Unpack` ครบ — ไม่ต้องเขียน serializer เอง |
| เกาะ | 18 เกาะ · เดินทางข้ามเกาะได้จริง · สัตว์ป่าเกิดครบทุกเกาะ |
| โมดูล | `Player.<ระบบ>.cs` 12 ไฟล์ ต่อสายที่ `Player.Systems.cs` จุดเดียว |

---

## ✅ ระบบที่ทำเสร็จ + เทสในเกมจริงแล้ว

| ระบบ | ไฟล์หลัก | ยืนยันด้วย |
|---|---|---|
| หลาย region + ล่องเรือ/เดินทางข้ามเกาะ | `Core/Host.cs` `World.cs` | ไปเกาะอื่นแล้วกลับ ของไม่หาย · เซฟแยก `regions/<id>.world` |
| จุดสำคัญประจำเกาะ (ท่าเรือ/รูวาร์ป/รอยแยก) | `World.PlaceTerrainPois` · `Support/TerrainPois.cs` | ท่าเรือโผล่ในโลก กดล่องเรือได้ |
| สัตว์ป่า — เกิด/เดินเล่น/อนิเมชั่น/ขนาดตัว | `Core/AnimalManager.cs` · `Support/AnimalMotions.cs` | `Compso_Stand` `Raptor_Walk` `Compso_Die` เล่นจริง · scale ตรงตามชนิด |
| ล่าสัตว์ — ตี/ดาเมจ/ตาย/ชำแหละ/สัตว์ตีกลับ | `Core/Player.Hunting.cs` `Player.Combat.cs` | ดาเมจ 1→225 ตามอาวุธ · หลอดเป้า 1803→1353→903 · เลขดาเมจขึ้นจอ |
| ทำให้เชื่อง (taming) | `Player.Domestication.cs` · `Support/TamingTuning.cs` | ใช้สูตรจริงจาก `constants.json → taming` |
| กรงสัตว์ + งานของสัตว์เลี้ยง | `Player.Cage.cs` · `Support/CageTypes.cs` | เมนูกรงเปิดได้ · ความจุ 45 = `min(45, 15+5*int(60/10))` |
| สัตว์เลี้ยง — เซฟ/โหลด/จำนวนครั้งที่ตาย | `Player.PetSave.cs` `PlayerContext.cs` | รีสตาร์ตแล้วสัตว์ยังอยู่ |
| คราฟต์จริง | `Player.Crafting.cs` · `Support/WorkbenchTags.cs` | 720 สูตร (ทำใหม่ได้จริง 625) · แท็กโต๊ะปลด 587 สูตร |
| เก็บเกี่ยวของธรรมชาติ | `Player.Gathering.cs` | ตัดไม้/เก็บพืช/ทุบหิน |
| สกิล/เลเวล/exp | `Player.Skills.cs` | 275 bundle · 13 หมวด · 8 อาชีพ |
| ของ/กระเป๋า/ตู้เก็บของ | `Player.Inventory.cs` | ตู้เซฟลงไฟล์เกาะ คนอื่นเปิดเห็นของชุดเดียวกัน |
| หลอดเอาชีวิตรอด + ตาย/เกิดใหม่ | `Core/SurvivalState.cs` | กินอาหารดันเพดานเลือด 30/30 → 300/300 |
| บทเรียนเริ่มเกม | `Player.Tutorial.cs` | บทสนทนาไม่ค้างบังจอแล้ว |
| กลางวัน/กลางคืน | (เดิมถูกต้องอยู่แล้ว) | รอบละ 48 นาที · `daytime 2880` |

**เครื่องมือทดสอบ:** `client/BotBridge.cs` (mod — ⚠️ ต้องลบก่อนเปิดจริง) +
`tools/bot.ps1` `tools/reload.ps1` `tools/regression.ps1`

---

## 🔧 งานปัจจุบัน — กวาดบั๊กการแสดงผล

**รูปแบบบั๊กที่เจอซ้ำที่สุด:** เซิร์ฟไม่ส่ง/ส่งฟิลด์ผิด → ฝั่งเกมวาดไม่ออก **โดยไม่มี error เลย**
ที่ร้ายที่สุดคือฟิลด์ชนิด `object` (`Item.Ext`, `ArtifactState.Cage`, …) — `Pack` เขียนแบบ
`if/else if` **ไม่มี else** ⇒ เจอ `JObject` จากไฟล์เซฟแล้วไม่เขียนอะไรลงไปเลย
⇒ ฟิลด์ที่เหลือ**เลื่อนตำแหน่งทั้งแพ็กเก็ต** ไม่ใช่แค่ช่องนั้นหาย

### เสร็จแล้ว (commit `0d5284d`)

| # | อาการ | จุดแก้ |
|---|---|---|
| 5 | **สิ่งปลูกสร้าง 43 ชนิดวางไม่ได้เลย** — `slot_id` ซ้ำ → `ArgumentException` ถูกกลืนเงียบ | `Cheats.SetDisplayParts` |
| 6·11 | `ArtifactState.EntityId` ไม่เคยตั้ง → ข้อความอัปเดตถูกทิ้งเงียบ (หลอดเลือด/กรง/ปลูกไม่ขยับ) | `Cheats` · `ArtifactManager.RaiseStateUpdated` |
| 13·17 | ป้ายชื่อโชว์ `Lv.0` ทุกหลัง · หลอดเลือดเป้าอ่าน 0/0 | `Cheats` · `BlueprintStore.MaxLevel` |
| 12 | หน้ารายละเอียดไอเทมไม่โชว์ค่าโจมตี/ป้องกัน/พลังงานเลยสักบรรทัด | `Support/ItemPerformance.cs` (ใหม่) |
| 1 | ธนูยิงแล้วไม่มีลูกศร ไม่มีเสียง | `PerformanceYaml.Weapon.Projectile` |
| 4 | ความเร็วเดินไม่เปลี่ยนตามอาวุธ | `battle_speed` + `SendBaseMoveSpeed` |
| 2 | หลอดสถานะของผู้เล่นคนอื่นค้างที่ค่าตอนเข้ามา | `UpdateSurvival` → `BroadCast` |
| 3 | ตัวละครหญิงถอดเสื้อได้ร่างผู้ชาย | `Gateway.UpdateAppearPlayer` |

### เสร็จแล้ว (commit `dccfe67` + `736f82e`)

| # | อาการ | ยืนยันยังไง |
|---|---|---|
| 7 | `Item.Ext` จากไฟล์เซฟเป็น `JObject` ⇒ แพ็กเก็ตไอเทมเลื่อนช่องทั้งชิ้น — เดิมคลุมแค่กระเป๋าผู้เล่น/สัตว์ | คลุมตู้/คลัง · ประตูติดบ้าน · หุ่นโชว์ ครบ |
| 9 | **แผนที่ไม่มีหมุดเลยสักอันตลอดเกม** — ไม่มี handler `ExplorePOI`(908) ⇒ ไม่ตอบ OK ⇒ ฝั่งเกมไม่เคยขอรายการหมุด | `[แผนที่] พบ Port ที่ [28,52]` · `พบ Crack ที่ [79,128]` · สมอโผล่บนแผนที่จริง |
| 15 | **หลุมอุกกาบาตวางเป็นแท่งเร่งวาร์ป** — `pois.yml` แยก craters/rifts แต่ตัวอ่านยัดลิสต์เดียว | แตะแล้วขึ้นป้าย "ปล่องภูเขาไฟสงบ" · ri35de ตรง pois.yml เป๊ะ 12 จุด |
| 10 | **เกาะหิมะเรนเดอร์เป็นทุ่งหญ้า** — `tile_set` ว่าง | เกมรายงาน `tile_set='tropical'` (เดิมว่าง) · เติมให้ 9 เกาะ · รายชื่อ 18 ชุด**ดึงจากตัวเกมเอง**ด้วย `tilesetdump` |
| 14 | ท้องฟ้าแจ่มใสตลอดกาล — `World.Weather` ว่างเสมอ | `[อากาศ] ri35de → sunny` · ชื่อสถานการณ์อากาศจาก `region_templates.json` จริง |
| 16 | **หน้าต่างซ่อมของไม่เปิด** — `RepairRequirement` เป็น null ทุกชิ้น | สูตร+ชื่อแท็กชุดซ่อมของ NEXON เป๊ะ |
| — | ไม่มี handler `GetDefoggedChunks`(204) ⇒ แผนที่ถูกหมอกบังหลังต่อใหม่ | เพิ่ม handler แล้ว |
| 8 | ปลูกพืชแล้วโตเต็มที่ทันที — `ArtifactState.Farming` ไม่เคยถูกตั้ง | โค้ดทำครบจาก `crops.json` แต่ **ยังไม่ได้ยืนยันในเกม** (เครื่องมือเทสยังกดเมนู "ปลูก" ของสิ่งปลูกสร้างไม่ได้) |

### ค้างจากงานกรง (ตรวจสอบเชิงโต้แย้งแล้วว่าเป็นปัญหาจริง)

- `Cheats.MakeItem` ไม่ตั้ง `Item.Ext = Reins` ⇒ หน้าต่างเลือกสัตว์ว่างเปล่า
- `PerformanceYaml.Rein` ขาด `vehicle_entity_type` / `size`
- `PetFood` ขาดฟิลด์ฝั่งการเลี้ยง
- `Player.Inventory.HandleUseItemMsg` ยังไม่รองรับการประทับบังเหียน
- `PetTuning.LifeSpanDaysBase` หน่วยไม่ตรง (วัน vs วินาที ที่ `TimedeltaFormatter`)

---

## 🚨 ช่องโหว่ใหญ่ที่เพิ่งเจอ — สร้างสิ่งปลูกสร้างผ่าน UI ไม่ได้เลย

**เซิร์ฟรับ `DestructArtifact` (รื้อ) แต่ไม่รับอะไรเลยในสายการสร้าง** ⇒ ผู้เล่นรื้อได้ แต่**สร้างไม่ได้**
ที่ผ่านมาไม่เจอเพราะเทสด้วย `cheat prop` ซึ่งข้ามสายนี้ทั้งเส้น

ลำดับที่ฝั่งเกมใช้จริง (client/BuildSystem.cs):

| ลำดับ | message | TypeCode | ฝั่งเกมรออะไร | มี handler? |
|---|---|---:|---|---|
| 1 | `EstimateBuild` → `BuildEstimation` | — | ราคา/วัสดุในหน้าเลือกแบบ (BuildSlotContainer.cs:93) | ❌ |
| 2 | `OccupyArtifactSite` → `Timer` | 2057 | จองพื้นที่ + เริ่มนับเวลาสร้าง (BuildSystem.cs:515-543) | ❌ |
| 3 | `BuildArtifact` → `Timer` | — | ใส่วัสดุ/เดินงานสร้าง (BuildSystem.cs:224-232) | ❌ |
| — | `ArtifactMaterials` | — | รายการวัสดุที่ใส่ไปแล้ว (BuildSystem.cs:374) | ❌ |
| — | `OccupyGardenGrid` | — | ลงแปลงสวน | ❌ |

⇒ **งานชิ้นถัดไปที่ควรทำก่อนเพื่อน** ถ้าจะให้คนเทสเล่นได้จริง

---

## ระบบถัดไป (ยังไม่ได้เริ่ม)

| ระบบ | ขนาด | สถานะตอนนี้ |
|---|---|---|
| ที่ดิน / Estate | 6+ msg | `GetEstateLicenses` ตอบ struct ว่าง (`Core/Player.cs`) |
| เควส | หลายสิบ | `GetQuests` ตอบ `Finished = true` หมด · ไม่มี handler `GetQuestState`(398132) |
| แคลน / ปาร์ตี้ / เพื่อน / จดหมาย | หลายสิบ | ไม่มี handler `GetParty`(20001) `GetSocial`(2402) `GetMemos`(2439) `GetFactions`(3600) |
| ตลาดจริง | หลายสิบ | `MarketManager` ราคา 0 · ของไม่หมด |
| หมู่เกาะ (Archipelago) + วาร์ป | 30+ msg | เดินทางข้ามเกาะทำแล้ว แต่ยังไม่มีระบบหมู่เกาะ/เส้นทาง |
| ระบบเปิดหลุมอุกกาบาต (ลงหินนำทาง) | ~5 msg | วางหลุมได้แล้ว ยังลงทุนเปิดไม่ได้ — ค่าอยู่ใน `Support/CrackTuning.cs` แล้ว |
| ภารกิจ / สารานุกรม | — | ไม่มี handler `GetMissions`(3620) |

### ของที่เกมยิงมาจริงบนเซิร์ฟ VPS แต่ยังไม่มี handler (5 ก.ย. 2026)

เก็บจาก `grep 'ไม่มี handler' /var/log/durango-lasthuman.log` หลังเล่นจริงหนึ่งรอบ

| กระทบการเล่นตรง ๆ | | ระบบที่ยังไม่เริ่ม | |
|---|---|---|---|
| `OccupyArtifactSite` | 2057 | `GetParty` | 20001 |
| `ReturnToHome` | 2100 | `GetSocial` | 2402 |
| `Dashed` | 2491 | `GetMemos` | 2439 |
| `WarpToPort` | 9081241 | `GetFactions` | 3600 |
| `SearchPOIs` | 904 | `GetMissions` | 3620 |
| `GetLastSearchedTime` | 906 | `GetClanCreationCosts` | 3667 |
| `GetQuestState` | 398132 | `GetSupportRequests` | 2347809 |
| `GetAttachableAccessories` | 9823457 | `GetNomadInfo` | 100000 |
| `Depart` | 2448 | `GetReturnerInfo` | 3450983 |
| | | `GetExpiredProducts` · `EngagementAgreementChanged` | 5015 · 1444250 |

**วิธีรู้ว่าขาดอะไรต่อ:** อ่าน log เซิร์ฟตอนเล่นจริง มันพิมพ์เองทุกครั้งที่เกมยิงของที่ยังไม่ได้ทำ
```
[conn] ไม่มี handler สำหรับ type=204 (bytes=3) — จะไม่เตือนซ้ำอีก
```
หาชื่อจากเลข: `grep -l "TypeCode = 204u" server/GameCode/Messages/*.cs` → `GetDefoggedChunks.cs`

---

## ของที่มีอยู่แล้ว — ใช้ซ้ำ อย่าเขียนใหม่

| ของ | ที่อยู่ | ใช้ทำอะไร |
|---|---|---|
| `Messages/` 989 ตัว | `server/GameCode/Messages/` | สเปกโปรโตคอลครบ + `Pack`/`Unpack` พร้อมใช้ |
| ตารางข้อมูลเกม | `server/data/assets/` | prototype 2,407 · natural 711 · artifact 560 · recipe 720 · pet 74 · สัตว์ 214 ชนิดพร้อมชื่อท่าทาง |
| terrain 18 แผนที่ | `server/data/terrains/*.zip` | วัตถุดิบของระบบหลายเกาะ |
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
