# แผนที่ระบบไอเทม / Item System Map

อัปเดต: 7 ก.ย. 2026  
สโคป: **เอกสารอย่างเดียว** — สแกนซอร์สใน repo นี้ ไม่ได้เดา และ **ไม่ได้แก้** `server/GameCode/**`

Handoff ที่ใช้เป็นเข็มทิศ (ตรวจในโค้ดแล้ว ไม่ได้เปิด issue ใน GitHub ของ repo นี้):

| Handoff | เรื่อง | พบใน repo นี้ไหม |
|---|---|---|
| `#140` | สวมของเลเวลสูงกว่าตัวละครแล้วต้องลด performance | **ไม่มี issue #140** (`gh issue view 140` → not found) — พฤติกรรมในโค้ดดูหัวข้อ 5 |
| `#163` | คราฟต์สืบทอดแอตจากช่องวัตถุดิบ **ช่องแรกเท่านั้น** | **ไม่มี issue #163** — พฤติกรรมในโค้ดดูหัวข้อ 6 |
| อาหารบัฟ | กินแล้วติด status effect | มีบางส่วน — หัวข้อ 7 |
| ตกแต่งอาวุธ / โต๊ะ 20·40·60 | สูตรที่ขอ `workbench_tags` ระดับ 20/40/60 | มีข้อมูลสูตร — หัวข้อ 8 |

สัญลักษณ์สถานะ: **มีจริง** · **stub** · **ขาด** · **UNKNOWN**

---

## 0. วิธีอ่านเอกสารนี้

- ชื่อฟิลด์ / id คงเป็นอังกฤษตามไฟล์ (`Item.Level`, `effect_on`, `workbench_tags`)
- ตัวเลขนับมาจาก JSON ใน `server/data/assets/` ด้วยสคริปต์ตอนเขียนเอกสารนี้
- ถ้าข้อมูลไม่มีในชุดที่สกัดมา จะเขียน **UNKNOWN** ชัด ๆ ไม่เดาสูตร Nexon
- `Durango.Offline` ใน `client/` คือเซิร์ฟจำลองของ Nexon ที่ฝังในเกม — ใช้เทียบพฤติกรรมฝั่งเกมได้ แต่ **ไม่ใช่เซิร์ฟ LastHuman**

---

## 1. ไฟล์ที่เกี่ยวข้อง (ของจริงใน repo)

### 1.1 โค้ดเซิร์ฟ (LastHuman)

| ไฟล์ | บทบาทที่พบจริง |
|---|---|
| `server/Core/Cheats.cs` | โรงงานไอเทมตัวเดียว `MakeItem(prototypeId, level)` — คราฟต์ / เก็บของ / โกง / ร้าน / บังเหียน |
| `server/Core/Player.Inventory.cs` | กระเป๋า · กิน (`UseItem`) · ซ่อมไอเทม · ย้อม · คลัง · ล็อกของ |
| `server/Core/Player.Crafting.cs` | คราฟต์ type=Craft · ประเมินผล · ตรวจโต๊ะ/เครื่องมือ/วัตถุดิบ · **ปฏิเสธ Modify/Reform** |
| `server/Core/Player.cs` | `HandleEquipMsg` · `UpdateEquipments` · `SendStatusEffects` · ความเร็วเดินจากอาวุธ |
| `server/Core/Player.Combat.cs` | ดาเมจผู้เล่น: `WeaponAttack(prototype, item.Level)` + สกิล — **ไม่เทียบเลเวลตัวละคร** |
| `server/Core/Player.Hunting.cs` | ดาเมจสัตว์ใช้ `CurrentAttackPower()` ชุดเดียว · เกราะผู้เล่นใช้ `CurrentDerivedDefense()` (สกิล) **ไม่บวกเกราะที่สวม** |
| `server/Core/Player.StatusEffects.cs` | บัพมีเวลา · อากาศ · type=1 → หลอด survival |
| `server/Core/Player.Life.cs` | กินน้ำ/อาบน้ำ → `drink_water`/`clean` · เครื่องประดับธงแคลนยัง Abort |
| `server/Core/Player.Repair.cs` | ซ่อมสิ่งปลูกสร้าง / เสริมเทค (**Abort** — ไม่มี `ReformSlots`) |
| `server/Core/Player.ToolWear.cs` | หักความทนทานเครื่องมือ — **ค่าของเรา** (ไฟล์เกมไม่มี HP ต่อชิ้น) |
| `server/Core/Player.SkillEffects.cs` | ตัวคูณหมวดสกิล (เวลาคราฟต์/ดาเมจ) — **ค่าของเรา** |
| `server/Core/PerformanceYaml.cs` | อ่านบล็อกมือ: weapon/armor/instrument/pet_food/reins/add_on |
| `server/Support/ItemPerformance.cs` | รวมตัวเลขจาก `performance.json` ทั้งหมวด แล้วคิดสูตรที่เลเวลไอเทม |
| `server/Support/StatusEffectCatalog.cs` | อ่าน `survival/status_effects.json` — ใช้ duration/deactivates + **type=1 เท่านั้น** |
| `server/Support/StatFormula.cs` | ตัวคิดสูตร `level` / `min` `max` `int` `**` |
| `server/Support/WorkbenchTags.cs` | แท็กโต๊ะจากไฟล์ **ที่เราสร้างเอง** `derived/workbench_tags.json` |
| `server/Support/YamlPrototype.cs` | ฟิลด์ prototype ที่พอร์ตมา (ไม่ครบทุกคีย์ใน JSON) |
| `server/Support/RepairTuning.cs` | `Item.RepairRequirement` จากสูตร `constants.json` → `repair.repair_requirement_perf` |
| `server/Core/PlayerContext.cs` | เซฟ: `inventory_items` + `equipped_items` (slot → item id) |

### 1.2 ข้อมูลเกม (assets)

