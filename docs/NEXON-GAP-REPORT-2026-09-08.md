---
title: รายงานช่องว่าง LastHuman vs Nexon
updated: 2026-09-08
source: cloud-agent bc-f5a5e23e-5380-434d-b45d-87a56301102e (read-only)
note: ไม่แก้โค้ดเกม — เอกสารอย่างเดียว
---

# Durango-LastHuman vs Nexon — รายงานช่องว่าง (read-only)

เทียบ `main` จากเอกสารระบบ + โค้ดจริงใน `server/Core` / `server/data/config.json`  
ไม่มี `HANDOFF-*` ใน repo — อนุมานจาก Features, Abort, และ stub  
**ถือว่าเสร็จแล้ว (ไม่ใส่เป็นช่องว่าง):** Quest Phase 1 Daily · PR #8 daily reset · PR #10 level-up FX · PR #11 wet refresh

เอกสาร `TODO.md` / `ROADMAP.md` / `สถานะงานปัจจุบัน.md` (7 ก.ย.) **ล้าหลังบางข้อ** ที่โค้ดปิดแล้ว: ท่าโจมตีสัตว์ + `CombatInteraction` · `AddExpForAction` มีผู้เรียก · สูตรปลดตามสกิล · Keepalive · `radiotower_addresses` · harden วาร์ป (`IsAlive` / tile / เพดาน timer) · ประทับบังเหียนใน `UseItem`

---

## 1. สรุปผู้บริหาร

LastHuman เล่นได้เป็น **เซิร์ฟเดี่ยววงปิด**: เก็บของ · คราฟต์ (ปลดตามสกิล) · สร้าง · ล่า · สกิล/เลเวล · เควสรายวัน · ล่องเรือข้ามเกาะ · สัตว์ป่า/ทามิง/กรงมีโค้ดจริง  
โปรโตคอลเกือบครบ — ช่องว่างหลักไม่ใช่ “หา message” แต่คือ **ระบบที่ handler ตอบ Abort/โครงว่าง** หรือ **สูตรเศรษฐกิจ/สังคมของ Nexon ยังไม่เดิน**

เทียบ Nexon ทั้งก้อน: **แกนเอาชีวิตรอด ~60–70% เล่นได้** · **สังคม/เศรษฐกิจ/เอนด์เกม ~10–20%** · **สถาปัตยกรรมกระจายโหนด = 0% (ตั้งใจ)**  
รวมทั้งผลิตภัณฑ์: ประมาณ **หนึ่งในสามของเกมเพลย์ Nexon** ที่ผู้เล่นรู้สึกได้ — พอเป็นเบต้าคนน้อย ไม่ใช่ Durango ออนไลน์

Features ใน `config.json` (`Taming`/`Livestock`/`Market`/`PartyAndClan`/`Friends`/`Mail`/`Wallet`/`Pvp` ฯลฯ เป็น `false`) **ไม่มีโค้ดอ่านเป็นด่านรันไทม์** — สถานะจริงอยู่ที่ handler ไม่ใช่ธง

---

## 2. ตารางตามระบบ

