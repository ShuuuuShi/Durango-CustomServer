# แผนที่ระบบสัตว์ / สัตว์เลี้ยง (Animals · Pets)

เอกสารนี้เป็น **แผนที่ชั้นระบบ** ไม่ใช่สเปกงานทำ — อ่านจากซอร์สจริงใน `server/Core` + `server/Support` + `server/data/**`  
วันที่สแกน: 7 ก.ย. 2026 · คู่กับ `ROADMAP.md` · `TODO.md` · `protocol-coverage.md`

**ขอบเขตของเอกสารนี้**

- ครอบคลุม 5 ชั้น: สัตว์ป่า (A-W) · ทำให้เชื่อง (A-D) · กรง (A-C) · เซฟสัตว์เลี้ยง (A-P) · ขี่/พาหนะ (A-V)
- **ห้ามแก้ `server/GameCode/**`** — ไฟล์นั้นคือโปรโตคอล NEXON
- **ห้ามเปิดขี่ยาน (`MountVehicle`)** — `Player.Vehicle.cs` ตอบ Abort ตั้งใจแล้ว อย่าปลดในสไลซ์ถัดไป
- ชั้น A1–A2 ท้ายไฟล์เป็น **คำแนะนำเท่านั้น** ไม่ได้สั่งให้ลงมือใน PR นี้

ทุกตัวเลขด้านล่างมาจากการเปิดไฟล์จริง ไม่ได้เดา

---

## สวิตช์ Features ที่ปิดอยู่ทั้งที่โค้ดมี

`server/data/config.json` → `Features` (และสำเนาทุกเกาะใน `data/islands/isle0N/config.json`) ตั้งไว้:

| คีย์ | ค่าในไฟล์ | ความหมายใน `config-meta.json` |
|---|---|---|
| `Taming` | **false** | "จับ/ขี่ไดโนเสาร์" |
| `Livestock` | **false** | "เลี้ยงปศุสัตว์ (ให้นม)" |
| `WarpAccelerator` | false | รอยแยก — ปิดคู่กับ Taming/Livestock/Market |
| `Market` | false | ตลาดผู้เล่น |

`grep Livestock\|Features` ใน `server/**/*.cs` = **0 hit**  
ตัวอ่าน `config.json` ฝั่งเกมเพลย์มีแค่ `Support/WorldTuning.cs` (หมวด `World`) + หน้าแอดมินใน `Gateway.cs`  
⇒ **ธง `Taming` / `Livestock` ไม่ได้เป็นด่านปิดโค้ด** handler ใน `Player.Hunting.cs` / `Player.Domestication.cs` / `Player.Cage.cs` ทำงานอยู่แล้วแม้ธงเป็น false  
อย่าอ่านธงแล้วสรุปว่า "ระบบยังไม่มี" และอย่าเปิดธงโดยคิดว่าจะปลดล็อกอะไร — ไม่มีโค้ดอ่านมัน

---

## วงจรทั้งเส้น (ชั้นซ้อนกัน)

```
สัตว์ป่าบนเกาะ          A-W  AnimalManager + UseTamingAction
        │ จับติด (เลือด ≤ 30%) → ได้บังเหียน (Item.Ext = Reins)
        ▼
กรงฝึกให้เชื่อง         A-D  Player.Domestication.cs
        │ ใส่บังเหียน → เริ่มจับเวลา → ป้อนอาหาร → Finish → DomesticationResult
        │ เอาออก → บังเหียนที่มีสัตว์ข้างใน → UseItem ผูกพัน
        ▼
PetStore + ไฟล์เซฟ     A-P  Player.Animals.cs + Player.PetSave.cs
        │
        ├─► โรงเลี้ยง / สั่งงาน    A-C  Player.Cage.cs
        ├─► เรียกออกมา / กระเป๋า / กาชา
        └─► ขี่สัตว์ (Mount 802 มีแล้ว) · ขี่ยาน (A-V Abort ตั้งใจ)
```

ลำดับลงทะเบียนใน `Player.Systems.cs` สำคัญเพราะ `Connection.Recv` ตัวหลังทับตัวหน้า:

1. `RegisterAnimalHandlers()` — ลง Abort ชุดกรง/ฝึกไว้ก่อน
2. `RegisterCageHandlers()` — ทับกรงเป็นของจริง
3. `RegisterDomesticationHandlers()` — ทับฝึกเป็นของจริง
4. `RegisterHuntingHandlers()` — จับสัตว์ป่า
5. `RegisterPetSaveHandlers()` — โหลด/เซฟ PetStore
6. `RegisterVehicleHandlers()` — ขี่ยาน Abort

---

## A-W — สัตว์ป่า

**ไฟล์หลัก:** `server/Core/AnimalManager.cs` · `Player.Hunting.cs` · `Support/AnimalTypes.cs` · `Support/AnimalMotions.cs`