| ไฟล์ | ของจริงที่นับได้ |
|---|---|
| `server/data/assets/item/prototype_data.json` | **2,407** prototype · แต่ละ id เป็นอาเรย์ยาว **1** เสมอ |
| `server/data/assets/item/recipes.json` | **720** สูตร: Craft 625 · Modify 73 · Reform 22 |
| `server/data/assets/item/bonus_prototypes.json` | **ไม่ใช่แอตไอเทม** — ตารางสุ่มดรอป 3 คีย์ (`exp_jelly`, `restore_volc_blueprint`, `breaking_solid_lava`) |
| `server/data/assets/item/generator_client_data.json` | 792 ชื่อ+ไอคอน generator (เก็บของ) |
| `server/data/assets/item/collectible_names.json` | 607 collectible id |
| `server/data/assets/item/tech_support.json` | 22 สูตรเสริมเทค (คู่กับ Reform) |
| `server/data/assets/performance.json` | 32 หมวด · อาหาร 352 · อาวุธ 248 · เกราะ 540 · modifiers 642 |
| `server/data/assets/tags.json` | 1,146 แท็ก · 320 ตัวมี `modifiers` · 11 ตัวมี `status_effect` |
| `server/data/assets/survival/status_effects.json` | 372 id · 419 เทมเพลต (บาง id มีหลายช่วงเลเวล) |
| `server/data/assets/constants.json` | สูตรสำเร็จคราฟต์/ซ่อม/reform/เพดานเลเวลผู้เล่น 60 |
| `server/data/assets/derived/workbench_tags.json` | **ตารางของเรา** — 72 โต๊ะ → แท็ก+ระดับ (ไม่ใช่ข้อมูล Nexon) |
| `server/data/assets/accessories.json` | ธงประดับ 3 รายการ (แคลน) — เซิร์ฟตอบอาเรย์ว่าง |

### 1.3 ฝั่งเกม (ใช้เทียบ ไม่ได้รันบนเซิร์ฟ)

| ไฟล์ | ที่เกี่ยวกับแผนที่นี้ |
|---|---|
| `client/Durango.Logic.Item/ItemData.cs` | อ่าน `Item.*` ทั้งก้อน รวม Tags / Performance / ReformSlots |
| `client/Crafting/Recipe.cs` | `IsValidWorkbench`: แท็กโต๊ะ `.Level >=` ที่สูตรขอ |
| `client/EquipSystem.cs` | ช่องสวม: precious/head/main/body/sub/gloves/shoes/bag |
| `client/Durango.UI/ItemContextPerformance.cs` | หน้ารายละเอียด — โชว์ Nums/Strs + `effect_on` จาก Performance |
| `client/Durango.Logic/StatusEffects.cs` | แทนที่ลิสต์ทั้งก้อน · หาเทมเพลตด้วย `MinLevel..MaxLevel` |

---

## 2. ตารางฟิลด์ที่พบจริง

### 2.1 `Messages.Item` (โปรโตคอล — `server/GameCode/Messages/Item.cs`)

ห้ามแก้ไฟล์นี้ สรุปเพื่อรู้ว่าเซิร์ฟ *ส่งได้* อะไร

| ฟิลด์ | `MakeItem` ตั้งไหม | หมายเหตุ |
|---|---|---|
| `Id` | มี | Guid ใหม่ทุกชิ้น |
| `Name` `Description` `Icon` | มี | จาก prototype |
| `Prototype` | มี | id ที่ส่งเข้ามา (`.` → `_`) |
| `Level` | มี | พารามิเตอร์ของผู้เรียก |
| `OriginalLevel` | **ไม่ตั้ง (= 0)** | ฝั่งเกมมีฟิลด์และใช้เรียงของ |
| `ModifiableCount` `ModifiedCount` | 0 / 0 | ประมาณคราฟต์ก็ส่ง 0 |
| `Size` | มี | `prototype.size` |
| `Durability` | มี | Gauge เต็ม 1.0 เสมอตอนเกิด |
| `ColorR/G/B` | มี | จากตารางสี + แฮช id |
| `Unstable` | false | |
| `RepairRequirement` | มีถ้าวัสดุซ่อมได้ | `RepairTuning.Of` |
| `FounderId` `FounderCategory` | ว่าง | |
| `Tags[]` `{Id, Level}` | มี | **ทุกแท็กได้ Level = เลเวลไอเทม** ไม่ได้ก๊อบจากวัตถุดิบ |
| `TagModifications[]` | ไม่ตั้ง | |
| `Performance[]` | มี | บล็อกมือ + `ItemPerformance.MergeInto` |
| `Ext` | มีเฉพาะ reins | Reins / ไม่มี Deodorant, LootBox, Capsule, … |
| `CollectibleId` `GeneratorId` | ไม่ตั้ง | |
| `EmotionalMotions` | ไม่ตั้ง | เกราะใน performance มีฟิลด์นี้ |
| `PioneerCost` `Tradable` | ไม่ตั้ง (default) | |
| `ReformSlots[]` | ไม่ตั้ง | เสริมเทคใช้ช่องนี้ — **ขาดทั้งเส้น** |

### 2.2 `prototype_data.json` — ฟิลด์ที่มีในไฟล์

นับจาก 2,407 รายการ (ทุกตัวเป็นอาเรย์ช่องเดียว)

| ฟิลด์ JSON | อยู่ในคลาส `Yaml.Prototype` ไหม | ใช้ทำอะไรตอนนี้ |
|---|---|---|
| `min_level` `max_level` | มี | เลือกแถว prototype ตามเลเวล · เก็บของใช้ `min_level` เป็นเลเวลไอเทม |
| `name` `item_description` `icon` `size` | มี | `MakeItem` |
| `category` `sub_categories` | มี | จับคู่ชุดซ่อม (`weapon/tool` → ชุดซ่อมเครื่องมือ) |
| `tags` | มี (`Dictionary<string,string>` ค่าเป็น null เกือบหมด) | คัดลอกเป็น `Item.Tags` |
| `color_r/g/b` `dyeables` `dump_locked` | มี | สี / ย้อม |
| `help` `hiding_color` `immune_to_time` `time_limited` | มีในคลาส | ยังไม่เห็นจุดใช้บนเซิร์ฟ |
| `item_name` `material_name` | **ไม่มีในคลาส** | ชื่อวัสดุ/ชื่อชิ้น — **UNKNOWN ว่าเซิร์ฟแท้ใช้ตอนไหน** |
| `hiding_repair` | **ไม่มีในคลาส** | มีจริง 77 รายการ |

หมวด `category` ที่นับได้:

| category | จำนวน |
|---|---:|
| building/furniture | 478 |
| food/medicine | 406 |
| material | 384 |
| accessory | 329 |
| weapon/tool | 281 |
| clothing | 216 |
| taming | 74 |
| seed | 63 |
| animal_collectible | 56 |
| mineral | 54 |
| plant_collectible | 52 |
| (ไม่มี category) | 14 |

แท็กบน prototype ที่พบบ่อย: `armor`(536) `eatable`(293) `weapon`(256) `clothes`(212) `hat`(162) `equipment_avatar`(146) …

### 2.3 `performance.json` — หมวดบนสุด

| หมวด | จำนวน prototype | ฟิลด์ตัวอย่างในแถวเลเวล |
|---|---:|---|
| `weapon` | 248 | `attack` (สูตร) `accuracy` `critical` `attack_type` `slot` `model` `weapon_framework` `battle_speed` `projectile` |
| `armor` | 540 | `defense` `bag_size` `slot` `male_model` `female_model` |
| `food` | 352 | ดูตารางอาหารด้านล่าง |
| `modifiers` | 642 | `strength_plus` `attack_plus` `hiding_power` … (ของติดตัวตาม prototype) |
| `pet_food` | 196 | `vigor` + โบนัสฝึก/โต |
| `reins` | 101 | `pet_entity_type` `vehicle_entity_type` `size` `speed` … |
| `add_on` | 46 | `add_on_model_key` เท่านั้น — **ประตู/หน้าต่าง/กระดูกติดผนัง/โปสเตอร์ ไม่ใช่สกินอาวุธ** |
| `tool` | 119 | `performance` |
| `repair_kit` | 9 | `ability` |
| `inventory` | 59 | `size_limit` ของตู้ |
| `workbench` | 67 | `craft_capacity` `entrust_*` |
| `artifact_stats` | 249 | **ว่างทั้งก้อน** (เซิร์ฟใช้ HP สิ่งปลูกสร้าง = ค่าของเรา 100) |
| อื่น ๆ | — | container, cage, fertilizer, instrument, trap, … |

ชั้นในเป็นช่วง `"[min, max]"` แล้วค่อยเป็นอ็อบเจ็กต์ฟิลด์  
`ItemPerformance.Of` **หยิบแถวแรกเสมอ** ไม่เลือกช่วงที่ครอบเลเวล — ไม่เหมือน `FoodTable.LevelRange.Pick` ใน `Player.Inventory.cs`

ช่วงหลายแถวมีจริง เช่น อาวุธ urban `"[30, 49]"` / `"[50, 60]"` และอาหาร 18 ชนิด — ค่าโจมตีที่โชว์/ใช้ของพวกนี้อาจผิดช่วง **UNKNOWN ว่า Nexon เลือกแถวไหน** แต่ในไฟล์มีสองสูตรชัดเจน

### 2.4 อาหารใน `performance.json` → `food`

ทุกแถว (370 รวมช่วง) มีชุดฟิลด์หลักชุดเดียวกัน:

| ฟิลด์ | เซิร์ฟใช้ตอนกินไหม |
|---|---|
| `life` `health` `fatigue` `energy_potential` | **ใช้** — บวกเข้าหลอดทันที |
| `eat_motion` `digestivetime` | **ใช้** — ท่า + ความยาว `ItemUsed` (ถ้า 0 ใช้ **ค่าของเรา** 3 วิ) |
| `effect_on` `effect_on_level` `modifier_effect_time` | **ใช้บางส่วน** — ใส่ timed SE ถ้า `effect_on` ไม่ว่าง |
| `energy_expression` `energy_per_sec` `energy_expression_over_time` | **ไม่ใช้** — คอมเมนต์ในไฟล์ยอมรับว่าความหมายยืนยันจาก client ไม่ได้ |
| `satiety` `water` | **ไม่ใช้** — เซิร์ฟไม่มีหลอดอิ่ม/น้ำของผู้เล่น |
| `sweet` `salty` `savory` `oily` `spicy` `bitter` `sour` | **ไม่ใช้** |
| `*_plus` (เช่น `strength_plus`) | **ไม่ใช้ตอนกิน** — มีในไฟล์อาหารบางชิ้น |
| `effect_off` `effect_off_level` `effect_container` | **ไม่ใช้** |
| `eatable_level_min/max` | มี 2 แถว — **ไม่ใช้** |

`effect_on` ว่าง 243 แถว / มีค่า 127 แถว  
ค่าที่เจอ: `drink_water`(25) `energetic`(19) `thirsty`(19) `hot_food`(16) `life_up`(16) `eat_bizarre_food`(14) `fruit_water`(5) `cold_food`(3) + รายการละ 1 (`stamina_up` `drunk` `poisoning` `tea_effect_01/02` `effect_coffee_*` …)

### 2.5 `recipes.json`

ฟิลด์ที่มีใน **ทุก** สูตร: `type` `name` `category` `subcategory` `slots` `tool_tags` `workbench_tags` `duration` `duration_wait` `energy` `effort` `entrusts` `max_level` `add_on` `deduct_modifiable_count` `color_extraction` `icon` `description` `default`

| ฟิลด์ | นับ | โค้ดคราฟต์อ่านไหม (`CraftRecipeData`) |
|---|---:|---|
| `min_level` `prototype_id` `count` `prototypes` | 625 (เฉพาะ Craft) | อ่าน |
| `required_ability` `required_ability_value` | 714 | อ่านเฉพาะ `required_ability` ไปโชว์ใน `ActionInfo` — **ไม่บังคับค่า** |
| `required_recipe` | 26 | ประกาศในคลาส — **ไม่ตรวจตอนคราฟต์** |
| `add_on` | 720 (211 ไม่ว่าง) | **ไม่อ่าน** — เป็นโมเดลครอบโต๊ะตอนทำ (ครัว/ราวตาก) ไม่ใช่ของที่ได้ |
| `deduct_modifiable_count` | 720 | **ไม่อ่าน** |
| `source_info` ในช่อง | 1,662 ช่อง | **ไม่อ่าน** — client ใช้ชี้แหล่งวัตถุดิบ |
| `replace_color` `add_color` `add_color_rate` | บางสูตร | **ไม่อ่าน** |

ช่องวัตถุดิบ (`slots[]`): `slot_id` `slot_name` `count_min/max` `required_tags` `required_materials` `weight` `source_info`  
`slot_id` ที่พบบ่อย: `main`(363) `base`(291) `connector`(196) `sub`(152) `handle`(60) `accessory`(46) …

