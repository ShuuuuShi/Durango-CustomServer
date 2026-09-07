# Quest Phase 1 — Daily / Once

อัปเดต: 7 ก.ย. 2026  
ขอบเขต PR นี้: ทำให้แท็บ **รายวัน (Daily)** เล่นได้จาก `quests/quests_for_client.json`  
**ไม่ทำ** Epic/Story playthrough และ **ไม่ทำ** Faction Missions

## สมมติฐานที่ทิ้ง

ใน repo นี้ **ไม่มี** `QuestCatalog.FirstHour`, `Player.QuestEngine.cs`, หรือ hook Collected/Crafted/Built มาก่อน  
`QuestStore` มีอยู่แล้วแต่ตอบ Finished ให้เควสเรื่องหลัก (sunset) และ NotActivated ให้ id อื่น  
`RequestQuestReward` ตอบ Abort ทั้งก้อน  
`SendQuestCategories` ส่งแค่ Epic=`sunset` ⇒ แท็บ Daily ไม่มี (VisibleCategories ว่าง)

## สิ่งที่เฟส 1 ทำ

| ชั้น | ไฟล์ | ทำอะไร |
|---|---|---|
| โหลด assets | `server/Support/QuestCatalog.cs` | อ่าน 1,386 แถวจาก `quests/quests_for_client.json` |
| ตรวจแคตตาล็อก | `server/QuestCatalogCheck.cs` | `DurangoServer --quest-check --data server/data` |
| สถานะ + รายการ | `server/Core/Player.Quest.cs` | `GetQuests(daily)` / `StateOf` ไม่ตอบ Finished ให้ Daily |
| เอนจิน | `server/Core/Player.QuestEngine.cs` | hydrate · รีเซ็ต KST · `NoteQuestEvent` · เคลม |
| เคลม | `server/Core/Player.QuestFlow.cs` | Daily ที่ ReachTheGoal จ่ายรางวัล; อื่นยัง Abort |
| เซฟ | `PlayerContext.Quests` + `quest_daily_reset_day` | persist ใน `.player` |
| เหตุการณ์ | Gathering / Crafting / Building / Hunting / PlantSeed | เดินความคืบหน้า + `NotifyQuestProceed` |

ห้ามแตะ `server/GameCode/**`

## เควสที่เล่นได้ (Live)

หมวด client = `daily` เท่านั้น (โฆษณาใน `QuestCategories.Categories`)

| id | เหตุการณ์ | ตัวกรอง | เป้า (จากคำอธิบายเกาหลี) |
|---|---|---|---:|
| `daily_gathering_a_01` | Collected | gather (ไม่ใช่ซาก) | 20 |
| `daily_butchery_a_01` | Collected | carcass | 10 |
| `daily_hunting_a_01` | Hunted | — | 10 |
| `daily_hunting_b_01` | Hunted | — | 10 |
| `daily_hunting_c_01` | Hunted | — | 10 |
| `daily_weaponcrafting_a_01` | Crafted | `weapon*` (เช่น `weapon_and_tool`) | 5 |
| `daily_weaponcrafting_b_01` | Crafted | `tool*` | 5 |
| `daily_armorcrafting_a_01` | Crafted | `clothing*` | 3 |
| `daily_cooking_a_01` | Crafted | `cook*` | 10 |
| `daily_cooking_b_02` | Crafted | `cook*` | 5 |
| `daily_process_a_01` | Crafted | `material_process*` / `process*` | 10 |
| `daily_process_b_01` | Crafted | เหมือน process_a | 4 |
| `daily_constructing_a_01` | Built | สำเร็จอาคาร (`CompleteArtifact`) | 2 |
| `daily_farming_a_01` | Farmed | ปลูกเมล็ด (`PlantSeed`) | 10 |

`a/b/c` ของล่าสัตว์เป็นเควสคนละใบแต่เงื่อนไขเดียวกันใน assets — การล่าหนึ่งครั้งเดินทั้งสามใบ

## รางวัล

assets **ไม่มี** ตารางไอเทม/เงิน/คะแนนเควส