### สิ่งที่ AnimalManager ทำจริง

เขียนใหม่ทั้งก้อน (เซิร์ฟ NEXON ในตัวเกมไม่เคยเกิดสัตว์) อิงสองไฟล์ประกบ:

| แหล่ง | หน้าที่ |
|---|---|
| `herds.yml` ของเกาะ | จุดเกิดฝูง (กลุ่ม land/beach/ocean/…) |
| `region_templates.json` → `herds` | ฝูงไหนเป็นสัตว์อะไร เลเวลเท่าไร |

- id คงที่ทุกครั้งที่เปิดเซิร์ฟ เช่น `herd_land_7` (เหตุผลเดียวกับ POI)
- ค่าสถานะคิดจากสูตรใน `entity_types/animal.json` ผ่าน `StatFormula` (`unstable_factor` ตั้งเอง = 1.0)
- เพดาน **`MaxAnimalsPerRegion = 60`** — **ค่าของเรา** (แม่แบบสั่งได้ 150+ แต่ยังส่งทั้งเกาะ)
- เดินเล่น: รัศมี 500 / ความเร็ว 100 จาก `client/ClientAnimalActor.cs` · พัก 4–12 วิ (**ค่าของเรา**)
- ไล่กัด: `BuildChase` หยุดห่างเหยื่อ · ท่าโจมตี `ToAttackMotionMessage` · คลิปโจมตียาว **0.8 วิ** (**ค่าของเรา**)
- ซาก: เก็บ `DiedAt` แล้ว `Process` คืนชีพที่ `HomeTile` เมื่อครบเวลากำจัดซาก
- `World.OnCorpseDisposed`: `DisappearEntity` + ล้างประวัติแล่ + ให้ผู้เล่นลืมตัวนี้ เพื่อ `SyncAnimalVisibility` จะได้ส่ง `AppearAnimal` ใหม่

### `config.json` → `Animals` vs ของที่วิ่งจริง

ไฟล์ตั้งค่า (และสำเนาทุกเกาะ) มีตัวเลขตามที่สั่งตรวจ:

| คีย์ | ค่าในไฟล์ | ความหมายใน `config-meta.json` |
|---|---:|---|
| `LifetimeSeconds` | **300** | อยู่บนโลกได้กี่วิ แล้ว despawn + เกิดใหม่ที่จุดว่าง **(ไม่ทับหิน)** |
| `RespawnSeconds` | **60** | ตายแล้วกี่วิถึงเกิดตัวใหม่แทน |
| `CorpseSeconds` | **150** | ซากอยู่ในโลกกี่วิก่อนหาย |

**`AnimalManager.cs` ไม่ได้อ่านสามคีย์นี้** (`grep LifetimeSeconds\|CorpseSeconds\|RespawnSeconds` ใน `*.cs` = 0 นอกไฟล์ config)

ของที่วิ่งจริงตอนนี้:

| เรื่อง | ค่าจริงในโค้ด | ที่มา |
|---|---|---|
| อายุขัยบนโลก (lifetime) | **ไม่มี** — สัตว์อยู่จนกว่าจะตาย | ไม่มี despawn ตาม 300 วิ |
| เวลารอเกิดใหม่ (respawn) | **ไม่มีขั้นแยก** — ฟื้นทันทีเมื่อซากหมดเวลา ที่จุดบ้านเดิม | `ReviveAtHome` ไม่สุ่มจุดใหม่ |
| เวลากำจัดซาก | **`constants.json` → `herd.collectible_dispose_delay` = 180** (สำรอง 180) | `AnimalManager.CorpseDisposeDelay` |
| ข้ามหิน (skip rock) | **ไม่มี** — เกิดใหม่ที่ `HomeTile` เดิมเสมอ | `config-meta` เขียน "ไม่ทับหิน" แต่โค้ดไม่ตรวจ tile |

⇒ ตัวเลข 300 / 60 / 150 ใน config เป็นสเปกที่หน้าแอดมินโชว์ได้ แต่**ยังไม่ถูกต่อเข้า AnimalManager**  
อย่าไปเปลี่ยนค่าใน config แล้วคาดว่าเกาะจะเปลี่ยนพฤติกรรม

### จับสัตว์ป่า → บังเหียน (ทางเข้าเดียวที่ไม่พึ่งร้านเงิน)

`UseTamingAction` (1900) ใน `Player.Hunting.cs` — **ทำจริง**

เงื่อนไขจาก `constants.json` → `taming` (`Support/TamingTuning.cs`):