`workbench_tags` เป็น `{แท็ก: ระดับขั้นต่ำ}`  
ระดับที่มีในไฟล์: 1, 10, 15, 19, 20, 25, 30, 35, **40**, 45, 50, 55, **60**  
สูตรที่ขอระดับ **20 = 25** · **40 = 134** · **60 = 22**

### 2.6 `tags.json`

ทุกแท็กมี: `name` `description` `category` `type` `purpose` `grade` `max_level` `visible` `visible_level` `unsearchable` `icon` `required_performance` `modifiers`

| ส่วนขยาย | จำนวน | ความหมายที่เห็นในไฟล์ |
|---|---:|---|
| `modifiers` ไม่ว่าง | 320 | สูตรแอต เช่น `attack: {function: incr, formula: level}` · `bag_size` · ต้านทานสภาพอากาศ |
| `status_effect` | 11 | ติด SE ตอนคริ/หลบ/เก็บ/สวม (`action`: critical_attack / dodge / collect / **equip**) |
| `group` | 148 | เช่น `blade` `shape` `add_on`(4) `clothes` |

เซิร์ฟ **ไม่ได้อ่าน `tags.json` ตอนประกอบไอเทม** — แค่คัดลอกคีย์แท็กจาก prototype แล้วใส่เลเวลไอเทม

### 2.7 `status_effects.json`

เทมเพลตมี: `min_level` `max_level` `name` `description` `duration` `deactivates` `effects[]` `tags` `icon` …

`effects[].type` ที่นับได้ในไฟล์:

| type | จำนวนจุด | ตัวอย่าง key | เซิร์ฟตีความไหม |
|---|---:|---|---|
| 1 | 108 | `life` `health` `stamina` `fatigue` `energy` | **ใช่** — เป็น velocity หลอด (`ApplyType1MomentaFromActiveEffects`) |
| 2 | 89 | `default` `arid` `hot` `cold` … | **ไม่** — น่าจะเป็นโมเมนตัมความล้าตามไบโอม (client `FatigueMomentum`) |
| 3 | 238 | `attack_plus` `exp_multiplier` `weaponcraft_plus` | **ไม่** |
| 4 | 5 | `energy` (เช่น `energetic`) | **ไม่** (คนละ type กับ type 1) |
| 7, 9, 10, 12, 13 | น้อย | — | **UNKNOWN** ความหมายฝั่งเซิร์ฟแท้ |

`StatusEffectCatalog.Parse` ข้ามทุก type ที่ไม่ใช่ 1 และคิดสูตร type 1 ที่ **`MinLevel` ของเทมเพลต** ไม่ใช่เลเวลที่ใส่จริง

### 2.8 `constants.json` ที่เกี่ยวกับของ

| คีย์ | ของจริง | ใช้บนเซิร์ฟ? |
|---|---|---|
| `max_levels.player` | 60 | สกิล (`SkillDataStore.MaxPlayerLevel`) |
| `success_probability` | `"1 - ( max(0, d - a - correction) / 100.) ** 2"` | **ไม่** — คราฟต์สำเร็จ 100% (`CraftTuning.SuccessRate`) |
| `hurt_fail_probability` `loss_rate` | สูตร `d - a` | **ไม่** |
| `craft_great_success` | สูตร `pa/ra` | **ไม่** (`GreatSuccessRate = 0`) |
| `effort_standard.craft` | `"5 + (level - 1) * 0.5"` | **ไม่** — ใช้ `recipes.effort` เป็นสตริงแต่ไม่ได้หัก |
| `durability.deltas.craft/attack/collect` | ตัวเลข | เครื่องมือหักแบบ **ค่าของเรา** ไม่ใช้ deltas เหล่านี้ตรง ๆ |
| `repair.repair_requirement_perf` | สูตร | **ใช้** (`RepairTuning`) |
| `item.default_improvement` | อัตราสุ่มปรับปรุง | **ไม่ใช้** |
| `reform.*` | แท็กที่ reform ได้ + ตารางความหายาก | **ไม่ใช้** (สูตร type=Reform ถูก Abort) |
| สูตรลด performance เมื่อเลเวลของ > เลเวลตัว | **ไม่มีคีย์นี้ในไฟล์** | **UNKNOWN** |

ตัวแปร `d` / `a` / `correction` ในสูตรสำเร็จ **UNKNOWN** ว่า map มาจากเลเวลไอเทม / ความสามารถหมวด / อะไร — มีแต่สตริง ไม่มีเอกสารประกอบใน repo

### 2.9 `bonus_prototypes.json`

ชื่อไฟล์หลอกตา: เป็น **ตารางสุ่มของโบนัส** (rate + รายการ prototype+count) ไม่ใช่ระบบแอต/ม็อดของไอเทมที่สวม  
อ้างอิงในโค้ด: ความเห็นใน `Player.Cage.cs` เรื่องรูปแบบ `[อัตรา, จำนวน]` — ไม่ได้ต่อกับ `MakeItem` / คราฟต์

---

## 3. เส้นทางเกิดไอเทม (ทุกทางผ่าน `Cheats.MakeItem`)

```
เก็บของ     Player.Gathering     level = prototype.min_level (ธรรมชาติส่วนใหญ่ = 1)
คราฟต์     Player.Crafting      level = เฉลี่ยเลเวลวัตถุดิบ แล้ว clamp min_level..max_level ของสูตร
โกง "it"   Player.cs            level จากอาร์กิวเมนต์
บังเหียน   Hunting/Domestication level = combat level ของสัตว์
ร้าน/มาร์เก็ต MarketManager / Gateway
แคปซูลบ้าน Player.Building      level = เลเวลสิ่งปลูกสร้าง
```

จากนั้น `MakeItem`:

1. ใส่ของพื้นฐาน + แท็กทุกตัวที่เลเวลไอเทม  
2. แนบบล็อกมือ: add_on / weapon / armor / instrument / pet_food / reins(+`Ext`)  
3. `ItemPerformance.MergeInto` คิดสูตรที่เลเวลนั้นแล้วเติม Nums/Strs  
4. **ไม่** รวม `tags.json → modifiers`  
5. **ไม่** ก๊อบแท็ก/แอตจากวัตถุดิบ