ตอนเคลม: `AddExpForAction(SkillTuning.QuestClaimWeight=8, หมวดสกิลที่คู่กับเควส)`  
`QuestRewardResults.Reward.Exp` = จำนวน exp ดิบที่จ่ายจริง  
`QuestScore` บนการ์ดเป็นค่าโชว์ (10) — แถบคะแนนหมวดยังว่าง (`QuestScoreRewards=[]`) ตามเดิม

## รีเซ็ต Daily

ปฏิทิน **KST (UTC+9)** เก็บใน `quest_daily_reset_day`  
เที่ยงคืน KST รีเซ็ตทุกแถวหมวด `daily` กลับ WIP 0 (รวมที่ยังไม่ทันเคลม)  
`QuestToDo.EndAt` = unix ของเที่ยงคืน KST ถัดไป

## UNKNOWN — งาน fest ถัดไป

### Daily ในหมวด `daily` ที่โชว์แต่ยังไม่เดิน

| id | เหตุผล |
|---|---|
| `mission_finish_1/5/10/20/30` | `MissionUpdated` — ต้องมี Faction Missions (นอกเฟส 1) |
| `daily_hunting_d_01` … `h_01` | ล่าบนเกาะไบโอมเฉพาะ — เซิร์ฟยังไม่กรองภูมิภาคเควส |

### Daily นอกหมวด `daily` (ไม่โฆษณาแท็บ)

`web_daily_*` (christmas), `urban_event_2019_*`, `volc_event_daily_*`, `summerclub_daily_*`, `daily_warprush_s03_*`, `pvp_daily_*`, `customize_estate_quest_*`, `globe_event_2019_*`

### Once (`quest_type=2`)

โหลดครบในแคตตาล็อก แต่ **ไม่เปิดแท็บ `permanent` / อีเวนต์**  
ส่วนใหญ่ไม่ใช่ตัวนับเหตุการณ์เดียวกับ Daily: advisor วิชา, milestone เลเวล 60+, `permanent_level_*`, สตอรี่ย่อย, อีเวนต์ปี 2018–2019

ถ้าจะเปิด Once ในเฟสต่อ: เลือกเฉพาะ id ที่ map สะอาดเข้า `QuestEventType` โดยไม่ต้องมีเลเวลไอเทม/NPC/เกาะอีเวนต์

### Epic / Story

ยัง sunset Finished ตามเดิม (`epics_for_client` → `QuestStore.IsStoryQuest`)  
`RequestEpicWarp` / `InteractWithEpicNPC` ยัง Abort

### รางวัลไอเทม / คะแนนหมวด / ตัวกรองละเอียด

- ไม่มี reward table ใน `quests_for_client.json`
- `daily_cooking_b_02` ("แปรรูปวัตถุดิบ") นับเป็น `cook` เพราะสูตรไม่มีหมวดแยก
- `daily_process_b_01` ("แต่งวัสดุ") ใช้ `material_process` เดียวกับ `process_a`
- ล่า `a/b/c` ไม่แยกลูก/ชนิดสัตว์
- ไบโอมล่า / ภารกิจฝ่าย / Once ดูตาราง UNKNOWN

## วิธีตรวจ

```bash
dotnet build server -c Release
# จากโฟลเดอร์ที่ data/ ชี้ถูก (หรือ copy ไปข้าง exe)
server/bin/Release/net9.0/DurangoServer --quest-check --data server/data
```

ในเกม: เข้าโลก → เปิดเควส ควรมีแท็บรายวัน → เก็บของ/คราฟต์/ล่า/ปลูก/สร้าง แล้วแถบเดิน → ครบเป้าปุ่มรับรางวัล → exp เข้าตัวละคร → รีสตาร์ตเซิร์ฟแล้วยังอยู่

## จุดต่อให้เอนจินถัดไป

1. `QuestCatalog.Classify` — เพิ่ม Once ที่ map ได้ โดยไม่เปิดแท็บถาวรทั้ง 541 ใบ
2. กรองเกาะ/ไบโอม สำหรับ `daily_hunting_d_*`…`h_*`
3. Faction mission → `NoteQuestEvent(MissionUpdated)` ให้ `mission_finish_*`
4. ตารางรางวัลไอเทมถ้าขุดเจอจาก dump อื่น
5. แถบคะแนนหมวด (`GetQuestScoreInfos`) ยังจงใจว่าง