| คีย์ | ค่า |
|---|---|
| `tamable_hp_rate` | 0.3 |
| `taming_time` | 3.0 วิ (ส่ง `Timer` ที่ seq เดิม) |
| `taming_cooltime` | 5.0 วิ |
| `success_ratio` / `adjust_by_life_ratio` | สูตรจริง — **การตีความของเราคือเอาสองค่ามาคูณ** |

จับติด → `Cheats.MakeItem(taming_result)` + `Rewarded { TamingCompletedEffect }` + สัตว์ตายบนโลก  
บังเหียนคราฟต์ไม่ได้เลยสักสูตร — ทางเล่นได้คือจับจากสัตว์ป่า (67 ชนิดมี `taming_result` / 64 ชนิด `tamable: true`)

---

## A-D — ทำให้เชื่อง (กรงฝึก)

**ไฟล์หลัก:** `server/Core/Player.Domestication.cs`

หน้าจอฝั่งเกม (`DomesticCageGroup`) refresh จาก `ArtifactState.DomesticCage` ตรง ๆ ไม่ได้ถามเป็น message  
⇒ ทุกครั้งที่สถานะเปลี่ยนต้อง `ArtifactManager.UpdateDomesticCage` (ยิง `ArtifactStateUpdated` + เซฟโลก)

### ลำดับที่เกมยิงจริง

| ปุ่ม | message | TypeCode | คำตอบที่เกมรอ |
|---|---|---:|---|
| ใส่บังเหียน | `PutInReinsToCage` | 694351 | `OK` (`.All(IsSuccess)`) |
| 길들이기 | `StartDomestication` | 694357 | `OK` |
| 먹이주기 | `PutItemsForDomestication` | 694352 | **ยิงทิ้งไม่รอ reply** — ล้มเหลวส่ง `Abort` ไม่มี seq |
| 취소 | `CancelDomestication` | 694354 | `OK` — รีเซ็ตเวลา+โอกาส (อาหารไม่คืน ตามกล่องยืนยันเกม) |
| 결과 확인 | `FinishDomestication` | 694355 | **`DomesticationResult` (694356) เท่านั้น** — ตอบ `OK` = UI เงียบ |
| 가방에 넣기 | `TakeOutReinFromCage` | 694359 | `OK` |
| 풀어주기 | `ReleaseReinFromCage` | 694353 | **`OK` เท่านั้น** (`.On<OK>`) — ไม่คืนบังเหียน |

ห้ามยัดบังเหียนที่ `Reins.Domesticated == true` กลับเข้ากรงฝึก (กันวนสุ่มแรงก์)

### สูตร + ค่าที่เราตั้งเอง

จาก `constants.json` → `pet`:

- `domesticate_time` = `max(starts_at + total_time * decrease_limit, ends_at - (total_time * d_ratio + d_time))`
- `domesticate_probability` = `min(max_prob, current_prob + d_success_rate)`
- `domesticate_decrease_limit` = 0.5
- `performance_reference` ชี้ชื่อคีย์ใน `pet_food`
- `advanced_tameable` = `[2010, 2047, 2088, 2025, 2023, 2020]` (vehicle_entity_type)

ไม่มีในไฟล์ข้อมูล → `DomesticationTuning` (**ค่าของเรา**):

| | ทั่วไป | advanced |
|---|---:|---:|
| เวลาฐาน | 1800 วิ (30 นาที) | 5400 วิ |
| โอกาสตั้งต้น | 0.5 | 0.25 |
| เพดานโอกาส | 1.0 | 1.0 |

ไม่มี timer เดินจริง — เก็บ `DomesticateSince` / `Until` แล้ว `Finish` ค่อยตรวจ (แบบงานวิจัย)

`Finish` สำเร็จ: สุ่มแรงก์จาก `pets_for_client.json` → `available_ranks` (น้ำหนักเป็นค่าของเรา ชุดเดียวกับ `RevertPetRank`) + ส่ง `Stat`/`Original` ครบ 7 ช่อง  
ล้มเหลว: ลบออกจากกรง (สัตว์หนี) แล้วส่ง `DomesticationResult { Domesticated = false }`

### เอาออกจากกรง + ผูกพัน

- ยังไม่เชื่อง → คืนบังเหียนเปล่า (`Item.Ext = Reins`, `Domesticated = false`)
- เชื่องแล้ว → คืนบังเหียนที่ **มี `Reins.Pet` ข้างใน** (`Domesticated = true`) ไม่ยัดเข้า PetStore ตรง ๆ
- ผู้เล่นกด 귀속 → `UseItem` → `TryImprintRein` (`Player.Inventory.cs`) → เข้า `PetStore` + `SendPetsInfo`

`NormalizeReinItems()` เติม `Item.Ext = Reins` ให้บังเหียนในกระเป๋าก่อน `SendInventory`  
**ห้ามทับ `Ext is Reins` ที่มีสัตว์ข้างในแล้ว** — ทับ = สัตว์ที่ฝึกมาหายตอนเข้าเกม