---

## 4. สวมใส่ (`Equip`)

`HandleEquipMsg` (`Player.cs`):

- `action == "equip"` → จำ `EquippedItems[slot] = itemId` ถ้าของอยู่ในกระเป๋า  
- อื่น ๆ → ถอดช่อง  
- ส่ง `Equipments` + อัปเดตโมเดลตัว + `SetBaseMoveSpeed` (จาก `weapon.battle_speed`)

**ไม่มี:**

- เทียบ `Item.Level` กับ `PlayerInfo.PlayerLevel`  
- ปฏิเสธของเลเวลสูงกว่าตัว  
- ลดค่าโจมตี/ป้องกัน  
- ใส่/ถอด status `clothes` / `accessory`  
- บวก `armor.defense` / `bag_size` / `performance.modifiers` เข้า derived  
- แท็ก `status_effect.action == "equip"` (เช่น `immune_lava`)

ฝั่งเกมมีช่อง `EquipSystem.Slot` ครบ และวาดค่าจาก `Item.Performance` ที่เซิร์ฟส่งมาตอนสร้างชิ้น — ถ้าเซิร์ฟคิดเลเวลเต็ม ของเลเวล 60 ในมือเลเวล 10 จะโชว์และตีเต็มสูตร

---

## 5. กติกาเลเวล — มีจริง / stub / ขาด / UNKNOWN