| ระบบ | สถานะ | ช่องว่าง vs Nexon (1 บรรทัด) |
|---|---|---|
| โปรโตคอล / handler | **Works** | Message เกือบครบ; เหลือเทสในเกม + scan ที่ล้าหลัง |
| บัญชี / ตัวตน | **Partial** | กุญแจเครื่องบน HTTP — ไม่มี login/HTTPS แบบ Nexon NPSN |
| แอดมิน / แบน / ประกาศ | **Works** | ไม่มี health-alert / คิวล็อกอิน Redis |
| Survival 3 หลอด | **Partial** | ไม่มี satiety/น้ำ; SE type 2/3/4 (fatigue ไบโอม / derived / energy) ไม่คิดบนเซิร์ฟ |
| อากาศ / ฝน→wet | **Works** | PR #11 รีเฟรช wet; สูตร `fatigue_formula` ทั้งก้อนยังไม่ครบ |
| เก็บของธรรมชาติ | **Works** | เลเวลเครื่องมือ vs เป้า = UNKNOWN; ไม่มีตาราง Nexon ใน assets |
| คราฟต์ Craft | **Partial** | ปลดตามสกิลแล้ว; สำเร็จ 100%; ไม่สืบทอดแอตช่องแรก; ไม่บังคับแท็กโต๊ะบนเซิร์ฟ |
| Modify / Reform / TechSupport | **Stub-Abort** | 73+22 สูตร + `ReformSlots` ทั้งเส้นไม่มี |
| ฝากคราฟต์ (entrust) | **Stub-Abort** | โต๊ะไม่มีคิวฝาก |
| ย้อมสี | **Partial** | `Dye` ในกระเป๋าทำงาน (ค่าของเรา); `Bleach` Abort; Features.DyeAndBleach ไม่ได้ใช้ |
| สร้างสิ่งปลูกสร้าง | **Partial** | Occupy→Complete เดินได้; รื้อทันทีไม่มี `Destructing`; แคปซูล T-stone = 0 |
| ที่ดิน / Estate | **Partial** | ประกาศ/ขยายส่วนตัวได้ ฟรี; แคลน/เมือง Abort; `SetArtifactAccess` Abort; ค่าใน `costs.json` ไม่ถูกหัก |
| สกิล / เลเวล / exp | **Works** | มีผู้เรียก + FX เลเวลขึ้น (PR #10); exp ต้านทานยังเป็น 0 |
| วิจัยหมวดสกิล | **Works** | คนละระบบกับห้องแล็บ |
| วิจัยห้องแล็บ / เผ่า | **Stub-Abort** | JSON มี แต่ส่งลิสต์ว่าง + Abort เริ่มวิจัย |
| สวมใส่ / แอตต่อสู้ | **Partial** | สวมได้; เกราะไม่เข้าสูตรโดนตี; ไม่ลดของเลเวลสูงกว่าตัว (#140); ไม่ใส่ SE ตอนสวม |
| ล่า / สัตว์ป่าโจมตี | **Works** | ส่งท่าโจมตี + `CombatInteraction` แล้ว (เอกสารแก้ฝั่งเซิร์ฟล้าหลัง) |
| ทามิงป่า → บังเหียน | **Partial** | โค้ดจับจริงแม้ Features.Taming=false; วงจรทั้งเส้นยังไม่ยืนยันในเกม |
| กรงฝึก / โรงเลี้ยง | **Partial** | handler จริงทับ Abort; อายุขัยสัตว์ไม่มีแท็กส่ง 30 วินาที; พรีวิวอาหารอาจผิดช่วง |
| สัตว์เลี้ยง / ขี่สัตว์ | **Partial** | เรียก/ขี่/งาน/กาชามี; `ReinifyPet` Abort ตั้งใจ |
| ยาน / บอลลูน / เครื่องยิง | **Stub-Abort** | ไม่มี `Catapult` / ตั๋วบอลลูน |
| เพาะปลูก | **Partial** | ปลูก+โตตามเวลาได้; รดน้ำ/ถอน/เร่ง/สารานุกรม Abort; **ไม่พบเส้นเก็บผล `grows_to`** |
| เดินทางข้ามเกาะ | **Partial** | ท่าเรือ→`Emigrated` ได้ ฟรี; ไม่มีประวัติเกาะก่อน; `LastReturnPoint` ว่าง; ค่าเรือ = 0 |
| วาร์ปในเกาะ / บ้าน | **Works** | เช็คตาย + เพดาน timer แล้ว; ไม่มีแคมป์ |
| รูวาร์ป | **Partial** | วาร์ปในเกาะฟรี; วาร์ปกลับเกาะ / เร่งวาร์ป / เกาะเมือง Abort |
| Cargo | **Stub-Abort** | ส่งของข้ามเกาะไม่มีที่ลง |
| เควส Daily | **Works** | เฟส 1 + รีเซ็ต KST; รางวัลเป็น exp ไม่มีตารางของ; ล่าไบโอม d–h / `mission_finish_*` ไม่เดิน |
| เควส Once / Epic / NPC | **Stub-Abort** | แท็บถาวรไม่เปิด; `InteractWithEpicNPC` / `RequestEpicWarp` Abort; เรื่องหลัก sunset Finished |
| แถบคะแนนเควส | **Stub-Abort** | `QuestScoreRewards=[]` ตั้งใจว่าง |
| ภารกิจฝ่าย (Faction) | **Stub-Abort** | ตอบโครงว่างเพื่อปลดไกด์; รับ/สุ่มภารกิจ Abort |
| ตลาดผู้เล่น | **Stub-Abort** | ลงขาย Abort; แคตตาล็อกกลางราคา 0 ของไม่หมด |
| ร้านเงินจริง (IAP) | **Stub-Abort** | รายการว่าง + ปฏิเสธซื้อ — ตั้งใจ |
| กระเป๋า T-stone | **Partial** | Wallet มีแล้ว; เกือบไม่มีจุดหัก (`TrySpendTStone` ไม่ถูกเรียก); `t_stone_reference` ไม่มีใน assets |
| แคลน / พันธมิตร | **Stub-Abort** | ไม่มี `/clans` HTTP + ที่เก็บสมาชิก; ทุกคำสั่งสร้าง/เข้า Abort |
| ปาร์ตี้ | **Stub-Abort** | ไม่มีที่เก็บสมาชิก/ช่องคุย; Make/Invite Abort |
| เพื่อน / บล็อก / DM | **Stub-Abort** | `GetSocial` ว่าง; คำสั่งเพื่อน Abort |
| จดหมาย | **Stub-Abort** | ไม่ push `MailPut`; รับ/ส่ง Abort |
| แชท | **Partial** | `SayInExclusiveChannel` กระจายทั้งเกาะ; ประวัติว่าง; ช่องเผ่า/ส่วนตัวไม่มี |
| เสียงใกล้ (Proximity Voice) | **Partial** | มอด + UDP relay แยกจากเกม — ไม่ใช่ช่อง Nexon |
| ดนตรีส่วนตัว | **Works** | ช่องเพลงใน `.player` มี |
| แชร์โน้ต / คอนเสิร์ต | **Stub-Abort** | ไม่มีคลังแชร์ / `Bandstand` |
| อีเวนต์ / เช็คอิน | **Stub-Abort** | ไม่ push ปฏิทิน; กดรับ Abort |
| PvP / Warp Rush / แพบทเรียน | **Stub-Abort** | ไม่มีคิว/สนาม/แพในโลก; ห้ามส่ง `DepartTutorialReady` |
| หลุมอุกกาบาต | **Partial** | วาง POI ได้; `InvestToCrack` Abort |
| ซ่อมสิ่งปลูกสร้าง | **Stub-Abort** | ความทนทานไม่ลด — ไม่มีอะไรให้ซ่อม |
| ซ่อมไอเทมในกระเป๋า | **Works** | สูตรชุดซ่อมมี |
| ชีวิตประจำวัน | **Partial** | กินน้ำ/อาบน้ำ/CPR มี; ฉายา/ธงแคลน/ตั้งชื่อหลัง Abort |
| แผนที่ / POI / หมอก | **Works** | `ExplorePOI` + `GetDefoggedChunks`; ซื้อแผนที่ Abort |
| ภาษาไทย | **Works** | `.mo` 33,440 รายการฝั่งเซิร์ฟ |
| บทเรียนเริ่มเกม | **Works** | บทสนทนาไม่ค้างจอ |
| Zoo / Garden / Colosseum / Frontend แยกโหนด | **Missing** | โปรเซสเดียว + ไฟล์ `.world` — ไม่ได้ทำ |

---

## 3. Top 10 ช่องว่างที่เล่นแล้วรู้สึก (เรียงผลกระทบ)

1. **ปาร์ตี้ + เพื่อน — เล่นด้วยกันไม่ได้เป็นกลุ่ม**  
   `MakeParty` / `InviteIntoParty` / `RequestFriend` Abort; `GetSocial` ว่าง · Features `PartyAndClan`/`Friends`=false (ไม่ได้เป็นด่านโค้ด)

2. **แคลนทั้งก้อน — ไม่มีเผ่าให้สร้าง**  
   ไม่มี `GET /clans` และที่เก็บสมาชิก · ตอบ OK จะโกหก UI · พันธมิตร/คลังเผ่า/วิจัยเผ่าตามไปตาย

3. **ไร่ไม่จบวงจร**  
   ปลูก+โตมี (`SeedPlant` / `ProcessFarming`) · รดน้ำ/ถอน/เร่ง Abort · **ไม่พบ handler เก็บผล `grows_to`** · เควส `daily_farming_a_01` นับแค่ปลูก

4. **ตลาด/เศรษฐกิจผู้เล่นว่าง**  
   ลงประกาศ Abort · แคตตาล็อก `Price=0` ของไม่หมด · T-stone มีแต่ไม่หักค่าเรือ/แคปซูล/ที่ดิน · `t_stone_reference` ไม่มีในข้อมูล

5. **เควสมีแค่ Daily**  
   Once/Epic/NPC/วาร์ปเรื่อง Abort · แถบคะแนนว่าง · `mission_finish_*` รอ Faction · ไม่มีรางวัลไอเทมใน `quests_for_client.json`

6. **Modify / Reform / โต๊ะระดับ 20·40·60**  
   ตกแต่งอาวุธ/แปรรูปปลายเกมทั้งเส้น Abort · เซิร์ฟไม่เทียบ `workbench_tags`

7. **เปิดหลุมอุกกาบาตไม่ได้**  
   POI วางแล้ว · `InvestToCrack` Abort ทั้งที่ `CrackTuning` โหลดสูตรแล้ว

8. **เกราะ/แอตสวมไม่เข้าการต่อสู้**  
   โดนตีใช้แค่ derived จากสกิล · ไม่บวก `armor.defense` · ไม่มี penalty ของเลเวลสูงกว่าตัว · แท็ก `equip` ไม่ถูกอ่าน

9. **สัตว์เลี้ยง: อายุขัยผิดหน่วย + วงจรยังไม่เทสจบ**  
   `DerivedOf` คืน 30 วันดิบเมื่อไม่มีแท็ก → ป้าย “30초” · `ReinifyPet` Abort · Features.Taming ไม่ได้ปิดโค้ด แต่ยังไม่ยืนยันจับ→ฝึก→ผูกพัน→งานบนเซิร์ฟจริง

10. **ล่องเรือไปแล้ว “กลับ” ไม่ครบสัญญา**  
    ไปข้างหน้าได้ · ไม่มีประวัติเกาะ · `SailingBack` จากเมนูท่าเรือไม่ยิงเพราะ `LastReturnPoint=null` · `WarpBack` Abort · Cargo Abort

---

## 4. นอกสcope เบต้าวงปิด (อย่าไล่ตอนนี้)

ตั้งใจไม่ทำจนกว่าเซิร์ฟเดี่ยวจะนิ่งและมีคนจริง — `ROADMAP-NEXT` เฟส N5 / `TODO` P4 / `NEXON-SERVER-ARCHITECTURE.md`:

- แยกโหนด **Frontend / Zoo / Garden / Colosseum**
- เอกสารดินต่อ chunk + ธุรกรรม BASE
- mirage LOD ทั้งเกาะ
- Couchbase (หรือเทียบเท่า) แทน `.world`
- etcd + sharding ตามเกาะ
- คิวล็อกอิน Redis / ประชากรต่อเกาะแบบ Nexon distributor
- login จริง + HTTPS (พอวงปิดด้วยกุญแจเครื่อง)
- IAP / ร้านเงินจริง (ปฏิเสธไว้แล้ว — อย่าแจกของฟรี)
- PvP เกาะ / Warp Rush / แพบทเรียน (`DepartTutorialReady` ห้ามส่ง)
- แคลนวอร์ / ยึดวาร์ปโฮล / ที่ดินเมือง
- ภารกิจหมู่เกาะ + unstable factor จริง
- Epic/Story playthrough + NPC เนื้อเรื่อง
- อีเวนต์ปี 2018–2019 / `web_daily_*` / PvP daily
- คอนเสิร์ต / วงดนตรีบนเวที
- สูตรเลือด/ดาเมจสัตว์แบบ NCalc เต็ม (มีค่าประมาณอยู่แล้ว)

---

**หมายเหตุหลักฐาน:** Features ไม่ได้เกตโค้ด · Abort ส่วนใหญ่ตั้งใจกัน UI ค้าง · Wallet มีแล้วแต่สูตรเงินค้างที่ `t_stone_reference` หายจาก dump · งานที่ “handler มีแต่ยังไม่เคยยิงในเกม” ยังเป็นความเสี่ยงคุณภาพ ไม่ใช่ช่องว่างฟีเจอร์เทียบ Nexon โดยตรง