---

## A-C — โรงเลี้ยง (GrowCage)

**ไฟล์หลัก:** `server/Core/Player.Cage.cs` · `Support/CageTypes.cs`

แตะกรง → `Interaction.Cage` (514) / `OpenDomesticCage` (533) — `Player.cs` ใส่ให้แล้วเมื่อ blueprint มี component `GrowCage` / `DomesticCage`  
หน้าจออ่าน `ArtifactState.Cage` ตรง ๆ — `CageTypes.Apply` เติมตอนสร้าง/โหลดโลก  
`CageTypes.NormalizeLoaded` แปลง `JObject` กลับเป็น `GrowCage`/`Cage` (ไม่ทำ = แพ็กเก็ตสถานะสิ่งปลูกสร้างเลื่อนทั้งก้อน)

ความจุคิดจาก `performance.json` → `cage` → `cage_size` (สูตรตามเลเวล) ที่เลเวล 60 (**ค่าของเรา** เพราะเซิร์ฟยังไม่เก็บเลเวลหลัง):

| prototype | สูตร | ที่เลเวล 60 |
|---|---|---:|
| `cage_01_2` | `min(45, 15 + 5 * int(level/10))` | 45 |
| `cage_01_4` | `min(100, 40 + 10 * int(level/10))` | 100 |
| `cage_01_6` | `min(150, 60 + 15 * int(level/10))` | 150 |

ที่กินในกรง = `performance.json` → `reins` → `size` ไม่ใช่จำนวนตัว

### handler ที่ทำจริง (ทับ Abort ของ `Player.Animals`)

| message | TypeCode | ทำอะไร |
|---|---:|---|
| `PutInCage` | 809 | สัตว์ยังอยู่ใน PetStore แต่ติด `CageInfo` (เกมใช้ปิดปุ่มเรียก/ปล่อย/แปลงบังเหียน) |
| `TakeOutFromCage` | 810 | ห้ามตอนมีงานค้าง · ห้ามทับทั้งก้อนจากกรงทับ PetStore (หลอดจาก JSON อาจไม่ครบ) |
| `FeedInCage` | 65101 | เติมความอิ่ม (`vigor`) + ร่นเวลางานด้วยสูตร `task_time` |
| `StartPetTask` | 65102 | หักความอิ่มตอนเริ่ม · งานผลิตต้องตรง `by_product` |
| `CancelPetTask` | 65103 | ไม่คืนอะไร (ตามกล่องยืนยันเกม) |
| `FinishPetTask` | 65104 | ของไม่พอในกระเป๋า = Abort **โดยไม่ลบงาน** · ส่ง `Rewarded` + `PetTaskFinishedEffect` |
| `GetAvailableTask` | 65106 | ตอบ `AvailableTask` ทั้งผลิต+ฝึก · **ไม่กรองเลเวล** (เกมโชว์ปุ่มเทาเอง) |

งาน 48 อันใน `pet/pet_task.json` · ชนิดผลผลิต 7 ค่า: `default` · `mammal` · `mammal_no_milk` · `mammal_honey` · `mammal_water` · `feather` · `iguana`

`WriteCage` ผลัก `ArtifactState` ที่ซ่อม `EntityId` เอง — เพราะ `AppearArtifact.States.EntityId` มักว่าง แล้วฝั่งเกม `Find(null)` ทิ้งแพ็กเก็ตเงียบ

---

## A-P — เซฟสัตว์เลี้ยง + ร้านในหน่วยความจำ

**ไฟล์หลัก:** `server/Core/Player.PetSave.cs` · `PlayerContext.Pets` · `Player.Animals.cs` (`PetStore`)

`PetStore` เป็น static dict ในหน่วยความจำ — `Player.Animals.cs` เปิด public ไว้ให้เสียบ persistence โดยไม่แตะ handler  
`Player.PetSave.cs` คือตัวที่มาเสียบ:

| จังหวะ | ทำอะไร |
|---|---|
| เข้าเกม | `LoadPersistedState` — `PlayerContext.Pets` → `PetStore` + โหลด `_deathCount` |
| ทุก `ContextChanged` | `FlushPersistedState` เขียนกลับ context **ก่อน** `GameServer` เซฟไฟล์ |
| สายหลุด | `SaveOnDisconnect` → `OnContextChanged` (หลาย handler สัตว์ไม่เรียกเซฟเอง) |

`PetSaveData` เก็บทั้งก้อน `Messages.Pet` + grazing/bag + เพดานหลอด + ของกาชาที่ค้าง  
ตอนโหลดต้องประกอบใหม่:

1. `IsSpawned` / `IsBoarding` = false (ไม่มีสัตว์อยู่ในโลกหลังรีสตาร์ต)
2. หลอด Life/Hungry สร้างใหม่ — `GaugeConverter` ย่อเหลือ `{min,max,cur}` เส้นแนวโน้มหาย
3. `RetryCost` คิดใหม่จาก `costs.json` — `Money` เป็น readonly อ่านกลับไม่ได้ (จะโชว์ฟรี)

`ItemExtRepair` ใน `PlayerContext` แปลง `Item.Ext` จาก `JObject` กลับเป็น `Reins` ฯลฯ ตั้งแต่โหลดไฟล์ — ครอบทั้งกระเป๋าผู้เล่นและกระเป๋าสัตว์

### handler สัตว์เลี้ยงใน `Player.Animals.cs` (ทำจริงเมื่อมีตัวในสโตร์)

| message | TypeCode | สถานะ |
|---|---:|---|
| `GetPetsInfo` | 14198037 | จริง — หัวใจหน้าจอสัตว์เลี้ยง (ต้องตอบ `PetsInfo`) |
| `GetPreviewPet` | 181120 | จริง |
| `GetPetInventory` | 49823 | จริง |
| `SpawnPet` / `ReturnPet` | 923570 / 808 | จริง — ส่ง `AppearPet` / `DisappearPet` |
| `RenamePet` | 804 | จริง |
| `PutInItemsIntoPet` / `TakeOutItemsFromPet` | 806 / 807 | จริง |
| `Feeding` | 805 | จริง (ให้อาหารตัวที่เรียกออกมา ไม่ผ่านกรง) |
| `ReleasePet` | 74012 | จริง (มีกล่องยืนยันของเกม) |
| `ResurrectPet` | 239187 | จริง ถ้ามีไอเทมแท็ก `medicine_animal` |
| `GrazePets` | 800000 | จริง |
| `DiscoverAnimal` | 5002 | จริง |
| กาชา milestone / skill / rank | ชุด 7401x | จริง |
| `UsePetActiveSkill` | 800200 | จริง |
| **`ReinifyPet`** | **74013** | **Abort ตั้งใจ** — แปลงกลับเป็นบังเหียนยังไม่มีทางเอากลับคืนที่ปลอดภัยถ้าวงจรกรงพัง |

เพดานเลี้ยง `PetTuning.MaxTamingPet = 10` (**ค่าของเรา**) ส่งใน `Derived.MaxTamingPet` — ไม่ส่ง = ป้าย "N / 0"

---

## A-V — ขี่ / พาหนะ

สองระบบคนละเรื่อง ห้ามปน:

### ขี่สัตว์เลี้ยง — มีแล้ว อย่าไปแตะในงานยาน

`Mount` (802) / `Unmount` (803) ใน `Player.Animals.HandleMountPetMsg` — **ทำจริง**  
เกมยิงจาก interaction บนสัตว์ที่เรียกออกมาเอง (`VehiclePet.ContextActionFinder`) ไม่รอ reply รอ push ก้อน `Pet`  
ตั้ง `IsBoarding` แล้ว broadcast ทั้งโลก

**สไลซ์ถัดไปห้าม "เปิด Mount"** ในความหมายของยาน — ของสัตว์เปิดอยู่แล้ว

### ขี่ยานสิ่งปลูกสร้าง — Abort ตั้งใจ (`Player.Vehicle.cs`)

เหตุผลรวม: ของเหล่านี้ขี่บนสถานะที่เซิร์ฟยังไม่เคยเขียน (`ArtifactState.Catapult` / `.Warpaccelerator` = null ตลอด)

| message | TypeCode | คำตอบ | ทำไม |
|---|---:|---|---|
| `MountVehicle` | 327918 | **Abort** "ยังไม่เปิดใช้งานเครื่องยิงหิน" | ไม่มี `CatapultState` ⇒ นั่งบนเครื่องที่ยิงไม่ออก |
| `UnmountVehicle` | 192834 | **ไม่ Abort** — ล้าง `BoardingOn` ถ้าเป็น Vehicle | เกมไม่ลงเอง รอ `PlayerDisplay` |
| `MountAirBalloon` | 123987 | **Abort** "ยังไม่เปิดใช้งานบอลลูน" | ไม่มีหักตั๋ว / ปลายทาง / บังคับลง 15 นาที |
| `UnmountAirBalloon` | 135867 | **ไม่ Abort** — ล้างถ้าเป็น AirBalloon | เกมลงเองแล้วบอกมา |
| `FireProjectileFromVehicle` | 203493 | **Abort** | ไม่มีตารางเวลายิง/กระสุนให้คิด |
| `ParticipateAcceleration` | 21112513 | **Abort** | ไม่มีคลื่นสัตว์ — ตอบ OK = เสียสสารวาร์ปฟรี |
| `ReceiveAcceleratorRewards` | 21112514 | **Abort** | ห้ามแต่งรางวัลปลอม |