| กติกา | สถานะ LastHuman | หลักฐาน | ช่องว่าง vs ที่ handoff / Nexon บอกไว้ |
|---|---|---|---|
| ไอเทมมี `Level` | **มีจริง** | `MakeItem` ตั้งทุกชิ้น | — |
| เพดานเลเวลผู้เล่น 60 | **มีจริง** | `constants.json → max_levels.player` + ระบบสกิล | — |
| ของที่ได้จากธรรมชาติตามเลเวลเกาะ/เป้า | **stub** | เก็บใช้ `prototype.min_level` (คอมเมนต์ใน Gathering เอง: วัตถุดิบธรรมชาติเป็น 1 หมด) | Nexon น่าจะผูกเลเวลเป้า — **UNKNOWN สูตร** ใน assets ที่เรามี |
| เลเวลของคราฟต์ | **มีจริงแต่เป็นค่าของเรา** | `ProductLevel` = เฉลี่ย **ทุก** ช่อง แล้ว clamp `min_level..max_level` | Handoff #163 + คอมเมนต์ในโค้ด: ของจริงเกี่ยวกับความสามารถ+คุณภาพ — สูตรใน `success_probability` ใช้ `d,a,correction` ซึ่ง **UNKNOWN** |
| `prototypes[].level` ในสูตร | มีในไฟล์เป็น `">=1"` ทั้ง 393 จุด | โค้ด**ไม่ได้อ่าน**ฟิลด์นี้ (เลือกแค่ `prototype_id` จาก criteria) | **UNKNOWN** ว่าเซิร์ฟแท้ใช้ทำอะไรนอกจากเกณฑ์มี/ไม่มีแท็ก |
| สวมของเลเวลสูงกว่าตัวแล้วลด performance (#140) | **ขาด** | Combat/Hunting ใช้ `item.Level` เต็ม · Equip ไม่เช็ค · **ไม่มีสูตรใน `constants.json`** | **UNKNOWN** สูตร Nexon (ไม่มีในชุดข้อมูลนี้) — สมมติที่พบบ่อยในเกมแนวนี้คือ `effective = min(item, player)` หรือ penalty จากผลต่าง แต่**ห้ามใส่จนกว่าจะมีหลักฐาน** |
| ประสิทธิภาพคิดที่เลเวลไอเทม | **มีจริง (ฝั่งโชว์+ดาเมจอาวุธ)** | `ItemPerformance` + `BattleDataStore.WeaponAttack` | เกราะ `defense` โชว์ในรายละเอียดแต่**ไม่เข้าสูตรโดนตี** |
| แท็กบนของมีเลเวล | **มีจริง** | `Tag.Level = item.Level` ทั้งก้อน | ไม่สืบทอดเลเวลแท็กจากวัตถุดิบ |
| ช่วงเลเวลใน `performance.json` | อาหารเลือกถูกช่วง · อาวุธ/เกราะใน `ItemPerformance` หยิบแถวแรก | อาวุธ urban มี 2 ช่วงสูตร | อาจโชว์/ตีผิดช่วง |

`success_probability` มี `d - a` ซึ่ง *อาจ* เกี่ยวกับช่องว่างเลเวล (เหมือนสูตรทามิงใช้ `d_l = playerLevel - animal.CombatLevel` ใน `Player.Hunting.cs`) แต่ในไฟล์คราฟต์**ไม่มีคำอธิบายตัวแปร** → คงเป็น **UNKNOWN** ไม่ใช่หลักฐานว่า #140 คือสูตรนี้

---

## 6. สืบทอดแอตตอนคราฟต์ (#163)

### ของจริงในสูตร

- ช่องแรกของอาวุธ Craft ส่วนใหญ่คือ `base`(86) หรือ `main`(44)  
- ช่องต่อมา (`connector` `handle` `accessory` …) ใส่แท็กวัสดุคนละชุด  
- 85 สูตรมี `prototypes[]` สลับ id ของที่ได้ตามแท็กในช่อง (เช่น `needle` → `needle_bone` ถ้าช่อง `main` มีแท็ก `bone`) — เซิร์ฟทำข้อนี้แล้ว (`ResolvePrototypeId`)  
- `tags.json → modifiers` คือแอตที่ Nexon ผูกกับ**แท็ก** ไม่ใช่กับ prototype อย่างเดียว

### LastHuman ทำอะไรตอนผลิต

| ขั้นตอน | ทำจริง | ไม่ทำ |
|---|---|---|
| เลือก prototype จาก criteria | มี | — |
| เลเวลชิ้นใหม่ | เฉลี่ย**ทุก**วัตถุดิบ | ไม่ได้ใช้ช่องแรกอย่างเดียว |
| แท็กบนของใหม่ | ชุดของ prototype ที่เลเวลผลลัพธ์ | ไม่ก๊อบแท็กจากช่อง `main`/`base` หรือช่องอื่น |
| ตัวเลขโจมตี/เกราะ/อาหาร | สูตร prototype+เลเวล | ไม่บวก `tags.modifiers` จากวัตถุดิบ |
| `TagModifications` / `ModifiableCount` | 0 | Modify กิน `deduct_modifiable_count` ไม่ได้เพราะสูตร type=1 ถูก Abort |
| ประมาณคราฟต์ (`EstimateCraft`) | ส่งแท็ก prototype ที่เลเวลเฉลี่ย | ฝั่งเกมจะเห็นของก่อนทำ**ไม่มีแอตวัสดุ** |

Handoff “สืบทอดจากช่องแรกเท่านั้น” **เข้ากับโครงสร้างสูตร** (ช่อง `main`/`base` เป็นของหลัก, ช่องอื่นเป็นส่วนประกอบ) แต่ **ไม่มีโค้ด client ที่ก๊อบแท็กให้** — ฝั่งเกมไม่ได้อ่านผลคราฟต์ล่วงหน้าจากวัตถุดิบ (คอมเมนต์ใน `MatchesCriterion`: client ไม่มีฟิลด์ `prototypes`)  
⇒ กติกา “ช่องแรกเท่านั้น” เป็นข้อกำหนดจาก handoff + รูปสูตร ไม่ใช่ซอร์สเซิร์ฟ Nexon ใน repo นี้ → ระบุในงานถัดไปว่าต้องยืนยันกับของจริง/วิดีโอถ้ามี

สรุปสถานะ: **ขาด** (เลเวลใช้ทุกช่อง + แอตไม่สืบทอดเลย)

---

## 7. บัฟอาหาร / บัฟชุดสวม

### 7.1 กิน (`UseItem` → `FoodTable`)

**มีจริง**

- หาแถว `performance.food[prototype][ช่วงเลเวล]`  
- บวก life/health/fatigue/energy ทันที  
- ถ้า `effect_on` ไม่ว่าง → `ApplyTimedStatusEffect` + ถามทับด้วย `AskEatFoodOverrideStatusEffect` เมื่อมีบัฟอาหารคนละ id  
- ลบของ · ส่ง `ItemUsed`  
- บังเหียนที่เชื่องแล้วแยกสาขา imprint ไม่ใช่อาหาร

**stub / ขาด**

| เรื่อง | สถานะ |
|---|---|
| ย่อยทีละวินาที (`energy_per_sec` / `digestivetime` ในฐานะระบบย่อย) | **stub** — ให้พลังงานก้อนเดียว · ใช้ digestivetime เป็นความยาวท่า |
| หลอดอิ่ม/น้ำ (`satiety` `water`) | **ขาด** — `SurvivalState` ไม่มีหลอดนี้ (เอกสารใน `config-meta.json` ยืนยัน) |
| ผล type 2/3/4 ของ SE อาหาร | **stub** — ไอคอน+เวลาถูกส่งไปเกม (เกมหาเทมเพลตเอง) แต่เซิร์ฟไม่ใส่ derived/fatigue biome/`energetic` type 4 |
| ผล type 1 (`life_up` `stamina_up` `poisoning` กาแฟหักล้า) | **มีจริงบนเซิร์ฟ** ถ้า effect นั้นถูกใส่ |
| รายการ “อาหาร” ใน `HasConflictingFoodStatus` | **stub** — ฮาร์ดโค้ดชุดหนึ่ง **ตก** `tea_effect_*` `effect_coffee_*` `effect_cactus_juice` `fruit_sandwich_effects` … |
| ของใช้ที่ไม่ใช่อาหาร (ยา/กล่อง/หนังสือสูตร) | **ขาด** — Abort “ยังใช้ไอเทมชนิดนี้ไม่ได้” |
| `effect_off` เมื่อหมดเวลา | **ไม่ใช้** |

`IsFoodishStatus` ที่ฮาร์ดโค้ด:  
`drink_water` `fruit_water` `thirsty` `hot_food` `cold_food` `energetic` `stamina_up` `life_up` `eat_bizarre_food` `drunk` `taste_good/very_good/bad/very_bad` `poisoning` `cactus_water`

### 7.2 ชุดสวม / เครื่องประดับ

| id ใน `status_effects.json` | ข้อมูลในไฟล์ | เซิร์ฟใส่ตอน Equip ไหม |
|---|---|---|
| `clothes` | ไม่มี `effects` ไม่มี `duration` — คำอธิบายเกาหลีเรื่องความล้า | **ไม่** |
| `accessory` | เหมือนกัน — “ภูมิอากาศเหมาะสม” | **ไม่** |

แท็กที่ `status_effect.action = "equip"` (เช่น `immune_lava`) **ไม่ถูกอ่านตอนสวม**

`LookAroundMood` ตอบ Timer แต่ไม่ให้ SE จาก `interior_mood` — คอมเมนต์ใน `Player.Life.cs` บอกเองว่ายังไม่มีสถิติสิ่งปลูกสร้าง

ธงประดับหลังตัว (`AttachAccessory`) ตอบ Abort — ข้อมูลปลดล็อกเป็นสถิติแคลนที่ยังไม่มี

---

## 8. “ตกแต่งอาวุธ” กับโต๊ะระดับ 20 / 40 / 60

มี **สามชั้น** ที่คนละเรื่อง อย่าปน:

### 8.1 `performance.add_on` (46 prototype)

ประตู หน้าต่าง กระดูกติดผนัง โปสเตอร์ แผนที่ — โมเดลประกอบบ้าน  
สูตร `bonedecoration_01_*` เป็น Craft ที่โต๊ะ `table: 1` → **คราฟต์ได้** ด้วยระบบปัจจุบัน

### 8.2 `recipes.add_on` (โมเดลครอบโต๊ะตอนทำ)

เช่น ฝาหม้อครัว / ราวตาก — เซิร์ฟไม่ส่ง ไม่ได้เกี่ยวกับของในกระเป๋า

### 8.3 สูตรที่ *ขอ* `workbench_tags` ระดับ 20 / 40 / 60

นี่คือชั้นที่ handoff ชี้:

| ระดับที่สูตรขอ | จำนวนสูตร | ตัวอย่าง |
|---|---:|---|
| 20 | 25 | `trim` `extend_sheet` **reform ทั้งหมด (table:20)** รวม `reform_nail` / `reform_temper` (อาวุธ+เครื่องมือ) |
| 40 | 134 | แปรรูป `process_*` · ชุด settler/explorer · ของแห้ง `dryer:40` · อาหาร `cook:40` |
| 60 | 22 | ของระดับสูง (`can_food_02` `cake_02` โมดูลหิน ฯลฯ) |

สูตรอาวุธประกอบ (`table_weapon: 50`) มี **28** สูตร — โต๊ะในตาราง derived ที่มีแท็กนี้มีตัวเดียว: `weapon_table_01` → `table_weapon: 60`

**ฝั่งเกม** (`Recipe.IsValidWorkbench`): ต้องมีแท็กตรงและ `tag.Level >=` ที่สูตรขอ  
แท็กมาจาก `AppearArtifact.Tags` ที่เซิร์ฟเติมจาก `derived/workbench_tags.json`  
ไฟล์นั้นตั้งระดับ ≈ `max_level` ของบลูปริ้นท์ (มักเป็น 60) เพราะเซิร์ฟ**ยังไม่เก็บเลเวลรายหลัง** — คอมเมนต์ใน `tools/derive-workbench-tags.py` และ `Cheats.MakeAppearArtifact`

**ฝั่งเซิร์ฟ** (`CheckWorkbench`): ตรวจแค่ “มี artifact + component `Workbench`” — **ไม่เช็คชื่อแท็ก ไม่เช็คระดับ**  
⇒ ยืนหน้าโต๊ะผิดชนิด เกมอาจไม่ให้กด แต่ถ้า client ส่งมา เซิร์ฟจะทำให้

สูตร **Modify (73)** และ **Reform (22)** ถูก Abort ทั้งก้อน → งาน “ตกแต่ง/ตีอาวุธใหม่ / ย้อมกระบวนการ / ถนอมอาหารบนโต๊ะ 20·40·60” **ยังไม่เข้าเส้นคราฟต์**

สถานะรวมชั้นนี้: ข้อมูลสูตร **มีจริง** · การบังคับโต๊ะระดับบนเซิร์ฟ **stub** · ระบบ Modify/Reform/TechSupport **ขาด** · mapping โต๊ะ→แท็กเป็น **ค่าของเรา** (UNKNOWN ของ Nexon)

---

## 9. สรุปมีจริง / stub / ขาด (มุมระบบ)

| ระบบ | มีจริง | stub | ขาด |
|---|---|---|---|
| สร้างไอเทมครบโมเดล/สี/แท็กพื้นฐาน | ✓ | | |
| ตัวเลขโจมตีในรายละเอียด + ดาเมจอาวุธตามเลเวลชิ้น | ✓ | เลือกช่วง performance แถวแรก | ลดตามเลเวลตัว (#140) |
| ป้องกันจากเกราะที่สวม | โชว์ใน UI | | ไม่เข้าสูตรโดนตี |
| คราฟต์ Craft 625 สูตร + สลับ prototype ตามแท็ก | ✓ | เลเวล=เฉลี่ยทุกช่อง · สำเร็จ 100% · ไม่หัก effort | สืบทอดแอตช่องแรก (#163) |
| Modify / Reform / TechSupport | | handler Abort ถูกชนิด | ทั้งระบบ |
| กินอาหาร → หลอด + SE บางตัว | ✓ | พลังงานก้อนเดียว · type 2/3/4 ไม่คิดบนเซิร์ฟ | satiety/น้ำ · ของใช้ชนิดอื่น |
| บัฟชุด `clothes`/`accessory` | เทมเพลตใน JSON | | ไม่เคยใส่ตอนสวม |
| แท็กม็อด (`tags.modifiers`) | ข้อมูลครบ | | ไม่ถูกประกอบลง Item |
| โต๊ะคราฟต์ | มีสิ่งปลูกสร้าง Workbench + แท็ก derived | ไม่เทียบระดับบนเซิร์ฟ | ตารางแท็ก Nexon |
| `ReformSlots` `TagModifications` `OriginalLevel` | โปรโตคอลมี | | MakeItem ไม่เติม |
| `bonus_prototypes.json` | ไฟล์มี | | ไม่ใช่ระบบแอต |

---

## 10. UNKNOWN ชัดเจน (อย่าเดาใน PR โค้ด)

1. **สูตรลด performance เมื่อ `item.Level > player.Level`** — ไม่มีใน `constants.json` / `performance.json` / `tags.json` ที่สกัดมา · issue #140 ไม่มีใน GitHub repo นี้  
2. **นิยามตัวแปร `d`, `a`, `correction`** ใน `success_probability` ของคราฟต์  
3. **กติกาสืบทอดแท็ก/ม็อดช่องแรก** แบบเซิร์ฟ Nexon แท้ (client ไม่มีโค้ดนี้ · เซิร์ฟ LastHuman ไม่มี) — มีแต่รูปสูตร+handoff  
4. **ตารางแท็กโต๊ะของ Nexon** — `entity_types/artifact.json` และ `blueprints.json` ไม่มีฟิลด์แท็กโต๊ะ  
5. **`repair_requirement` ราย prototype** — มีใน client `PrototypePresetRepair` แต่**ไม่ได้อยู่ในชุด assets** → `RepairTuning` ใส่ `repair_requirement = 0` แล้วคิดจากเลเวลอย่างเดียว  
6. **`artifact_stats` ว่าง 249 รายการ** — HP สิ่งปลูกสร้าง  
7. **ความหมาย `effects.type` 7/9/10/12/13** และ type 4 ต่างจาก type 1 อย่างไรบนเซิร์ฟแท้  
8. **`energy_expression` / ย่อยอาหารตามเวลา** — client ไม่ได้อ่าน  
9. **`prototypes[].level: ">=1"`** — เซิร์ฟไม่ใช้  
10. **`item_name` / `material_name` / `hiding_repair`** ใน prototype — คลาสเซิร์ฟไม่พอร์ต  
11. **GitHub #140 / #163** — ไม่มีใน `ShuuuuShi/Durango-LastHuman` (อาจเป็นเลขจากบอร์ด/แชทอื่น)

---

## 11. งานโค้ดที่แนะนำเป็นแผ่นถัดไป (อย่าทำใน PR นี้)

เรียงตาม dependency ไม่ใช่ตาม calendar — แต่ละแผ่นควรมีเทสในเกมจริงตามกฎโปรเจกต์

### แผ่น A — ลด performance เมื่อของสูงกว่าตัว (#140)  
**ไฟล์:** `Player.Combat.cs` `Player.Hunting.cs` `ItemPerformance.cs` `HandleEquipMsg` (ถ้าต้องโชว์ค่าที่ลดแล้ว)  
**อย่าเริ่มจนกว่า:** หาสูตรจากเซิร์ฟแท้ / แพตช์โน้ต / ทดลองในเกมต้นฉบับ แล้วเขียนในคอมเมนต์ว่าที่มาคืออะไร  
**ถ้ายัง UNKNOWN:** อย่าเดา `min(item, player)` ลงโปรดักชัน — ทำแฟล็กทดลองและเอกสาร `**ค่าของเรา**` ถ้าเจ้าของรับความเสี่ยง  
**เทส:** ตัวเลเวลต่ำ สวมดาบเลเวลสูง → ดาเมจและหน้ารายละเอียดต้องต่ำลงชุดเดียวกัน · ถอดแล้วกลับ

### แผ่น B — สืบทอดแอตจากช่องแรก (#163)  
**ไฟล์:** `Player.Crafting.cs` → `MakeProducts` / `HandleEstimateCraftMsg` เท่านั้น (อย่าแตะ GameCode)  
**ทำ:**  
1. กำหนดช่องแรก = ช่องแรกใน `recipe.slots` (ปกติ `main`/`base`) ไม่ใช่ลำดับใน dictionary ที่ client ส่ง  
2. ก๊อบแท็กจากวัตถุดิบช่องนั้นที่ prototype ผลลัพธ์ยังไม่มี — คง `Tag.Level` ของวัตถุดิบหรือ clamp ตามสูตร (ต้องเลือกแล้วเขียน `**การตีความของเรา**` ถ้ายัง UNKNOWN)  
3. หลังก๊อบแท็ก คิด `tags.json → modifiers` ลง `Performance.Nums` (incr ตาม formula)  
4. ประมาณคราฟต์ต้องใช้กฎชุดเดียวกับของที่ได้  
5. **อย่า** เฉลี่ยเลเวลทุกช่องถ้าข้อกำหนดคือช่องแรก — แยก “เลเวลชิ้น” กับ “แอต”  
**เทส:** สูตรดาบ ช่องใบมีดกระดูก vs หิน → ของได้คนละแท็ก/คนละม็อด · ช่องด้ามไม่เปลี่ยนแท็กหลัก

### แผ่น C — บัฟอาหารให้ครบ type ที่ไฟล์มี  
**ไฟล์:** `StatusEffectCatalog.cs` `Player.StatusEffects.cs` `Player.Inventory.cs` (`IsFoodishStatus`)  
**ทำ:** map type 2 → fatigue biome ถ้ามีจุดต่อใน `SurvivalState`/`Weather` อยู่แล้ว · type 3 → derived ชั่วคราว · type 4 energy · คิดสูตรที่เลเวล SE จริงไม่ใช่ `MinLevel` · เติม id ที่ `effect_on` มีจริงลงรายการชนกัน  
**อย่า** ย่อยอาหารตามเวลาจนกว่าจะถอดความหมาย `energy_expression` ได้  
**เทส:** กาแฟ/ชา/อาหารร้อน-เย็น/พิษ — ไอคอน + หลอด/ค่าสถานะขยับตาม type

### แผ่น D — บัฟชุดตอน Equip/Unequip  
**ไฟล์:** `Player.cs` `HandleEquipMsg` + catalog แท็ก `status_effect.action=equip`  
**ทำ:** ใส่/ถอด `clothes`/`accessory` ถ้าจะให้ไอคอนความล้าตามชุด · อ่านแท็ก immune_* · **บวก `armor.defense` เข้า CurrentDerivedDefense** (ช่องว่างที่ผู้เล่นรู้สึกชัดกว่าไอคอน)  
**เทส:** สวม/ถอดเสื้อแล้ว fatigue หรือ defense เปลี่ยน · ล็อกเอาต์แล้วยังถูกต้อง

### แผ่น E — บังคับโต๊ะตาม `workbench_tags` ระดับ 20/40/60  
**ไฟล์:** `Player.Crafting.cs` `CheckWorkbench` + ส่ง `AppearArtifact.Tags` ให้ตรง derived  
**ทำ:** เทียบแท็กโต๊ะกับสูตรแบบเดียวกับ `client/Crafting/Recipe.cs`  
**พ่วง:** ถ้าระดับโต๊ะทุกหลังเป็น 60 ตลอด สูตรที่ “ต้องโต๊ะ 20” จะผ่านหมด — ต้องเก็บเลเวลรายหลังหรือแยกแท็กตามชนิดโต๊ะก่อน งานตกแต่งอาวุธ (Reform) ยังเข้าแผ่น F อยู่ดี  
**เทส:** สูตร `table_weapon:50` ที่โต๊ะธรรมดาต้องพลาดทั้ง UI และเซิร์ฟ · ที่ `weapon_table_01` ผ่าน

### แผ่น F — Modify / Reform / TechSupport (ตกแต่งอาวุธ-ชุด)  
**ไฟล์:** `Player.Crafting.cs` `Player.Repair.cs` `Cheats.MakeItem` (`ReformSlots` `ModifiableCount`) + `item/tech_support.json`  
**อย่าทำก่อน** แผ่น B ถ้า Reform คือการเพิ่มแท็กบนของเดิม  
**เทส:** สูตร `reform_nail` / `reform_temper` ที่โต๊ะระดับที่สูตรขอ · ของไม่หายเมื่อ Abort

### แผ่น G (เล็ก) — เลือกช่วง `performance.json` ให้ถูกเลเวล  
**ไฟล์:** `ItemPerformance.cs` ให้ใช้ logic เดียวกับ `LevelRange.Pick`  
**เทส:** อาวุธ urban เลเวล 35 vs 55 ได้สูตรคนละช่วง

---

## 12. สิ่งที่ PR นี้ไม่ได้ทำ

- ไม่มีโค้ดใน `server/Core` / `server/Support` / `server/GameCode`  
- ไม่รัน build (งานเอกสารอย่างเดียวตามสเปก)  
- ไม่เดาสูตรที่ไฟล์ไม่มี