`HandleTouchMsg` ยังไม่แจก `Interaction.MountVehicle` — เมนูเครื่องยิงหินไม่โผล่  
`MountAirBalloon` ฝั่งเกมสร้าง interaction เองได้ถ้ามีบอลลูนในฉาก ⇒ ต้องมี handler กันค้าง

**อย่าแทน Abort ชุดนี้ด้วยของจริงจนกว่าจะมี API เขียน `ArtifactState.Catapult` / `.Warpaccelerator` + ช่องเซฟ**

---

## ตาราง handler: ของจริง vs Abort

สัญลักษณ์: **จริง** = ทำตามโปรโตคอลที่เกมรอ · **Abort** = ตั้งใจปฏิเสธ · **ทับ** = ตัวหลังชนะ

| ชั้น | message | TypeCode | ตัวแรก (Animals) | ตัวชนะ | หมายเหตุ |
|---|---|---:|---|---|---|
| A-W | `UseTamingAction` | 1900 | — | **จริง** Hunting | จับป่า → บังเหียน |
| A-D | `PutInReinsToCage` | 694351 | Abort | **จริง** Domestication | |
| A-D | `StartDomestication` | 694357 | Abort | **จริง** | |
| A-D | `CancelDomestication` | 694354 | Abort | **จริง** | |
| A-D | `PutItemsForDomestication` | 694352 | Abort | **จริง** | ไม่รอ reply |
| A-D | `FinishDomestication` | 694355 | Abort | **จริง → `DomesticationResult`** | ห้ามตอบ OK |
| A-D | `TakeOutReinFromCage` | 694359 | Abort (Animals/Inventory) | **จริง** | คืนบังเหียน / บังเหียน+สัตว์ |
| A-D | `ReleaseReinFromCage` | 694353 | Abort | **จริง → OK** | ไม่คืนของ |
| A-C | `PutInCage` | 809 | Abort | **จริง** Cage | |
| A-C | `TakeOutFromCage` | 810 | Abort (Inventory) | **จริง** | |
| A-C | `FeedInCage` | 65101 | Abort | **จริง** | |
| A-C | `StartPetTask` | 65102 | Abort | **จริง** | |
| A-C | `CancelPetTask` | 65103 | Abort | **จริง** | |
| A-C | `FinishPetTask` | 65104 | Abort | **จริง** | |
| A-C | `GetAvailableTask` | 65106 | จริง (กรองไม่ครบ) | **จริง** Cage | กรอง `by_product` |
| A-P | `ReinifyPet` | 74013 | **Abort** | Abort | ตั้งใจ — ยังไม่เปิด |
| A-P | `Mount` / `Unmount` | 802 / 803 | **จริง** | จริง | ขี่สัตว์ ไม่ใช่ยาน |
| A-V | `MountVehicle` | 327918 | — | **Abort** | อย่าเปิด |
| A-V | `MountAirBalloon` | 123987 | — | **Abort** | อย่าเปิด |
| A-V | `FireProjectileFromVehicle` | 203493 | — | **Abort** | อย่าเปิด |
| A-V | `UnmountVehicle` / `UnmountAirBalloon` | 192834 / 135867 | — | ล้างสถานะ ถ้าขี่ชนิดนั้นอยู่ | ห้าม Abort |

---

## ข้อมูลเกมที่ใช้ (assets)

ตรวจจำนวนในไฟล์จริง 7 ก.ย. 2026:

| ไฟล์ | ที่ใช้ | จำนวน / หมายเหตุ |
|---|---|---|
| `entity_types/animal.json` | A-W สูตรเลือด/ตี/กัน · `tamable` · `taming_result` · `preferred_food_tag` · scale | **214** ชนิด · `tamable: true` = 64 · มี `taming_result` = 67 |
| `pet/pets_for_client.json` | A-D/A-C/A-P `rein_id` · `available_ranks` · `vehicle_entity_type` · `by_product` · `is_ridable` | **74** ชนิด |
| `pet/pet_task.json` | A-C งานผลิต/ฝึก | **48** งาน |
| `pet/pet_exp.json` | A-C/A-P exp ต่อเลเวล | 74 คีย์ (คู่กับชนิดสัตว์) |
| `pet/pet_active_skills.json` | A-P สกิลแอคทีฟ | **37** สกิล |
| `pet/pet_active_skill_conditions.json` | A-P น้ำหนักกาชาสกิล | มี `weight` จริง |
| `performance.json` → `reins` | บังเหียน: `pet_entity_type` / `vehicle_entity_type` / `size` / `speed` / `capacity` / `hungry_*` | **101** prototype · ช่วง `"[1, 60]"` |
| `performance.json` → `pet_food` | `vigor` · `decrease_domesticate_time(_ratio)` · `increase_domesticate_success_rate` · `decrease_grow_time(_ratio)` + แท็กสเตต | **196** prototype · บางตัวมีหลายช่วงเลเวล |
| `performance.json` → `cage` | สูตรความจุกรง 6 ชนิด | 4 โรงเลี้ยง + 2 กรงฝึก |
| `constants.json` → `pet` | สูตรฝึก/งาน · `advanced_tameable` · `milestone_level` · `default_grazable_count=5` · `reinify_tags` · `resurrection_tags` | ดูหัวข้อ A-D / A-C |
| `constants.json` → `taming` | จับป่า | ดู A-W |
| `constants.json` → `herd.collectible_dispose_delay` | ซากหาย | **180** วิ (คนละตัวกับ config `CorpseSeconds` 150) |
| `tags.json` | แท็ก milestone (`life_span_plus_*` เป็น **วัน**) | 25 แท็ก `required_performance = animal_stat` |
| `costs.json` | ราคาหมุนกาชา | `pet_revert_rank` / `pet_revert_milestone` / `pet_revert_active_skill` |

สองเลขห้ามสลับตอนประกอบ `DomesticationInfo`:

- `EntityType` = `vehicle_entity_type` → เปิด `animal.json` (รูป/ชื่อ)
- `PetEntityType` = `pet_entity_type` → เปิด `pets_for_client.json` (อาหาร/แรงก์)

---

## P1 ที่ยังค้าง (ตรวจซ้ำในโค้ด 7 ก.ย. 2026)

รายการเดียวกับ `ROADMAP.md` / `TODO.md` ข้อกรง — สถานะปัจจุบันหลังไล่ซอร์ส:

### 1. `Item.Ext = Reins` (หน้าต่างเลือกสัตว์ว่าง)

**เคยเป็น blocker จริง** — ฝั่งเกมกรองด้วย `data.Reins.HasValue` จาก `Item.Ext` เท่านั้น ไม่ดูบล็อก Performance

ตอนนี้โค้ดตั้งครบสามจุดแล้ว:

- `Cheats.MakeItem` สร้าง `new Reins { PetEntityType, VehicleEntityType, Size, … }`
- `NormalizeReinItems` กวาดกระเป๋าตอนเข้าเกม (ข้ามของที่เป็น `Reins` อยู่แล้ว)
- `ItemExtRepair` แปลง `JObject` จากไฟล์เซฟตั้งแต่โหลด

เหลือความเสี่ยง: ไอเทมเก่าในเซฟ / ทางสร้างไอเทมที่ข้าม `MakeItem` / `Pack` ไม่มี `else` ถ้า Ext ยังเป็น `JObject`  
⇒ P1 กลายเป็น **ยืนยันในเกม** ว่าหน้าต่างเลือกสัตว์ไม่ว่าง หลังจับสัตว์ป่าได้บังเหียน

`PerformanceYaml.Rein` อ่าน `vehicle_entity_type` + `size` แล้ว (ไม่ขาดคีย์ในคลาส)

### 2. `PetFood` ฟิลด์เลี้ยง / พรีวิวฝั่งเกม

ไฟล์จริงมีครบ: `vigor` + `decrease_domesticate_*` + `increase_domesticate_success_rate` + `decrease_grow_time*`

ช่องว่างที่เหลือ:

- `PerformanceYaml.PetFood` ประกาศแค่ **`vigor`** — `GetPetFood` ใช้ตอนประกอบมือใน `MakeItem`
- `MakeItem` แนบ `pet_food` แค่ `{ vigor }` ก่อน แล้ว `ItemPerformance.MergeInto` เติมคีย์ที่เหลือด้วย `TryAdd`
- **`ItemPerformance.Of` หยิบช่วงเลเวลแถวแรกใน JSON ไม่ใช่ช่วงที่ตรงเลเวลไอเทม**  
  `pet_food` หลายชนิดมี `"[60, 70]"` เป็นแถวแรก ⇒ พรีวิวฝั่งเกมอาจอ่านค่าช่วงผิด
- เซิร์ฟฝึก/เลี้ยงอ่าน `performance.json` ตรง ๆ เอง (`DomesticationTables` / `CageTables.FoodOf`) จึงร่นเวลาได้จริงแม้พรีวิวจอเป็น 0

คอมเมนต์ใน `Player.Cage.cs` ยังเตือนว่าจอโชว์ "ไม่ร่น" ทั้งที่เซิร์ฟร่นให้ — ตรงกับช่องนี้

### 3. `LifeSpan` หน่วยวัน vs วินาที

`PetTuning.LifeSpanDaysBase = 30` (**ค่าของเรา**, หน่วยวัน ให้ตรงแท็ก `life_span_plus_*`)  
ฝั่งเกมอ่าน `Derived.LifeSpan` เป็น **วินาที** (`TimedeltaFormatter` ตารางเริ่มที่ 86400)

`PetFactory.ToWireUnits` คูณ `SecondsPerDay` **หลังบวกแท็กแล้ว** — ถูกทาง  
แต่ `DerivedOf` มี early return:

```1544:1546:server/Core/Player.Animals.cs
                [Derived.LifeSpan] = (float)PetTuning.LifeSpanDaysBase
            };
            if (tags == null || tags.Count == 0) return d;
```

สัตว์เพิ่งเชื่อง (ยังไม่มีแท็ก) **ข้าม `ToWireUnits`** ⇒ ส่งเลข 30 ดิบ ๆ = ป้าย "30초" แล้ว `Math.Min` กับเวลาจริงค้างที่นั้น  
นี่คือช่อง P1 ที่ยังปิดไม่ได้ในโค้ด

---

## สไลซ์ถัดไป — คำแนะนำเท่านั้น (อย่าทำใน PR เอกสารนี้)

เรียงตามสิ่งที่บล็อกวงจร "จับ → ฝึก → เลี้ยง" โดย**ไม่เปิดขี่ยาน**

### A1 — ปิดช่อง P1 ที่ยังเป็นบั๊กตัวเลข/พรีวิว

- ให้ `PetFactory.DerivedOf` เรียก `ToWireUnits` แม้ `tags` ว่าง (อายุขัย 30 วัน → วินาที)
- ให้ `ItemPerformance.Of` เลือกช่วงเลเวลที่ตรงเลเวลไอเทม (หรือให้ `MakeItem` แนบคีย์ `pet_food` ครบจากช่วงที่ถูก)
- เทสในเกม: จับสัตว์ได้บังเหียน → หน้าต่างใส่กรงไม่ว่าง · ป้อนอาหารแล้วแถบเวลาฝั่งเกมขยับตรงเซิร์ฟ

อย่าเพิ่งต่อ `config.json` → `Animals` (300/60/150 / ข้ามหิน) เข้า `AnimalManager` ในสไลซ์นี้ — คนละระบบกับวงจรเลี้ยง และค่าซากที่วิ่งอยู่คือ 180 จาก `constants.json`

### A2 — เทสวงจรครบบนเซิร์ฟจริง (ยังไม่เปิด Mount ยาน)

เส้นที่ต้องเดินให้จบในเกม ไม่ใช่แค่ compile:

1. คราฟต์เครื่องมือแท็ก `capturable` → ตีสัตว์ `tamable` เลือด ≤ 30% → `UseTamingAction` ได้บังเหียน
2. สร้างกรงฝึก (`DomesticCage`) → ใส่บังเหียน → เริ่มฝึก → ป้อนอาหาร → `Finish` ได้ `DomesticationResult`
3. เอาออก → กดผูกพัน (`UseItem`) → สัตว์โผล่ใน `GetPetsInfo` → รีสตาร์ตเซิร์ฟแล้วยังอยู่ (A-P)
4. ใส่โรงเลี้ยง → สั่งงาน → เก็บผล

**อย่าทำใน A2:** แทน `MountVehicle` / บอลลูน / เครื่องเร่งวาร์ป · อย่าเปิด `Features.Taming` โดยคิดว่าจะปลดอะไร (ไม่มีผู้อ่าน) · อย่าแตะ `server/GameCode/**`

`ReinifyPet` เปิดได้ทีหลังเมื่อวงจร A-D เทสผ่านแล้วเท่านั้น (ปุ่มนี้แปลงสัตว์กลับเป็นบังเหียน — ทำก่อนวงจรคืนมี = ของหาย)

---

## ไฟล์ที่อ่านประกอบ (อย่าแก้ GameCode)

| ชั้น | Core / Support | ข้อมูล |
|---|---|---|
| A-W | `AnimalManager.cs` `Player.Hunting.cs` `AnimalTypes.cs` `TamingTuning.cs` | `animal.json` `herds.yml` `region_templates.json` `config.json`→Animals (ยังไม่ถูกอ่าน) `constants.json`→taming/herd |
| A-D | `Player.Domestication.cs` `Player.Inventory.cs` (`TryImprintRein`) `Cheats.MakeItem` | `constants.json`→pet `performance.json`→reins/pet_food |
| A-C | `Player.Cage.cs` `CageTypes.cs` | `pet_task.json` `pets_for_client.json` `performance.json`→cage |
| A-P | `Player.PetSave.cs` `PlayerContext.cs` `Player.Animals.cs` | ไฟล์ `.player` ช่อง `pets` |
| A-V | `Player.Vehicle.cs` `Player.Animals.HandleMountPetMsg` | — |

จุดต่อสายเดียว: `server/Core/Player.Systems.cs`
