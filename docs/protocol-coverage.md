# โปรโตคอล — เกมยิงอะไร เซิร์ฟรับอะไรแล้ว

สร้างด้วย `python tools/scan-protocol.py --md` — อ่านจากซอร์สจริง ไม่ได้เดา
คอลัมน์ **ทำอะไร** เขียนเพิ่มด้วยมือจาก `server/GameCode/Messages/<ชื่อ>.cs` (ฟิลด์จริง) + โค้ดฝั่งเกมใน `client/` + ข้อมูลเกมใน `server/data/assets/`

| | จำนวน |
|---|---:|
| message ทั้งหมดในโปรโตคอล | 852 |
| เซิร์ฟรับได้ตอนนี้ | 42 |
| ตัวเกมยิงออกมาจริง | 391 |
| ยังไม่มี handler | 358 |

## อ่านตารางนี้ยังไง

**ทิศทางของ message ไม่เหมือนกัน** — ต้องดูให้ออกก่อนวางแผน:

- **เกม → เซิร์ฟ (ส่วนใหญ่)** = เซิร์ฟต้องเขียน handler รับ แล้วตอบกลับ
- **เซิร์ฟ → เกม** = เซิร์ฟต้อง *ส่ง* ออกไป ไม่ใช่รับ — ตัวที่ตัวอย่างชี้ไป `client/Durango.Online/Player.cs` หรือ `World.cs` เกือบทั้งหมดเป็นแบบนี้ เพราะ `client/Durango.Online/` **คือเซิร์ฟจำลองที่ฝังมาในตัวเกม** (โหมดออฟไลน์) ไม่ใช่โค้ดฝั่งผู้เล่น → ในตารางทำเครื่องหมาย `[เซิร์ฟ→เกม]`
- คู่ request/response ระบุไว้ในคำอธิบาย เช่น `GetRecipes` → `Recipes`

**สัญลักษณ์**

- `✅` = ตอนสแกนยังไม่มี handler แต่ตอนนี้ `server/Core/Player.cs` รับได้แล้ว (ดูหัวข้อ "ตารางนี้เก่าไปแล้วบางส่วน")
- `(?)` = **เดา** — ประกอบจากชื่อ+ฟิลด์ ยังไม่ได้เปิดโค้ดรอบจุดเรียกยืนยัน (มี 23 จุด)
- ไม่มี `(?)` = ยืนยันแล้วอย่างน้อย 2 ทาง: ฟิลด์จริงใน `Messages/<ชื่อ>.cs` + เมธอด/โค้ดที่ยิงมันในเกม (บางตัวมีข้อความ UI เกาหลีในโค้ดยืนยันซ้ำอีกชั้น)

**หมายเหตุเรื่องหมวด** — สคริปต์แบ่งหมวดจาก *คำในชื่อ message* ไม่ได้ดูระบบจริง เลยมีที่ผิดที่อยู่หลายตัว เช่น `MakeSection`/`MakeClan`/`MakeParty` ไปอยู่หมวด "คราฟต์" เพราะขึ้นต้นด้วย Make, ที่ดิน (Estate) ทั้งชุดไปอยู่หมวด "สกิล/เลเวล", `GetClanCreationCosts` ไปอยู่ "เอาชีวิตรอด" — ในคำอธิบายจะบอกไว้ว่าจริง ๆ อยู่ระบบไหน

## ตารางนี้เก่าไปแล้วบางส่วน

ตอนตรวจ (5 ก.ย. 2026) `server/Core/Player.cs` รับ message ได้ **63 ตัว** ไม่ใช่ 42 — มี **21 ตัวในตาราง "ยังไม่มี handler" ข้างล่างที่รับได้แล้ว**:

`GetAdvisorTargets` · `GetArchipelago` · `GetAvailableEmotions` · `GetDiscoveryInfo` · `GetExploredPOIs` · `GetPOICount` · `GetPersonalRegionInfo` · `GetPioneerGradeInfo` · `GetRegion` · `GetResistanceExpCaps` · `GetRoutes` · `GetSailingBackCost` · `GetSkills` · `GetStatistics` · `GetStatusEffects` · `GetTargetTitle` · `GetTitles` · `SailingBack` · `ToggleStatusEffect` · `TravelByRegion` · `TravelByRegionInArchipelago`

ทำเครื่องหมาย `✅` ไว้ในตารางแล้ว ตัวเลขสรุปด้านบนยังเป็นของตอนสแกน

---

## ต่อสู้/ล่า

ระบบต่อสู้: ผู้เล่นกดใช้ "ท่า" (battle action) ใส่เป้าหมาย → เซิร์ฟตัดสินความเสียหาย/ค่าประสบการณ์ แล้วผลักผลกลับด้วย `Damaged`/`BattleLog`/`EntityDied`. ตายแล้วต้องมีเส้นทางฟื้นคืนชีพ (ฟรีที่จุดเกิด / จ่ายบัตรเกิดตรงนั้น / ให้เพื่อนทำ CPR). ข้อมูลท่าอยู่ `server/data/assets/player/player_battle_actions.json` ค่าตัวเลขต่อสู้อยู่ `constants.json` (`success_probability`, `base_critical_damage_bonus`, `pvp_*_damage_multiplier`, `residual_aggro` ฯลฯ)

| message | TypeCode | ทำอะไร | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---|---:|---|
| `ExitBattle` | 3496 | เลิกโหมดต่อสู้ (ปลดล็อกเป้า กลับสู่โหมดปกติ) ไม่มีฟิลด์ — คู่กับ `EnterBattle`/`BattleEnded` ที่เซิร์ฟส่งกลับ | 1 | `client/CombatSystem.cs` |
| `Revive` | 2101 | ขอเกิดใหม่หลังตาย เลือกเกิดที่ warphole ตามช่อง `WarpholeTile` (ถ้าว่าง = จุดเกิดเริ่มต้น) | 1 | `client/PlayerController.cs` |
| `UseBattleAction` | 3440 | ใช้ท่าต่อสู้ `ActionId` ใส่เป้า `TargetEntityId` (หรือลงช่อง `TargetTile`) ณ เวลา `StartAt` | 1 | `client/Durango.Logic.Combat/UsingAction.cs` |
| `ReviveImmediately` | 210201 | จ่ายบัตร (`VoucherId`) เพื่อฟื้นคืนชีพทันทีตรงจุดที่ตาย — ราคาอยู่ `costs.json` → `revive_immediately`; ก่อนหน้านี้เกมถาม `GetReviveImmediatelyInfo` → `ReviveImmediatelyInfo` | 1 | `client/PlayerController.cs` |

## สัตว์/เลี้ยง

ระบบสัตว์เลี้ยงเป็นระบบที่ใหญ่ที่สุดระบบหนึ่ง มี 5 ชั้นซ้อนกัน: (1) **ทำให้เชื่อง** — ตีสัตว์ป่าให้สลบ ใส่บังเหียน (rein) แล้วเอาเข้ากรง (2) **เลี้ยง/ให้อาหาร** ในโรงเลี้ยง (3) **สั่งงาน** ให้ผลิตของ (4) **สุ่มพัฒนา** — milestone / rank / active skill เป็นระบบกาชาที่รีโรลได้ด้วยบัตร (5) **ใช้งาน** — เรียกออกมาข้างตัว ขี่ ใส่ของในกระเป๋าสัตว์
ข้อมูลรองรับครบพอควร: `server/data/assets/pet/` (`pets_for_client.json` 74 ชนิด, `pet_task.json` 48 งาน, `pet_active_skills.json`, `pet_exp.json`) + `entity_types/animal.json` 214 ชนิดสัตว์ (มีธง `tamable`, `preferred_food_tag`, `rein_id`)

| message | TypeCode | ทำอะไร | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---|---:|---|
| `AcceptPetRank` | 74016 | ยืนยันรับแรงก์ที่เพิ่งสุ่มได้ (จบการรีโรล) — คู่กับ `RevertPetRank` | 1 | `client/PetManager.cs` |
| `RevertPetRank` | 74014 | ขอสุ่มแรงก์สัตว์ใหม่ → เซิร์ฟตอบ `RevertPetRankCandidate` (แรงก์+แท็กที่จะได้) ให้ดูก่อนกดรับ; ค่าใช้จ่ายที่ `costs.json` → `pet_revert_rank` | 1 | `client/PetManager.cs` |
| `CancelPetTask` | 65103 | ยกเลิกงานที่สั่งสัตว์ตัวนี้ทำอยู่ในกรง/โรงเลี้ยง | 1 | `client/PetManager.cs` |
| `GetPetInventory` | 49823 | ขอดูของในกระเป๋าสัตว์ตัวนี้ → `PetInventory` | 1 | `client/Durango.Logic.Item/Inventory.cs` |
| `PutInItemsIntoPet` | 806 | ฝากของจากกระเป๋าเราเข้ากระเป๋าสัตว์ | 1 | `client/InventorySystem.cs` |
| `TakeOutItemsFromPet` | 807 | เอาของออกจากกระเป๋าสัตว์กลับมาที่ตัวเรา | 1 | `client/InventorySystem.cs` |
| `StartPetTask` | 65102 | สั่งสัตว์เริ่มทำงาน `TaskId` ในกรงที่ระบุ (ฝึก/ผลิตของ) — รายการงานอยู่ `pet/pet_task.json` | 1 | `client/PetManager.cs` |
| `ReinifyPet` | 74013 | "ปลดผูกมัด" — แปลงสัตว์เลี้ยงกลับเป็นบังเหียน เพื่อย้าย/ซื้อขายได้ ต้องใช้ไอเทมที่มีแท็กตาม `constants.json` → `pet.reinify_tags` | 1 | `client/PetManager.cs` |
| `GrazedPets` | 29912241 | **[เซิร์ฟ→เกม]** รายชื่อสัตว์ที่ถูกปล่อยเล็มหญ้าอยู่ — เป็นคำตอบของ `GetGrazedPets` (รับได้แล้ว) | 1 | `client/Durango.Online/Player.cs` |
| `PutItemsForDomestication` | 694352 | ใส่อาหาร/ของลงกรงระหว่างทำให้เชื่อง → ตอบ `MilestoneCandidates`/`MilestoneResult` | 1 | `client/PetManager.cs` |
| `GetPetsInfo` | 14198037 | ขอรายการสัตว์เลี้ยงทั้งหมดของเรา → `PetsInfo` (หน้าจอสัตว์เลี้ยงเปิดด้วยตัวนี้) | 1 | `client/PetManager.cs` |
| `ReleasePet` | 74012 | ปล่อยสัตว์ทิ้งถาวร (ลบออกจากบัญชี) | 1 | `client/PetManager.cs` |
| `ResurrectPet` | 239187 | ชุบชีวิตสัตว์เลี้ยงที่ตาย → ตอบ `OK` | 1 | `client/PetManager.cs` |
| `GetPreviewPet` | 181120 | ขอดูค่าสถานะตัวอย่างของสัตว์ (ชนิด+แรงก์+เลเวลที่ระบุ) ก่อนตัดสินใจ → `Pet` | 1 | `client/PetManager.cs` |
| `FinishPetTask` | 65104 | เก็บผลงานที่สัตว์ทำเสร็จแล้ว (ได้ของ/EXP) | 1 | `client/PetManager.cs` |
| `RenamePet` | 804 | ตั้งชื่อสัตว์เลี้ยง | 1 | `client/PetManager.cs` |
| `DiscoverAnimal` | 5002 | บันทึกว่าเจอสัตว์ชนิดนี้ครั้งแรก (น่าจะปลดสารานุกรม/ข้อมูลชนิด) (?) | 1 | `client/MapSystem.cs` |
| `StartDomestication` | 694357 | เริ่มขั้นตอนทำให้เชื่องในกรง โดยใส่บังเหียน `ItemId` | 1 | `client/PetManager.cs` |
| `CancelDomestication` | 694354 | ยกเลิกการทำให้เชื่องกลางคัน (ได้บังเหียนคืนไหมยังไม่ยืนยัน) (?) | 1 | `client/PetManager.cs` |
| `FinishDomestication` | 694355 | จบการทำให้เชื่อง ได้สัตว์เลี้ยงจริง → `DomesticationResult` | 1 | `client/PetManager.cs` |
| `GrazePets` | 800000 | สั่งปล่อยสัตว์ (หลายตัวพร้อมกัน) ออกเล็มหญ้าในที่ดิน — สัตว์ที่อยู่ในโรงเลี้ยงปล่อยไม่ได้ → ตอบ `PetsInfo` | 1 | `client/PetManager.cs` |
| `DisappearPet` | 198246 | **[เซิร์ฟ→เกม]** แจ้งว่าสัตว์ของผู้เล่นคนนั้นหายจากสายตาแล้ว (เก็บเข้าที่/ออกนอกระยะ) | 1 | `client/Durango.Online/Player.cs` |
| `UsePetActiveSkill` | 800200 | สั่งสัตว์ที่เรียกออกมาใช้สกิลแอคทีฟ — ข้อมูลสกิลอยู่ `pet/pet_active_skills.json` + เงื่อนไขที่ `pet_active_skill_conditions.json` | 1 | `client/PetManager.cs` |
| `ReturnPet` | 808 | เรียกสัตว์กลับ เก็บเข้าที่ (เลิกเรียกออกมา) | 1 | `client/PetManager.cs` |
| `SpawnPet` | 923570 | เรียกสัตว์ออกมาเดินตามข้างตัว | 1 | `client/PetManager.cs` |

## คราฟต์/สูตร

การคราฟต์ในเกมนี้ทำที่ "โต๊ะ" (workbench artifact): เกมส่งวัสดุ+สูตร เซิร์ฟจับเวลา แล้วคืนของที่ทำเสร็จ. งานยาวฝากไว้กับโต๊ะได้ (entrusted craft) และจ่ายข้ามเวลาได้
ข้อมูลครบมาก: `item/recipes.json` **720 สูตร** (มี `prototypes`/`slots`/`energy`/`duration`/`required_ability`/`workbench_tags`/`tool_tags`/`min_level`) และ `building/blueprints.json` **556 พิมพ์เขียว**. `GetRecipes`/`GetArtifactBlueprints` เซิร์ฟรับได้แล้ว แต่ยังตอบเป็นค่าว่าง
**หมายเหตุ:** `MakeSection` เป็นเรื่องคลังของ, `MakeClan`/`MakeParty` เป็นเรื่องสังคม — หลงมาอยู่หมวดนี้เพราะชื่อขึ้นต้นด้วย Make

| message | TypeCode | ทำอะไร | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---|---:|---|
| `MakeSection` | 3686 | *(จริง ๆ เป็นเรื่องคลัง)* สร้างหมวดใหม่ในคลังของสิ่งปลูกสร้าง (ตั้งชื่อช่องเก็บของ) | 1 | `client/InventorySystem.cs` |
| `Recipes` | 120 | **[เซิร์ฟ→เกม]** รายการสูตรที่ปลดแล้ว + สูตรที่กดถูกใจ + สูตรที่เพิ่งได้ใหม่ — เป็นคำตอบของ `GetRecipes` | 1 | `client/Durango.Online/Player.cs` |
| `ArtifactBlueprints` | 316 | **[เซิร์ฟ→เกม]** เหมือน `Recipes` แต่เป็นพิมพ์เขียวสิ่งปลูกสร้าง — คำตอบของ `GetArtifactBlueprints` | 1 | `client/Durango.Online/Player.cs` |
| `SetBlueprintLike` | 3801 | กดถูกใจ/เลิกถูกใจพิมพ์เขียว (ปักหมุดไว้บนสุด) → เซิร์ฟตอบ `ArtifactBlueprints` ชุดใหม่กลับมา | 1 | `client/RecipeSystem.cs` |
| `SkipEntrustedCraft` | 7498153 | จ่ายเพื่อข้ามเวลารอของงานคราฟต์ที่ฝากไว้กับโต๊ะ | 1 | `client/CraftSystem.cs` |
| `MakeClan` | 3651 | *(จริง ๆ เป็นเรื่องสังคม)* ก่อตั้งแคลนชื่อ `ClanName` โดยจ่ายสกุลเงิน `Currency` — ราคาอยู่ `constants.json` → `clan_creation_costs` | 1 | `client/ClanSystem.cs` |
| `CancelCrafting` | 2023 | ยกเลิกงานคราฟต์ที่ค้างอยู่บนโต๊ะ (คืนวัสดุไหมยังไม่ยืนยัน) (?) | 1 | `client/CraftSystem.cs` |
| `MakeParty` | 20003 | *(จริง ๆ เป็นเรื่องสังคม)* สร้างปาร์ตี้ใหม่ ไม่มีฟิลด์ | 1 | `client/Durango.Logic/PartySystem.cs` |
| `SetRecipeLike` | 3800 | กดถูกใจ/เลิกถูกใจสูตร → เซิร์ฟตอบ `Recipes` ชุดใหม่กลับมา | 1 | `client/RecipeSystem.cs` |

## สกิล/เลเวล

หมวดนี้จริง ๆ ปนกัน 3 ระบบ:
1. **สกิล/ค่าสถานะ** — สกิลเป็นตาราง 13 หมวด (`skill/categories.json`, `skills.json`) ปลดด้วย "แต้มสกิล" และหมวดต้อง "วิจัย" (ใช้เวลา + จ่ายข้ามได้). ค่าสถานะทั้งตัวละครส่งเป็นก้อนเดียวชื่อ `Statistics` (พลังพื้นฐาน/derived/ค่าต้านทาน/โมดิฟายเออร์)
2. **ที่ดิน (Estate)** — จับจอง/ขยาย/ย่อ/ต่ออายุ/ตั้งสิทธิ์เข้าออก/วาร์ปกลับบ้าน. ค่าใช้จ่ายอยู่ `costs.json` → `estate` ขนาดสูงสุดผูกกับเกรดผู้บุกเบิก `pioneer.json` → `estate_size`
3. **จุดสนใจ (POI)** บนแผนที่ — เปิดหมอก/รับรางวัลสำรวจ

| message | TypeCode | ทำอะไร | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---|---:|---|
| `ToggleStatusEffect` | 2081 | ✅ เปิด/ปิดสถานะที่สลับได้เอง — ที่เห็นชัดคือ "เข้านอน/ตื่น" ของ `SleepChecker`; รายการสถานะ 372 ตัวอยู่ `survival/status_effects.json` | 2 | `client/SleepChecker.cs` |
| `GetExploredPOIs` | 902 | ✅ ขอรายการจุดสนใจที่สำรวจแล้วในภูมิภาค `RegionId` → `ExploredPOIs` | 2 | `client/MapSystem.cs` |
| `GetQuestState` | 398132 | ขอสถานะเควสของ `QuestIds` ที่ระบุ (ยังไม่รับ/กำลังทำ/เสร็จ) → `QuestState` | 2 | `client/Durango.Development/Commands.cs` |
| `EstateLicenses` | 3821 | **[เซิร์ฟ→เกม]** สรุปที่ดินทั้งหมดที่ถือ: ที่ดินในเมือง/ส่วนตัว/แคลน + ขนาดสูงสุดของแต่ละแบบ + คลังวาร์ปโฮลของแคลน — คำตอบของ `GetEstateLicenses` | 1 | `client/Durango.Online/Player.cs` |
| `VisitEstate` | 2104 | ขอวาร์ปไปเยี่ยมที่ดินของคนอื่น/ของแคลน (มีค่าเดินทาง `Cost` ถ้าเป็นวาร์ปโฮลแคลน) | 1 | `client/EstateSystem.cs` |
| `ExplorePOI` | 908 | แจ้งว่าเดินไปถึงจุดสนใจนี้แล้ว (เปิดหมอกจุดนั้น) → ตอบ `OK`; รางวัลสำรวจอยู่ `constants.json` → `poi_finding_reward` | 1 | `client/Durango.Logic.Map/POIUpdater.cs` |
| `DeclareEstate` | 2422 | จับจองที่ดินที่ช่อง `Cell` เป็นประเภท `OwnerType` (ส่วนตัว/เมือง/แคลน) → `EstateLicense` | 1 | `client/EstateSystem.cs` |
| `CancelSkillCategoryResearch` | 36431 | ยกเลิกการวิจัยหมวดสกิลที่กำลังทำอยู่ | 1 | `client/Durango.Logic/SkillSystem.cs` |
| `GetExpectedCropBooster` | 37123 | พรีวิว: ถ้าใส่ปุ๋ยชุด `ItemIds` ลงแปลงนี้ จะได้บูสต์เท่าไร (ยังไม่ใส่จริง) → `ExpectedCropBooster` | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `GetStatusEffects` | 2016 | ✅ ขอสถานะ (บัฟ/ดีบัฟ) ทั้งหมดที่ติดตัวอยู่ตอนนี้ → `StatusEffects` | 1 | `client/StatusEffectSystem.cs` |
| `Statistics` | 2040 | **[เซิร์ฟ→เกม]** ค่าพลังทั้งชุด: พลังพื้นฐาน, derived, เลเวล, EXP, เลเวล/EXP ค่าต้านทาน, โมดิฟายเออร์, พลังตัวแทน — คำตอบของ `GetStatistics` | 1 | `client/Durango.Online/Player.cs` |
| `SkipSkillCategoryResearch` | 3643 | จ่ายเพื่อข้ามเวลารอวิจัยหมวดสกิล | 1 | `client/Durango.Logic/SkillSystem.cs` |
| `ReturnToEstate` | 10190234 | วาร์ปกลับที่ดินของตัวเอง/ของแคลน ตาม `OwnerType` | 1 | `client/EstateSystem.cs` |
| `SetEstateLicense` | 2420 | ตั้งสิทธิ์ของที่ดิน (ใครเข้าได้ ใครหยิบของได้ ใครสร้างได้) ผ่าน `AccessRights` → ตอบ `OK` | 1 | `client/EstateSystem.cs` |
| `GetStatistics` | 2039 | ✅ ขอค่าสถานะตัวละครทั้งชุด → `Statistics` | 1 | `client/StatisticsSystem.cs` |
| `RedrawActiveSkill` | 800102 | *(เป็นเรื่องสัตว์)* สุ่มสกิลแอคทีฟของสัตว์ใหม่ ทับของเดิม (จ่ายบัตรได้ด้วย `WithVoucher`) → `DrawSkillResult` | 1 | `client/PetManager.cs` |
| `RemoveEstate` | 9518234 | ยกเลิก/คืนที่ดินแปลงนี้ | 1 | `client/EstateSystem.cs` |
| `ExpandEstate` | 2421 | ขยายที่ดินเพิ่มอีกหนึ่งช่องที่ `Cell` → `EstateLicense` ใหม่ | 1 | `client/EstateSystem.cs` |
| `GetEstateLicenseById` | 879534 | ขอข้อมูลที่ดินตาม id (ใช้ตอนรีเฟรชข้อมูลที่ค้าง) | 1 | `client/EstateSystem.cs` |
| `GetResistanceExpCaps` | 349378781 | ✅ ขอเพดาน EXP ของค่าต้านทานแต่ละแบบ (ร้อน/หนาว/พิษ ฯลฯ) ที่เลเวลปัจจุบัน → `ResistanceExpCaps` | 1 | `client/StatisticsSystem.cs` |
| `RequestClanStatusEffects` | 3704 | *(เป็นเรื่องแคลน)* ขอบัฟที่ได้จากแคลน (มาจากงานวิจัยแคลน) → `ClanStatusEffectsUpdated` | 1 | `client/ClanSystem.cs` |
| `DrawActiveSkill` | 800101 | *(เป็นเรื่องสัตว์)* สุ่มสกิลแอคทีฟให้สัตว์ครั้งแรก → `DrawSkillResult` | 1 | `client/PetManager.cs` |
| `ResearchSkillCategory` | 2446 | เริ่มวิจัยหมวดสกิล `Category` — ถ้าใส่ `SkipCategory` มาด้วยคือขอข้ามหมวดที่วิจัยค้างอยู่ไปพร้อมกัน | 1 | `client/Durango.Logic/SkillSystem.cs` |
| `GetSkills` | 2047 | ✅ ขอต้นไม้สกิลทั้งหมด + แต้มคงเหลือ + หมวดที่วิจัยแล้ว → `Skills` | 1 | `client/Durango.Logic/SkillSystem.cs` |
| `ExtendEstateActivation` | 3822 | จ่าย `Cost` ต่ออายุที่ดินไม่ให้หมดอายุ → `EstateLicense` | 1 | `client/EstateSystem.cs` |
| `Skills` | 123 | **[เซิร์ฟ→เกม]** รายการสกิลที่เรียนแล้ว, แต้มสกิล, สถานะแต่ละหมวด, จำนวนที่ยังไม่ฝึก, สกิลแนะนำ — คำตอบของ `GetSkills` | 1 | `client/Durango.Online/Player.cs` |
| `ShrinkEstate` | 2426 | ย่อที่ดินคืนทีละช่องที่ `Cell` → `EstateLicense` | 1 | `client/EstateSystem.cs` |

## เอาชีวิตรอด

แกนเอาชีวิตรอดคือ "ความเหนื่อยล้า (fatigue)" ที่ขยับตามสภาพแวดล้อม (ร้อน/หนาว/เปียก/สกปรก) แล้วแก้ด้วยกิจกรรม เช่น ดื่มน้ำ อาบน้ำ นอน กินอาหาร — ทุกอย่างลงเอยที่ **status effect**
ข้อมูลครบ: `survival/status_effects.json` (372 สถานะ พร้อม `effects` ที่บวก/ลบค่า `hot`/`cold`/ฯลฯ), `survival/fatigue_categories.json`, `survival/date_time.json` (พระอาทิตย์ขึ้น/ตก), `constants.json` → `fatigue_formula`/`fatigue_velocity`/`drink_water`/`wash_body`
**หมายเหตุ:** `GetClanCreationCosts` เป็นเรื่องแคลนล้วน ๆ หลงมาอยู่หมวดนี้

| message | TypeCode | ทำอะไร | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---|---:|---|
| `GetCheatFlags` | 2088 | ขอสถานะสวิตช์โกง/โหมดทดสอบของเซิร์ฟ → `CheatFlags` (`Dictionary<string,bool>`) — ใช้กับปุ่ม dev เท่านั้น | 2 | `client/Durango.UI/CommandButtonGroup.cs` |
| `Weather` | 331 | **[เซิร์ฟ→เกม]** แจ้งสภาพอากาศปัจจุบัน (`_Weather`) และความเข้ม (`WeatherRatio`) — ส่งตอนผู้เล่นเข้าโลก; อากาศที่ใช้ได้ของแต่ละภูมิภาคอยู่ `region_templates.json` → `weather` | 1 | `client/Durango.Online/World.cs` |
| `GetClanCreationCosts` | 3667 | *(เป็นเรื่องแคลน)* ขอราคาก่อตั้งแคลน → `Costs`; ค่าอยู่ `constants.json` → `clan_creation_costs` | 1 | `client/ClanSystem.cs` |
| `RemoveDeathPoint` | 2034 | ลบหมุด "จุดที่ตายไว้" ออกจากแผนที่ (เก็บของคืนแล้ว/ไม่เอาแล้ว) | 1 | `client/MapSystem.cs` |
| `GiveAttendanceReward` | 1097854 | รับรางวัลเช็คชื่อประจำวัน ช่องที่ `RewardNumber` ในหมวด `Category`; `IsRestore = true` คือย้อนเก็บวันที่พลาด — ตั้งค่าที่ `constants.json` → `attendance` | 1 | `client/Durango.Logic/EventSystem.cs` |
| `DrinkWater` | 3492 | ดื่มน้ำจากแหล่งน้ำที่ยืนอยู่ ลดความกระหาย/ความร้อน → ตอบ `Timer` (ระยะเวลาแอนิเมชัน) แล้วค่อยติดสถานะ `drink_water` | 1 | `client/InteractionSystem.cs` |
| `GiveAttendanceAppendix` | 1097855 | เลือกรับ "รางวัลพิเศษท้ายตาราง" ของการเช็คชื่อ (เลือกได้ 1 ใน N ตาม `SelectedReward`) | 1 | `client/Durango.Logic/EventSystem.cs` |

## เควส

หมวดนี้จริง ๆ คือ **สามระบบที่แยกกัน** และหมวดสแกนดูดเรื่องเพื่อน/แคลน/ช่างเข้ามาปนด้วย:
1. **Mission (ภารกิจฝ่าย/faction)** — 7 ฝ่ายใน `factions.json` มีระดับมิตรภาพ+รางวัล; ผู้เล่นไปคุยกับ NPC ผู้ส่งสาร รับ/ยกเลิก/สับเปลี่ยน (shuffle) ภารกิจ มีโควตาสับเปลี่ยนต่อวันที่เติมได้
2. **Quest (เควสหลัก/รายวัน/สะสมแต้ม)** — `quests/quests_for_client.json` **1,386 เควส** แต่ **มีแค่ข้อความแสดงผล** (`subject`/`description`/`category`/`icon`/`quest_type`) **ไม่มีเงื่อนไข/todo/รางวัล** → ตรรกะเควสต้องเขียนเองทั้งหมด
3. **Tech Support (แต่งของ/reform)** — `RequestTechSupport*`, `RequestResetReformSlot` เป็นระบบใส่คุณสมบัติเสริมลงช่อง reform ของไอเทม ข้อมูลที่ `item/tech_support.json` (22 แบบ)

| message | TypeCode | ทำอะไร | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---|---:|---|
| `GetMissions` | 3620 | ขอรายการภารกิจฝ่ายที่ถืออยู่ทั้งหมด → `MissionInfos` | 2 | `client/FactionSystem.cs` |
| `RecommendMissions` | 3621 | ขอชุดภารกิจแนะนำจาก NPC ตัวนี้ (คุยแล้วได้ลิสต์) → `MissionInfos` | 2 | `client/FactionSystem.cs` |
| `GetQuestScoreInfos` | 237920 | ขอความคืบหน้าแต้มเควสของหมวด `Category` (แต้มสะสม + ระดับรางวัลที่ปลดได้) → `QuestScoreInfos` | 2 | `client/Durango.Logic/QuestSystem.cs` |
| `RequestDumpedPersonalIsland` | 381922 | คำสั่ง dev: ดึงข้อมูลเกาะส่วนตัวของผู้เล่นคนนั้นออกมาดู → `DumpedPersonalIsland` | 2 | `client/Durango.Development/Commands.cs` |
| `GetSupportRequests` | 2347809 | ขอรายการ "คำขอความช่วยเหลือ" ของฝ่าย (ผู้เล่นช่วยกันส่งของ) → `SupportRequests` | 2 | `client/FactionSystem.cs` |
| `RequestNearestPOI` | 911 | ถามว่าจุดสนใจประเภทนี้ที่ใกล้ตำแหน่ง `Tile` ที่สุดอยู่ไหน → `NearestPOI` (ใช้ทำลูกศรนำทาง) | 2 | `client/Durango.Development/Commands.cs` |
| `CancelMission` | 3624 | ทิ้งภารกิจฝ่ายที่รับไว้ | 2 | `client/FactionSystem.cs` |
| `RequestEpicWarp` | 77777 | ขอวาร์ปเข้าฉากเนื้อเรื่องหลัก (epic) — ยิงหลังได้รางวัลเควส; ข้อมูล `quests/epics_for_client.json` | 1 | `client/Durango.Logic/QuestSystem.cs` |
| `RequestFullCountPOIsReward` | 9031 | ขอรับรางวัล "สำรวจจุดสนใจครบทั้งภูมิภาค" — ค่าที่ `constants.json` → `all_pois_finding_reward` | 1 | `client/MapSystem.cs` |
| `RequestReturnerGuideAction` | 3450984 | ผู้เล่นที่หายไปนานแล้วกลับมา กดปุ่มในไกด์ผู้กลับมา (`ReturnerGuideAction`) เช่นรับของขวัญ/วาร์ปไปจุดแนะนำ | 1 | `client/PlayGuideSystem.cs` |
| `AcceptMission` | 3623 | รับภารกิจ `MissionId` จาก NPC ที่ยืนคุยอยู่ → `OK` | 1 | `client/FactionSystem.cs` |
| `RequestResetReformSlot` | 59145 | *(ระบบแต่งของ)* ล้างช่อง reform ช่องที่ `ReformSlotIndex` ของไอเทมให้ว่าง → `OK`; ราคาที่ `costs.json` → `reset_reform_slot` | 1 | `client/TechSupportSystem.cs` |
| `RechargeMissionShuffleCount` | 3626 | เติมโควตาสับเปลี่ยนภารกิจที่ใช้หมดแล้ว (จ่ายเงิน) → `MissionInfos` | 1 | `client/FactionSystem.cs` |
| `RecommendMissionImmediately` | 3629 | จ่ายเพื่อขอภารกิจใหม่จากฝ่ายนี้ทันทีโดยไม่ต้องรอรอบ → `MissionInfos` | 1 | `client/FactionSystem.cs` |
| `QuestCategories` | 237902 | **[เซิร์ฟ→เกม]** รายการหมวดเควสทั้งหมด + เควส epic ปัจจุบัน — คำตอบของ `GetQuestCategories` | 1 | `client/Durango.Online/Player.cs` |
| `GetRecommendMissionCost` | 3628 | ถามราคาก่อนกด `RecommendMissionImmediately` → `Costs` | 1 | `client/FactionSystem.cs` |
| `InteractWithEpicNPC` | 3141593 | คุย/ส่งไอเทมให้ NPC เนื้อเรื่องหลัก (`EpicNPCType` + `ItemIds`) | 1 | `client/ClientInteractionQuest.cs` |
| `RefuseFriendRequest` | 1451216 | *(เป็นเรื่องสังคม)* ปฏิเสธคำขอเป็นเพื่อน → `Social` ชุดใหม่ | 1 | `client/SocialSystem.cs` |
| `CustomQuestEvent` | 312798 | ยิงคีย์เวิร์ดเหตุการณ์เควสแบบกำหนดเอง (จากสคริปต์ไกด์ในเกม) ให้เซิร์ฟเช็คว่าตรงเงื่อนไขเควสไหน | 1 | `client/Durango.Logic.PlayGuide/CustomCommand.cs` |
| `ShuffleMission` | 3627 | สับเปลี่ยนชุดภารกิจของฝ่ายนี้ใหม่ (ใช้โควตาสับเปลี่ยน) → `MissionInfos` | 1 | `client/FactionSystem.cs` |
| `CancelClanJoinRequest` | 1923487521 | *(เป็นเรื่องแคลน)* ยกเลิกใบสมัครเข้าแคลนที่ยื่นค้างไว้ → `OK` | 1 | `client/ClanSystem.cs` |
| `SkipTutorialMission` | 3633 | ข้ามภารกิจสอนเล่น (`constants.json` → `skip_tutorial_missions` คุมว่าข้ามอันไหนได้) | 1 | `client/FactionSystem.cs` |
| `AcceptFriendRequest` | 1451215 | *(เป็นเรื่องสังคม)* รับคำขอเป็นเพื่อน → `Social` | 1 | `client/SocialSystem.cs` |
| `SetPersonalRegionAdmission` | 20423 | ตั้งว่าใครเข้าเกาะส่วนตัวเราได้บ้าง (รายการ `LicenseCategory[]` เช่น เพื่อน/แคลน/ทุกคน) | 1 | `client/Durango.UI.Popup/PersonalRegionAdmissionPopup.cs` |
| `RequestTechSupportEstimate` | 59141 | *(ระบบแต่งของ)* ขอ "ใบเสนอราคา" สำหรับแต่งช่อง reform โดยล็อกแท็กที่อยากเก็บไว้ (`TagsToLock`) → `TechSupportEstimateResult` | 1 | `client/TechSupportSystem.cs` |
| `SendFactionSupportRequest` | 725982 | ส่งคำขอความช่วยเหลือของฝ่าย (`RequestId`) → `AcceptedSupportRewards` | 1 | `client/FactionSystem.cs` |
| `RequestQuestScoreReward` | 237925 | ขอรับรางวัลขั้นแต้ม `Score` ของหมวด `Category` → `QuestScoreInfos` ชุดใหม่ | 1 | `client/Durango.Logic/QuestSystem.cs` |
| `RequestQuestReward` | 237923 | ขอรับรางวัลของเควส `QuestId` ที่ทำเสร็จแล้ว → `QuestRewardResults` | 1 | `client/Durango.Logic/QuestSystem.cs` |
| `RequestFriend` | 1451212 | *(เป็นเรื่องสังคม)* ส่งคำขอเป็นเพื่อน → `Social` | 1 | `client/SocialSystem.cs` |
| `RequestTechSupport` | 59144 | *(ระบบแต่งของ)* ลงมือแต่งของจริง — ส่งวัสดุ `Materials` ลงช่อง reform ที่โต๊ะ → `OK`; ตารางคุณสมบัติที่ `item/tech_support.json` | 1 | `client/CraftSystem.cs` |
| `CheckSequenceMissionCleared` | 3631 | เช็คว่าภารกิจแบบต่อเนื่อง (ชุดที่ต้องทำเรียงกัน) จบชุดหรือยัง → `SequenceMissionCleared` (?) | 1 | `client/FactionSystem.cs` |
| `GetAttendanceRewards` | 1097852 | ขอตารางรางวัลเช็คชื่อของหมวด `Category` (รับไปแล้วช่องไหนบ้าง) → `AttendanceRewards` | 1 | `client/Durango.Logic/EventSystem.cs` |
| `RequestArchipelagoRegionClear` | 240002 | แจ้งว่าเคลียร์ภารกิจของภูมิภาคในหมู่เกาะนี้ครบแล้ว ขอเปิดทางไปต่อ (?) — ข้อมูล `quests/archipelago_todos_client.json` (71 ชุด) | 1 | `client/Durango.Logic/ArchipelagoMissionSystem.cs` |
| `CancelFriendRequest` | 1451220 | *(เป็นเรื่องสังคม)* ถอนคำขอเป็นเพื่อนที่เราส่งไป → `Social` | 1 | `client/SocialSystem.cs` |
| `RequestClanRewards` | 3706 | *(เป็นเรื่องแคลน)* ขอรับรางวัลระดับแคลน — ตาราง `clan.json` → `level_rewards` | 1 | `client/ClanSystem.cs` |

## ของ/กระเป๋า

สามชั้นของ "ที่เก็บของ" ในเกมนี้:
1. **กระเป๋าตัวละคร** — `GetInventory` (ช่อง `Target` ว่าง = กระเป๋าเรา, ใส่ `PropKey` = ของสิ่งปลูกสร้าง) และเซิร์ฟผลัก `InventoryUpdated` ทุกครั้งที่ของเปลี่ยน
2. **คลัง (warehouse)** ในสิ่งปลูกสร้าง — แบ่งเป็น "section" ที่ผู้เล่นตั้งชื่อ/เรียงเองได้
3. **วาร์ปโฮลขนส่ง (cargo warphole)** — ส่งของข้ามเกาะไปที่ดินตัวเอง/แคลน มีภาษีที่เจ้าของวาร์ปโฮลตั้งได้

ข้อมูลรองรับเต็ม: `item/prototype_data.json` **2,407 ไอเทม**, `tags.json`, `item/bonus_prototypes.json`; ค่าคลัง/ขนส่งอยู่ `constants.json` → `warehouse`, `cargo_warp`, `item`

| message | TypeCode | ทำอะไร | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---|---:|---|
| `InventoryUpdated` | 113 | **[เซิร์ฟ→เกม]** ผลักการเปลี่ยนแปลงของในกระเป๋า: ของที่เพิ่ม/id ที่ถูกลบ/ลำดับใหม่/รายการของที่ล็อกไว้ — เป็น message ที่เกมพึ่งพามากที่สุด (เรียก 11 จุด) | 11 | `client/Durango.Online/Player.cs` |
| `InventoryOrder` | 15 | บันทึกลำดับการเรียงของในกระเป๋า (ถ้าใส่ `TargetArtifact` = เรียงของในสิ่งปลูกสร้างนั้น) | 2 | `client/InventorySystem.cs` |
| `SendCargo` | 3814 | ส่งของผ่านวาร์ปโฮลขนส่งไปยังปลายทาง `CargoDestination` (ที่ดิน/วาร์ปโฮลแคลน) → ตอบ `CargoReceiver` | 1 | `client/CargoWarpholeSystem.cs` |
| `SetSectionItemOrder` | 3689 | บันทึกลำดับของภายในหมวดหนึ่งของคลัง | 1 | `client/InventorySystem.cs` |
| `AddItemsToWarehouse` | 3690 | ฝากของเข้าคลัง ลงหมวด `SectionName` | 1 | `client/InventorySystem.cs` |
| `GetInventory` | 2010 | ขอรายการของ — `Target` ว่าง = กระเป๋าเรา, ใส่ `PropKey` = ของในสิ่งปลูกสร้างนั้น → `Inventory`/`InventoryUpdated` | 1 | `client/Durango.Logic.Item/Inventory.cs` |
| `PutInItem` | 2434 | ใส่ของลงในสิ่งปลูกสร้างที่ยืนอยู่ (หีบ/เตา/โต๊ะ) | 1 | `client/InventorySystem.cs` |
| `GetReceivedItems` | 3809 | ดูของที่ถูกส่งมาถึงวาร์ปโฮลขนส่งตัวนี้แล้วรอรับ → `ReceivedItems` | 1 | `client/CargoWarpholeSystem.cs` |
| `TakeOutReinFromCage` | 694359 | *(เป็นเรื่องสัตว์)* เอาบังเหียนออกจากกรง → ตอบ `MilestoneCandidates` | 1 | `client/PetManager.cs` |
| `UseItemsForPioneerPoint` | 812234572 | ทิ้งไอเทมลงสิ่งปลูกสร้างเพื่อแลกเป็น "แต้มผู้บุกเบิก" (เพิ่มเกรดที่ดิน) — ตาราง `pioneer.json` → `grade_point` | 1 | `client/EstateSystem.cs` |
| `CargoWarpholeTaxToClanFund` | 3815 | โอนภาษีที่วาร์ปโฮลขนส่งเก็บได้เข้ากองทุนแคลน → `ClanCargoWarphole` | 1 | `client/EstateSystem.cs` |
| `GetSectionItems` | 3692 | ขอดูของในหมวด `SectionName` ของคลังนี้ → `SectionItems` | 1 | `client/InventorySystem.cs` |
| `OccupyCargoWarphole` | 3819 | จับจองวาร์ปโฮลขนส่งกลางให้เป็นของเรา/ของแคลน | 1 | `client/Durango.UI/CargoWarpholeGroup.cs` |
| `RepairItem` | 3717 | ซ่อมไอเทมด้วยชุดซ่อม `KitItemIds` → ตอบ `Timer` (ระยะเวลา) หรือ `EnergyWarning` ถ้าพลังงานไม่พอ | 1 | `client/RepairSystem.cs` |
| `LockOrUnlockItems` | 3497 | ล็อก/ปลดล็อกไอเทม กันทิ้ง/กันขายพลาด | 1 | `client/InventorySystem.cs` |
| `UseItem` | 17 | ใช้ไอเทม 1 ชิ้น; `Accept = true` คือผู้เล่นยืนยันแล้วหลังเห็นป๊อปอัพเตือน (เช่นอาหารที่จะทับสถานะเดิม) → `Timer` แล้ว `ItemUsed` | 1 | `client/InventorySystem.cs` |
| `ChangeEquipSlotType` | 81534 | สลับชุดสวมใส่ (preset) เป็นชุดที่ `SlotType` → `OK` | 1 | `client/EquipSystem.cs` |
| `PopItemsFromWarehouse` | 3691 | เอาของออกจากคลัง หมวด `SectionName` | 1 | `client/InventorySystem.cs` |
| `ActivateCargoReceiver` | 3811 | เปิดใช้งานตัวรับของปลายทางของวาร์ปโฮลขนส่ง | 1 | `client/CargoWarpholeSystem.cs` |
| `SetCargoWarpholeTaxRate` | 3816 | ตั้งอัตราภาษี (`Rate`) ที่เก็บจากคนที่มาใช้วาร์ปโฮลขนส่งของเรา | 1 | `client/EstateSystem.cs` |
| `TakeOutFromCage` | 810 | *(เป็นเรื่องสัตว์)* เอาสัตว์ออกจากกรง | 1 | `client/PetManager.cs` |
| `GetCargoReceivers` | 3812 | ขอรายชื่อปลายทางที่ส่งของไปได้ (ที่ดินเรา/วาร์ปโฮลแคลน) + ค่าส่งต่อขนาด → `CargoReceivers` | 1 | `client/CargoWarpholeSystem.cs` |
| `DeliverItems` | 3614 | ส่งมอบไอเทมให้ฝ่าย `FactionType` (ภารกิจส่งของ) — เงื่อนไขดูได้จาก `GetFactionDeliveryCondition` | 1 | `client/FactionSystem.cs` |
| `Dye` | 3666 | ย้อมสีของ: ใส่สีย้อม `Materials` + เครื่องมือ `ToolItemId` ลงช่องสี `Channel` ที่โต๊ะ `Workbench` — ตารางสีอยู่ `colortable.json` | 1 | `client/CraftSystem.cs` |
| `MoveItemsInWarehouse` | 3685 | ย้ายของระหว่างหมวดในคลัง (`SourceSectionName` → `TargetSectionName`) | 1 | `client/InventorySystem.cs` |
| `CheckUnstableItem` | 1590123 | ก่อนออกจากเกาะไม่เสถียร: ถามเซิร์ฟว่ายังมี "ไอเทมไม่เสถียร" ค้างในกระเป๋า/กระเป๋าสัตว์กี่ชิ้น → `ResultCheckUnstableItem`; ถ้ามีเกมจะเตือนว่าของจะหายถ้าไม่ส่งผ่านวาร์ปโฮลก่อน | 1 | `client/MapSystem.cs` |

## สร้าง/ที่ดิน

การสร้างสิ่งปลูกสร้าง (artifact): วางพิมพ์เขียว → ใส่วัสดุ → รอเวลา postprocess (เพื่อนช่วยเร่งได้) → เสร็จ. สิ่งปลูกสร้างย้ายได้โดย "แคปซูล" กลับเป็นไอเทม
ข้อมูลครบ: `building/blueprints.json` (556 แบบ พร้อม `slots`/`energy`/`postprocess_time`/`postprocess_helper_max`), `building/artifact_effects.json`, `artifact_set_effects.json`, `blueprint_remodelings.json`, `entity_types/artifact.json`

| message | TypeCode | ทำอะไร | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---|---:|---|
| `PlaceCapsulatedArtifact` | 4021 | วางสิ่งปลูกสร้างที่เก็บเป็นแคปซูลไว้ลงที่ `Tile` ชั้น `Floor` หันทาง `Rotation` → `ArtifactMaterials`/`ArtifactPlaced` | 1 | `client/BuildSystem.cs` |
| `SetArtifactAccess` | 987123450 | ตั้งสิทธิ์ใช้งานสิ่งปลูกสร้างตัวนี้ (ใครเปิด/หยิบของได้) ผ่าน `ArtifactAccess` | 1 | `client/EstateSystem.cs` |
| `CompleteArtifact` | 2094 | กด "สร้างเสร็จ" เมื่อวัสดุครบ (ปิดงานก่อสร้าง) → `ArtifactCompleted`/`ArtifactBuilt` | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `CapsulateArtifact` | 4020 | เก็บสิ่งปลูกสร้างกลับเป็นแคปซูล (ไอเทม) เพื่อย้ายที่ → `ArtifactCapsulated`; ราคาถามก่อนด้วย `GetCapsulatingCost` | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `RepairArtifact` | 2055 | ซ่อมสิ่งปลูกสร้างด้วยชุดซ่อม → `Timer` หรือ `EnergyWarning` | 1 | `client/RepairSystem.cs` |

## สังคม

หมวดสังคมแยกเป็น 4 ระบบย่อยที่ไม่เกี่ยวกันเลย:
- **เพื่อน/บล็อก/ตามติด** (`SocialSystem`) — สถานะทั้งหมดตอบกลับเป็นก้อนเดียวชื่อ `Social`
- **แคลน** (`ClanSystem`) — สมัคร/อนุมัติ/ยศ/กองทุน/ตราสัญลักษณ์/พันธมิตร/งานวิจัยแคลน. ข้อมูล `clan.json`, `clan_research.json` (15 สาย)
- **ปาร์ตี้** (`PartySystem`) — ชวน/เข้า/ออก/เตะ/ย้ายหัวหน้า
- **จดหมาย** (`MailSystem`) — มี 2 ชุดคำสั่งซ้อนกัน: ชุด `...Mails` (จดหมายระบบ) กับ `...UserMails` (จดหมายจากผู้เล่น) ต้องทำแยกกัน

⚠️ ข้อสังเกตสำคัญ: **แชตไม่ได้วิ่งบนเซิร์ฟเกม** — `Tune`/`Conversations`/`SayInConversation` วิ่งบน connection แยกชื่อ `Connections.Radiotower` ถ้าจะทำแชตต้องมีอีก endpoint

| message | TypeCode | ทำอะไร | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---|---:|---|
| `AcceptUserMails` | 9786523 | รับของแนบจากจดหมายผู้เล่นตาม `MailIds` | 2 | `client/MailSystem.cs` |
| `RejectPartyInvitation` | 20007 | ปฏิเสธคำเชิญเข้าปาร์ตี้ (และใช้ยกเลิกคำเชิญที่เราส่งไปด้วย) | 2 | `client/Durango.Logic/PartySystem.cs` |
| `GetFactions` | 3600 | ขอข้อมูลฝ่ายทั้งหมด + ระดับมิตรภาพของเรา → `Factions`; ข้อมูล `factions.json` (7 ฝ่าย) | 1 | `client/FactionSystem.cs` |
| `SetClanEmblem` | 3695 | ตั้งตราสัญลักษณ์แคลน (ส่งมาเป็น `byte[]` ดิบ) | 1 | `client/ClanSystem.cs` |
| `DeleteUserMails` | 9786524 | ลบจดหมายผู้เล่น | 1 | `client/MailSystem.cs` |
| `RenameClan` | 36510 | เปลี่ยนชื่อแคลน → `OK` | 1 | `client/ClanSystem.cs` |
| `MarkUserMailsAsRead` | 98712436 | ทำเครื่องหมายว่าอ่านจดหมายผู้เล่นแล้ว | 1 | `client/MailSystem.cs` |
| `GetClanFund` | 3678 | ขอยอดกองทุนแคลน → `Costs` | 1 | `client/ClanSystem.cs` |
| `GetClanNotificationEnabled` | 4027 | ขอค่าตั้งว่าเปิดแจ้งเตือนช่องแชตแคลนช่องไหนบ้าง → `ToggleClanNotification` | 1 | `client/SocialSystem.cs` |
| `GetFactionDeliveryCondition` | 3612 | ถามว่าฝ่ายนี้รับส่งของแบบไหน/ต้องการอะไร (`PropKey Target`) → `FactionDeliveryCondition` | 1 | `client/FactionSystem.cs` |
| `ResubscribeClanChannel` | 24 | สมัครฟังช่องแชตแคลนใหม่ (หลังเปลี่ยนแคลน) | 1 | `client/SocialSystem.cs` |
| `LeaveParty` | 20008 | ออกจากปาร์ตี้ | 1 | `client/Durango.Logic/PartySystem.cs` |
| `DropClanApplier` | 3659 | ปัดตกใบสมัครเข้าแคลนของคนนั้น | 1 | `client/ClanSystem.cs` |
| `GetRoutesOfParty` | 20300 | ขอเส้นทางเดินเรือที่ปาร์ตี้ใช้ร่วมกันจากท่านี้ → `Routes` | 1 | `client/ExploreSystem.cs` |
| `GetMyFriendType` | 78209743 | ถามว่าเรากับคนนี้เป็นอะไรกัน (เพื่อน/บล็อก/ไม่รู้จัก) → `FriendType` | 1 | `client/SocialSystem.cs` |
| `JoinIntoParty` | 20006 | ตอบรับคำเชิญ เข้าปาร์ตี้ | 1 | `client/Durango.Logic/PartySystem.cs` |
| `ActivateFaction` | 3610 | เปิดใช้งานฝ่าย (น่าจะยิงตอนรู้จักฝ่ายนั้นครั้งแรก) (?) | 1 | `client/PlayGuideSystem.cs` |
| `StartClanResearch` | 3702 | เริ่มงานวิจัยแคลนที่อาคารวิจัย — ข้อมูล `clan_research.json` | 1 | `client/Durango.Logic/ResearchSystem.cs` |
| `SendMail` | 2077 | ส่งจดหมายหาผู้เล่น พร้อมแนบของ `ItemIds` — ขีดจำกัดที่ `constants.json` → `letter_limits`, `mail` | 1 | `client/MailSystem.cs` |
| `ElectPartyLeader` | 20010 | ย้ายตำแหน่งหัวหน้าปาร์ตี้ให้สมาชิกคนนั้น | 1 | `client/Durango.Logic/PartySystem.cs` |
| `RemoveFriend` | 1451217 | ลบเพื่อน → `Social` | 1 | `client/SocialSystem.cs` |
| `InviteIntoParty` | 20004 | ชวนคนเข้าปาร์ตี้ | 1 | `client/Durango.Logic/PartySystem.cs` |
| `GetLatestChatLog` | 25 | ขอประวัติแชตย้อนหลังของช่อง `ChannelType` (แบ่งหน้าด้วย `Offset`/`Limit`) → `ChatLogs` | 1 | `client/SocialSystem.cs` |
| `GetSocial` | 2402 | ขอข้อมูลสังคมทั้งก้อน (เพื่อน/คำขอ/บล็อก/ตัวเลือก) → `Social` | 1 | `client/SocialSystem.cs` |
| `MarkMailsAsRead` | 98712435 | ทำเครื่องหมายว่าอ่านจดหมายระบบแล้ว | 1 | `client/MailSystem.cs` |
| `SetFriendType` | 908134 | เปลี่ยนประเภทความสัมพันธ์กับคนนั้น (เพื่อน/คนสนิท ฯลฯ) → `Social` | 1 | `client/SocialSystem.cs` |
| `KickClanMember` | 3661 | เตะสมาชิกออกจากแคลน → `OK` | 1 | `client/ClanSystem.cs` |
| `SetClanMemberRole` | 3662 | เปลี่ยนยศสมาชิกแคลน (`RoleId`) → `OK` | 1 | `client/ClanSystem.cs` |
| `ReportFactionProp` | 3611 | รายงานว่าพบวัตถุ/จุดของฝ่าย (`EntityType` + ตำแหน่ง) — ใช้กับภารกิจ "ไปหาสิ่งนี้" (?) | 1 | `client/Durango.UI/MissionGroup.cs` |
| `KickPartyMember` | 20009 | เตะสมาชิกออกจากปาร์ตี้ | 1 | `client/Durango.Logic/PartySystem.cs` |
| `DeleteMails` | 2076 | ลบจดหมายระบบ | 1 | `client/MailSystem.cs` |
| `JoinClan` | 3655 | ยื่นสมัคร/เข้าแคลน `ClanId` → `OK` | 1 | `client/ClanSystem.cs` |
| `ToggleClanNotification` | 4025 | เปิด/ปิดแจ้งเตือนช่องแชตแคลนเป็นราย `ChannelType` | 1 | `client/SocialSystem.cs` |
| `SetClanInfo` | 3699 | ตั้งประกาศ (`Notice`) และคำแนะนำแคลน (`Intro`) | 1 | `client/ClanSystem.cs` |
| `AcceptMails` | 2075 | รับของแนบจากจดหมายระบบ | 1 | `client/MailSystem.cs` |
| `ApproveClanApplier` | 3657 | อนุมัติใบสมัครเข้าแคลน → `OK` | 1 | `client/ClanSystem.cs` |
| `DonateToClanFund` | 3679 | บริจาคเงินเข้ากองทุนแคลน (`Dictionary<Currency,long>`) → `Costs` ยอดใหม่ | 1 | `client/Durango.UI/ClanInfoPage.cs` |
| `GetAvailableClanResearch` | 5987333 | ขอรายการงานวิจัยแคลนที่ทำได้ที่อาคารนี้ → `AvailableClanResearch` | 1 | `client/Durango.Logic/ResearchSystem.cs` |
| `InviteToClan` | 3660 | ชวนผู้เล่นเข้าแคลน → `OK` | 1 | `client/ClanSystem.cs` |
| `GetClanResearch` | 5987341 | ขอสถานะงานวิจัยแคลนที่ทำไปแล้ว → `ClanResearchList` | 1 | `client/Durango.Logic/ResearchSystem.cs` |
| `SetSocialOptions` | 24002 | ตั้งค่าความเป็นส่วนตัว (ให้ใครชวน/ตามติด/เห็นตำแหน่งได้บ้าง) | 1 | `client/SocialSystem.cs` |
| `LeaveClan` | 3652 | ออกจากแคลน → `OK` | 1 | `client/ClanSystem.cs` |
| `GetParty` | 20001 | ขอข้อมูลปาร์ตี้ปัจจุบัน → `Party`/`PartyInfo` | 1 | `client/Durango.Logic/PartySystem.cs` |

## ตลาด/เงิน

สองระบบแยกกัน: **ร้านค้าเงินจริง (commodity/purchase)** กับ **ตลาดผู้เล่น (market)**. ตลาดผู้เล่นเซิร์ฟมีโครงแล้ว (`server/Core/MarketManager.cs` + `SearchProducts`/`BuyProduct` รับได้แล้ว) ส่วนร้านเงินจริงมีข้อมูลที่ `purchaser/commodities.json`, `special_deals.json`, `vouchers.json`
ระบบนี้ควรทำท้ายสุด — private server ไม่มีการชำระเงินจริง ทำเป็น "แจกฟรี/ตัดจากสกุลเงินในเกม" ก็พอ

| message | TypeCode | ทำอะไร | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---|---:|---|
| `AcceptPurchase` | 5247809 | ยืนยันรับของจากรายการที่ซื้อไว้ (`PurchaseId` + `SubId` สำหรับของแบบทยอยรับ) | 2 | `client/ShopSystem.cs` |
| `PurchaseCommodity` | 856710 | ซื้อสินค้าร้านค้า `CommodityId` → `Purchased` | 1 | `client/ShopSystem.cs` |
| `PurchaseCommodityWithVoucher` | 841253 | ซื้อสินค้าเดียวกันแต่จ่ายด้วยบัตรแทนเงินจริง → `Purchased` | 1 | `client/ShopSystem.cs` |
| `GetUserFirstPurchaseHistory` | 856720 | ขอประวัติ "ซื้อครั้งแรก" (ใช้เช็คโบนัสซื้อครั้งแรก) → `UserFirstPurchaseHistory` | 1 | `client/ShopSystem.cs` |
| `Products` | 5100 | **[เซิร์ฟ→เกม]** รายการประกาศขายในตลาดผู้เล่น — เป็นคำตอบของ `SearchProducts`/`GetFavoriteProducts` (รับได้แล้ว) | 1 | `client/Durango.Online/Player.cs` |
| `GetAcceptableSubPurchases` | 259674 | ขอรายการของที่ซื้อแล้วแต่ยังไม่ได้กดรับ → `AcceptableSubPurchases` | 1 | `client/ShopSystem.cs` |
| `GetPurchases` | 510397 | ขอรายการคำสั่งซื้อทั้งหมดของบัญชี → `Purchases` | 1 | `client/ShopSystem.cs` |
| `MarketCollectAllPayments` | 5102 | กดรับเงินค้างรับจากตลาดผู้เล่นทั้งหมดในครั้งเดียว | 1 | `client/Durango.UI/MarketHistoryWidget.cs` |

## แผนที่/เดินทาง

โลกของเกมซ้อนกัน 3 ชั้น: **หมู่เกาะ (archipelago)** → **ภูมิภาค (region)** → **ช่อง (tile/chunk)**. การเดินทางมี 2 แบบคนละกลไก:
- **เดินเรือ (travel)** — ไปที่ท่าเรือ เลือกเส้นทาง แล้วออกเรือไปภูมิภาคอื่น (`TravelByRegion`, `SailingBack`, `GetRoutes`) ✅ เซิร์ฟรับชุดนี้ได้แล้ว
- **วาร์ป (warp)** — วาร์ปโฮลลัดไปที่ดินตัวเอง/เมือง/เกาะส่วนตัว มีค่าใช้จ่าย (`Warp*`, `GetWarpCosts`)

ข้อมูลครบ: `region_templates.json` **267 แม่แบบภูมิภาค** (ระดับ, ชีวนิเวศ, อากาศ, ฝูงสัตว์, อายุ) + `archipelago_templates.json` **86 หมู่เกาะ**. ค่าวาร์ปที่ `constants.json` → `warp`
**หมายเหตุ:** `RemoveSection` (ลบหมวดคลัง) หลงมาอยู่หมวดนี้เพราะคำว่า Section

| message | TypeCode | ทำอะไร | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---|---:|---|
| `GetWarpCosts` | 2106 | ขอตารางค่าวาร์ปทุกปลายทาง (โชว์ก่อนกดวาร์ป) → `WarpCosts` | 2 | `client/Durango.UI/InteractionGroup.cs` |
| `TravelByRegion` | 2029 | ✅ ออกเรือจากท่านี้ไปภูมิภาค `RegionId` (`PartierId` = ตามเพื่อนไป) | 2 | `client/ExploreSystem.cs` |
| `Warp` | 2108 | วาร์ปไปช่อง `Tile` ภายในภูมิภาคเดิม | 1 | `client/MapSystem.cs` |
| `GetWarpAcceleratorCost` | 21112519 | ถามราคาการเข้าร่วม "เครื่องเร่งวาร์ป" (กิจกรรมลงขันเปิดทางวาร์ป) → ค่าที่ `constants.json` → `warp_accelerator` | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `GetDefoggedChunks` | 204 | ขอรายการ chunk ที่เปิดหมอกแล้วของภูมิภาคปัจจุบัน → `DefoggedChunks` | 1 | `client/Durango.UI/MapContext.cs` |
| `WarpToPort` | 9081241 | วาร์ปกลับไปที่ท่าเรือของภูมิภาคนี้ | 1 | `client/MapSystem.cs` |
| `RecommendStableRegions` | 5792841 | ขอรายชื่อภูมิภาค "เสถียร" (ภูมิภาคที่ไม่หมดอายุ) ที่แนะนำให้ไป → `RecommendedStableRegions` (?) | 1 | `client/MapSystem.cs` |
| `GetIslandTravelOptions` | 2130 | ขอตัวเลือกการเดินทางระหว่างเกาะที่เปิดให้เลือกตอนนี้ → `IslandTravelOptions` (?) | 1 | `client/MapSystem.cs` |
| `AddFavoriteRegionOwners` | 20011 | *(เป็นเรื่องสังคม)* เพิ่มเจ้าของภูมิภาคเข้ารายการโปรด (ไว้กลับไปเที่ยวง่าย ๆ) → `Social` | 1 | `client/SocialSystem.cs` |
| `WarpToPersonalRegion` | 3023 | วาร์ปไปเกาะส่วนตัวผ่านวาร์ปโฮลที่ยืนอยู่ | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `WarpToNextArchipelagoRegion` | 2035 | วาร์ปไปภูมิภาคถัดไปในหมู่เกาะ (เดินหน้าตามสายภารกิจหมู่เกาะ) | 1 | `client/ExploreSystem.cs` |
| `GetPersonalRegionInfo` | 20420 | ✅ ขอข้อมูลเกาะส่วนตัวของเรา (id, สิทธิ์เข้า, อายุ) → `PersonalRegionInfo` | 1 | `client/EstateSystem.cs` |
| `ActivePersonalRegionWarphole` | 3022 | เปิดใช้งานวาร์ปโฮลไปเกาะส่วนตัว | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `TravelToStableRegion` | 20321235 | เดินทางไปภูมิภาคเสถียรที่เลือกจากรายการแนะนำ | 1 | `client/MapSystem.cs` |
| `IsWarpholeAvailable` | 3021 | เช็คก่อนว่าวาร์ปโฮลตัวนี้ใช้ได้ไหม → `OK` ถ้าได้ | 1 | `client/Durango.UI/WorldMapGroup.cs` |
| `RecommendRegion` | 3001 | ขอภูมิภาคใหม่ที่เข้ากับ `TemplateId`/`Role` (สุ่มโลกให้ผู้เล่นไปตั้งรกราก) → `Region` | 1 | `client/ExploreSystem.cs` |
| `GetRegion` | 2120 | ✅ ขอข้อมูลภูมิภาคตาม id → `Region` (หรือ `Error` ถ้าไม่มี) | 1 | `client/MapSystem.cs` |
| `RemoveSection` | 3687 | *(จริง ๆ เป็นเรื่องคลัง)* ลบหมวดหนึ่งออกจากคลัง | 1 | `client/InventorySystem.cs` |
| `RecommendPersonalRegion` | 3002 | ขอสร้าง/สุ่มเกาะส่วนตัวจากแม่แบบ `TemplateId` | 1 | `client/Durango.UI/EstateGroup.cs` |
| `OpenMap` | 915 | ใช้บัตร (`VoucherId`) ซื้อ/เปิดแผนที่ภูมิภาคทั้งผืน (เปิดหมอกทีเดียว) → `ExploredPOIs`; ราคาที่ `costs.json` → `open_map` | 1 | `client/MapSystem.cs` |
| `GetWarpBackCost` | 2109 | ถามค่าวาร์ปกลับที่เดิม → `WarpCosts` | 1 | `client/MapSystem.cs` |
| `GetRegionMapInfo` | 205 | ขอข้อมูลแผนที่ของภูมิภาค (ภูมิประเทศ/หมุด) สำหรับหน้าแผนที่แชร์ → `RegionMapInfo` | 1 | `client/Durango.UI/SharedMapContext.cs` |
| `RemoveFavoriteRegionOwners` | 20012 | *(เป็นเรื่องสังคม)* เอาเจ้าของภูมิภาคออกจากรายการโปรด → `Social` | 1 | `client/SocialSystem.cs` |
| `WarpBack` | 2110 | วาร์ปกลับจุดก่อนหน้า — น่าจะย้อนรอยการวาร์ปครั้งล่าสุด (?) | 1 | `client/MapSystem.cs` |
| `TravelByRegionInArchipelago` | 2054 | ✅ เดินเรือไปภูมิภาคอื่น *ภายในหมู่เกาะเดียวกัน* | 1 | `client/ExploreSystem.cs` |
| `GetWarpCostToNextRegion` | 12033 | ถามค่าวาร์ปไปภูมิภาคถัดไปของสายภารกิจหมู่เกาะ → `WarpCosts` | 1 | `client/Durango.Logic/ArchipelagoMissionSystem.cs` |
| `TravelToRandomPersonalRegion` | 20314 | สุ่มไปเยี่ยมเกาะส่วนตัวของผู้เล่นคนอื่น | 1 | `client/ExploreSystem.cs` |
| `WarpToUrbanRegion` | 3024 | วาร์ปไปเมือง (urban region) ผ่านวาร์ปโฮลที่ยืนอยู่ | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |

## อื่น ๆ

ถังรวมของทุกอย่างที่สคริปต์แยกหมวดไม่ออก — จริง ๆ ในนี้มีระบบใหญ่ ๆ ซ่อนอยู่หลายตัว:

- **โครงสร้างโปรโตคอลพื้นฐาน** — `OK` / `Error` / `Info` / `Abort` / `Timer` / `Keepalive` / `Depart` — ต้องมีก่อนอย่างอื่นทั้งหมด
- **เก็บของ/หาของ (gathering)** — `Collect`, `GetCollectible`, `Collected`, `GiveUpDistribution`
- **ดนตรี/คอนเสิร์ต** — `MusicManager` ทั้งชุด (แต่งเพลง แชร์เพลง ตั้งวงเล่นคอนเสิร์ต) เป็นระบบเต็มที่แยกออกมาได้เลย
- **ซีซัน 2 / Warp Rush / PvP island** — `S02*` ทั้งหมด เป็นโหมดแยกที่มีคิวเข้าเล่น
- **สิ่งปลูกสร้างเชิงโต้ตอบ** — `FireBurnable`/`ExtinguishBurnable`/`Sprinkle`/`GrowRapidly`/`UprootPlant`/`TakeEffect`/`InvestToCrack`
- **ไกด์/ที่ปรึกษา (advisor)** — `GetTargetTitle`/`ReceiveAdvisorReward`/`CancelTargetTitle` + `advices.json` 58 หลักสูตร, `titles.json` 95 ฉายา
- **พาหนะ** — `Mount`/`MountVehicle`/`MountAirBalloon`/`FireProjectileFromVehicle` (เครื่องยิงบนพาหนะ)

| message | TypeCode | ทำอะไร | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---|---:|---|
| `OK` | 1231 | **[เซิร์ฟ→เกม]** ตอบ "สำเร็จ" ให้คำสั่งที่ไม่มีข้อมูลกลับ — ใช้แทบทุกที่ (16 จุด) ต้องมีตั้งแต่แรก | 16 | `client/Durango.Online/GameServer.cs` |
| `Timer` | 1134 | **[เซิร์ฟ→เกม]** บอกว่า "งานนี้ใช้เวลา N วินาที" เพื่อให้เกมเล่นแอนิเมชัน/หลอดเวลาให้ตรง | 5 | `client/Durango.Online/Player.cs` |
| `Abort` | 1024 | **[เซิร์ฟ→เกม]** ยกเลิกงานที่ค้างอยู่พร้อมข้อความ (เช่นโดนขัดจังหวะกลางคัน) | 4 | `client/Durango.Online/Player.cs` |
| `TutorialEvent` | 701 | แจ้งเซิร์ฟว่าเกิดเหตุการณ์สอนเล่นชื่อ `Event` (สคริปต์ไกด์ยิงมา) | 4 | `client/Durango.Logic.PlayGuide/CustomCommand.cs` |
| `MountAirBalloon` | 123987 | ขึ้นบอลลูน (ใช้ตั๋วถ้า `WithVoucher`) — ราคาที่ `costs.json` → `balloon_ticket` | 3 | `client/Durango.UI/EstateGroup.cs` |
| `S02Leave` | 222221 | ออกจากเกาะ PvP ซีซัน 2 | 3 | `client/Durango.Logic/PvpIslandSystem.cs` |
| `Unmount` | 803 | ลงจากหลังสัตว์ที่ขี่อยู่ | 3 | `client/PetManager.cs` |
| `SetConcertMusic` | 63459080 | ตั้งเพลงลำดับที่ `Order` ของคอนเสิร์ตบนเวทีตัวนี้ (ส่งค่าว่างมา = ล้างช่องนั้น) | 2 | `client/MusicManager.cs` |
| `GetAdvisorTargets` | 3708 | ✅ ขอรายการ "หลักสูตร" ที่ที่ปรึกษาแนะนำให้ทำ → `AdvisorTargets`; ข้อมูล `advices.json` | 2 | `client/Durango.Logic/LearningGuideSystem.cs` |
| `GetReturnerInfo` | 3450983 | ขอสถานะผู้เล่นที่กลับมาเล่นใหม่ (สิทธิพิเศษที่ได้) → `ReturnerInfo`; ตั้งค่าที่ `constants.json` → `returner` | 2 | `client/PlayGuideSystem.cs` |
| `Rename` | 324 | ตั้ง/เปลี่ยนชื่อสิ่งปลูกสร้าง (`PrevName` + `IsFirstRename` เพื่อคิดค่าธรรมเนียมครั้งถัดไป) — ราคาที่ `costs.json` → `artifact_rename` | 2 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `RegisterConcert` | 63459082 | ลงชื่อเป็นนักดนตรีในคอนเสิร์ต โดยใช้เครื่องดนตรี `InstrumentItemId` (ส่งค่าว่าง = ถอนชื่อ) | 2 | `client/MusicManager.cs` |
| `InviteToConversation` | 2411 | ชวนคนเข้าห้องสนทนา (`ConversationId` ว่าง = เปิดห้องใหม่) — วิ่งบน connection แชตแยก | 2 | `client/SocialSystem.cs` |
| `GetPOICount` | 900 | ✅ ขอจำนวนจุดสนใจทั้งหมด/ที่เจอแล้วของภูมิภาค → `POICount` | 2 | `client/Durango.Logic.Map/POIUpdater.cs` |
| `ContactReactingProp` | 78452083 | ใช้งานวัตถุที่ตอบสนอง (เช่นช่วยผู้เล่นที่ล้มด้วย CPR, ป้อนของให้วัตถุพิเศษ) → `OK`/`ReactingPropRewarded` | 2 | `client/Durango.Logic.Interactions/ReactingPropInteractions.cs` |
| `PlaySharedMusic` | 47852451 | เล่นเพลงที่คนอื่นแชร์ไว้ (`SharedSheetId`) ด้วยเครื่องดนตรีของเรา | 1 | `client/MusicManager.cs` |
| `RepairImmediate` | 2056 | จ่าย `Cost` เพื่อซ่อมสิ่งปลูกสร้างเสร็จทันทีไม่ต้องรอ | 1 | `client/RepairSystem.cs` |
| `SearchPOIs` | 904 | "ค้นหารอบตัว" — ขอให้เซิร์ฟเผยจุดสนใจใกล้ ๆ → `SearchedPOIs`; คูลดาวน์เช็คด้วย `GetLastSearchedTime` | 1 | `client/InteractionSystem.cs` |
| `PutInCage` | 809 | เอาสัตว์เข้ากรง/โรงเลี้ยง | 1 | `client/PetManager.cs` |
| `GetTitles` | 2044 | ✅ ขอรายการฉายาที่ปลดแล้ว → `Titles`; ข้อมูล `titles.json` (95 ฉายา พร้อม `abilities`/`modifiers`) | 1 | `client/StatisticsSystem.cs` |
| `GetEncyclopedia` | 37125 | ขอสารานุกรม (เพาะปลูก/สัตว์ ฯลฯ) หมวด `EncyclopediaCategory` → `FarmingEncyclopedia`; ข้อมูล `encyclopedia/` | 1 | `client/FarmingEncyclopediaSystem.cs` |
| `PickMilestone` | 800012 | *(สัตว์)* เลือกรับ milestone ที่สุ่มได้ให้สัตว์ตัวนี้ → `MilestoneResult` | 1 | `client/PetManager.cs` |
| `SuggestAlly` | 9138747 | *(แคลน)* เสนอเป็นพันธมิตรกับแคลนอื่น → `OK` | 1 | `client/ClanSystem.cs` |
| `Sprinkle` | 37121 | สั่งสปริงเกลอร์รดน้ำแปลง → `SprinkledInfo`; ตั้งค่าที่ `constants.json` → `sprinkler` | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `StartPersonalResearch` | 5987338 | เริ่มงานวิจัยส่วนตัวที่โต๊ะวิจัย — ข้อมูล `personal_research.json` (80 หัวข้อ พร้อม `cooltime`/`currency`/`effect`) | 1 | `client/Durango.Logic/ResearchSystem.cs` |
| `GetAvailableTask` | 65106 | *(สัตว์)* ขอรายการงานที่สัตว์ตัวนี้ทำได้ในกรงนี้ (ตามเลเวล/ชนิด) → `AvailableTask`; ข้อมูล `pet/pet_task.json` | 1 | `client/PetManager.cs` |
| `KickVisitor` | 20424 | ไล่ผู้มาเยือนออกจากที่ดิน/เกาะเรา (`Silent` = ไล่เงียบไม่แจ้ง) (?) | 1 | `client/SocialSystem.cs` |
| `SetResurrectionRewards` | 133 | ตั้งว่าจะให้ไอเทมอะไรเป็นรางวัลแก่คนที่มาช่วยชุบชีวิตเรา (CPR) (?) | 1 | `client/InventorySystem.cs` |
| `HostConcert` | 63459079 | เปิดคอนเสิร์ตบนเวทีตัวนี้ (เป็นเจ้าภาพ) | 1 | `client/MusicManager.cs` |
| `ResetAccessory` | 9823460 | ถอดเครื่องประดับที่ติดอยู่ทั้งหมดออก | 1 | `client/EquipSystem.cs` |
| `GetPioneerGradeInfo` | 812234574 | ✅ ขอเกรดผู้บุกเบิกและแต้มปัจจุบัน → `PioneerGradeInfo`; ตาราง `pioneer.json` (คุมขนาดที่ดิน + สิทธิ์เข้าภูมิภาค) | 1 | `client/EstateSystem.cs` |
| `RecommendArchipelago` | 3012 | ขอหมู่เกาะใหม่ตามระดับ/ชีวนิเวศ/ระดับความไม่เสถียร → `Archipelago`; แม่แบบที่ `archipelago_templates.json` | 1 | `client/ExploreSystem.cs` |
| `GetLastSearchedTime` | 906 | ถามว่าใช้ "ค้นหารอบตัว" ครั้งล่าสุดเมื่อไร (คิดคูลดาวน์) → `LastSearchedTime` | 1 | `client/InteractionSystem.cs` |
| `MountVehicle` | 327918 | ขึ้นพาหนะ (เกวียน/เครื่องยิง) ที่ตำแหน่งนั้น | 1 | `client/PetManager.cs` |
| `SetReturningPoint` | 2105 | ปักจุด "กลับมาที่นี่" (เช็คพอยต์) ที่ช่องนี้ — ยิงอัตโนมัติเมื่อเดินผ่าน trigger (?) | 1 | `client/PlayerTriggerMakeCheckPoint.cs` |
| `ReceiveAdvisorReward` | 3908 | รับรางวัลเมื่อเรียนจบหลักสูตรที่ปรึกษา (ได้ฉายา `TitleId`) → `OK` | 1 | `client/Durango.Logic/LearningGuideSystem.cs` |
| `GetDiscoveryInfo` | 5000 | ✅ ขออัตราการค้นพบของแม่แบบภูมิภาคนี้ (เจอสัตว์/พืช/แร่อะไรบ้างแล้ว) → `DiscoveryInfo` | 1 | `client/MapSystem.cs` |
| `Musics` | 47852454 | **[เซิร์ฟ→เกม]** เพลงทั้งหมดในช่องเก็บของผู้เล่น + เพลงที่แชร์ไว้ — คำตอบของ `GetMusics` (รับได้แล้ว) | 1 | `client/Durango.Online/Player.cs` |
| `FindTargetEntityPosition` | 3950 | ถามว่าสิ่งของ/สัตว์ประเภท `EntityType` ที่ต้องการอยู่ตรงไหน (ทำลูกศรนำทางในไกด์) → `TargetEntityPosition` | 1 | `client/Durango.UI/PlayGuideHelperGroupBase.cs` |
| `GetActions` | 314 | ขอรายการท่าต่อสู้ที่ใช้ได้ตอนนี้ → `Actions`; ข้อมูล `player/player_battle_actions.json` | 1 | `client/CombatSystem.cs` |
| `Error` | 1022 | **[เซิร์ฟ→เกม]** ตอบว่าคำสั่งล้มเหลว พร้อมชื่อชนิดและข้อความ | 1 | `client/Durango.Online/Player.cs` |
| `GetPunchMachineLeaderboard` | 785103 | ขอกระดานคะแนนของเครื่องวัดพลังหมัด → `PunchMachineLeaderboards`; ค่าเปิดเครื่องที่ `constants.json` → `punch_machine_activate_cost` | 1 | `client/PunchingLeaderboardSystem.cs` |
| `GetArchipelago` | 2121 | ✅ ขอข้อมูลหมู่เกาะตาม id → `Archipelago` | 1 | `client/ExploreSystem.cs` |
| `GetTechSupportEstimates` | 59138 | *(แต่งของ)* ขอใบเสนอราคาที่มีอยู่ของไอเทมหลายชิ้นพร้อมกัน → `TechSupportEstimates` | 1 | `client/TechSupportSystem.cs` |
| `PutInReinsToCage` | 694351 | เอาบังเหียนใส่เข้ากรง (เตรียมทำให้เชื่อง) | 1 | `client/PetManager.cs` |
| `Info` | 1023 | **[เซิร์ฟ→เกม]** ข้อความแจ้งเฉย ๆ ให้ผู้เล่นเห็น (ใช้กับผลของคำสั่งโกง/ระบบ) | 1 | `client/Durango.Online/Player.cs` |
| `GetRechargeShuffleCost` | 3625 | ถามราคาเติมโควตาสับเปลี่ยนภารกิจ → `Costs` | 1 | `client/FactionSystem.cs` |
| `LookAroundMood` | 234789 | "มองไปรอบ ๆ" ที่สิ่งปลูกสร้าง เพื่อรับบัฟบรรยากาศ (mood) → `Timer` แล้วติดสถานะ; ตั้งค่าที่ `constants.json` → `artifact_mood` | 1 | `client/InteractionSystem.cs` |
| `Unblock` | 4017 | ปลดบล็อกผู้เล่น → `Social` | 1 | `client/SocialSystem.cs` |
| `SelectTitle` | 2046 | เลือกฉายาที่จะโชว์ใต้ชื่อตัวละคร | 1 | `client/StatisticsSystem.cs` |
| `S02GetLobbyInfo` | 222214 | ขอข้อมูลล็อบบี้/คิว Warp Rush ซีซัน 2 → `S02LobbyInfo` | 1 | `client/Durango.Logic/WarpRushSystem.cs` |
| `RenameWarehouseSection` | 3696 | เปลี่ยนชื่อหมวดในคลัง | 1 | `client/InventorySystem.cs` |
| `FeedInCage` | 65101 | ป้อนอาหาร `ItemIds` ให้สัตว์ที่อยู่ในกรง | 1 | `client/PetManager.cs` |
| `ReceiveAcceleratorRewards` | 21112514 | รับรางวัลจากการลงขันเครื่องเร่งวาร์ป | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `FireProjectileFromVehicle` | 203493 | ยิงกระสุนจากพาหนะ (เครื่องยิงหิน) ไปยัง `TargetPosition` — ตั้งค่าที่ `constants.json` → `catapult` | 1 | `client/Durango.UI/CombatGroup.cs` |
| `Mount` | 802 | ขึ้นขี่สัตว์เลี้ยงที่เรียกออกมา | 1 | `client/PetManager.cs` |
| `BreakAlly` | 9138751 | *(แคลน)* ตัดความเป็นพันธมิตรกับแคลนนั้น → `OK` | 1 | `client/ClanSystem.cs` |
| `GetCapsulatingCost` | 4022 | ถามราคาการเก็บสิ่งปลูกสร้างเป็นแคปซูลก่อนกดจริง | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `GetCommodities` | 856700 | ขอรายการสินค้าร้านค้าที่ซื้อได้ → `Commodities`; ข้อมูล `purchaser/commodities.json` | 1 | `client/ShopSystem.cs` |
| `ParticipateTutorialBoat` | 2303 | เข้าร่วมเรือสอนเล่นตอนจะออกจากเกาะสอนเล่น — ดูเหมือนผู้เล่นช่วยกันเติมวัสดุให้เรือ (?) | 1 | `client/TutorialIslandSystem.cs` |
| `GetMilestoneCandidate` | 800010 | *(สัตว์)* ขอดูตัวเลือก milestone ที่จะสุ่มได้ของ `MilestoneId` นั้น → `MilestoneCandidates` | 1 | `client/PetManager.cs` |
| `S02EnqueueEntree` | 222201 | เข้าคิวรอเข้าโหมด Warp Rush ซีซัน 2 → `OK` | 1 | `client/Durango.Logic/WarpRushSystem.cs` |
| `GetNomadInfo` | 100000 | ขอสถานะ "นักเร่ร่อน" → `NomadInfo` — น่าจะเป็นระบบไกด์ผู้เล่นใหม่ (?) | 1 | `client/PlayGuideSystem.cs` |
| `Follow` | 2401 | ตามติดผู้เล่นคนนั้น (ติดตามความเคลื่อนไหว/เห็นตำแหน่ง) (?) | 1 | `client/SocialSystem.cs` |
| `MiniGameDanceStarted` | 4625401 | แจ้งว่าเริ่มมินิเกมเต้นแล้ว — ตั้งค่าที่ `constants.json` → `mini_game_dance` | 1 | `client/Durango.UI/MiniGameDanceGroup.cs` |
| `PutMaterialsIntoTutorialBoat` | 2304 | ใส่วัสดุลงเรือสอนเล่น | 1 | `client/TutorialIslandSystem.cs` |
| `PublishMusic` | 47852557 | เผยแพร่เพลงในช่อง `Slot` ให้คนอื่นเล่นได้ → `SharedSheet` | 1 | `client/MusicManager.cs` |
| `FinishConcert` | 63459101 | ปิดคอนเสิร์ต | 1 | `client/MusicManager.cs` |
| `UnmountAirBalloon` | 135867 | ลงจากบอลลูน | 1 | `client/PetManager.cs` |
| `DeleteEngagementData` | 1444251 | ลบข้อมูลความยินยอม/การมีส่วนร่วมของผู้เล่น (จากหน้าตั้งค่า) (?) | 1 | `client/Durango.UI.Popup/EngagementConfigPopup.cs` |
| `EngagementAgreementChanged` | 1444250 | ผู้เล่นกดยอมรับ/ยกเลิกความยินยอม (`Agreed`) (?) | 1 | `client/Durango.Logic/EngagementSystem.cs` |
| `InvestToCrack` | 3663 | ลงขัน "หินนำทางทรัพยากร" จำนวน `Amount` ใส่รอยแยก (crater) เพื่อเปิด แล้วจะมีทรัพยากรวาร์ปเข้ามา → `Timer` แล้ว `OK`; ตั้งค่าที่ `constants.json` → `crack` | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `CancelTargetTitle` | 3901 | ยกเลิกหลักสูตรที่ปรึกษาที่กำลังเรียนอยู่ → `OK` | 1 | `client/Durango.Logic/LearningGuideSystem.cs` |
| `UseTamingAction` | 1900 | ใช้ท่า "ทำให้เชื่อง" ใส่สัตว์ป่า (ต่างจากท่าโจมตี) โดยใช้เครื่องมือ `ToolItemId` | 1 | `client/Durango.Logic.Combat/UsingAction.cs` |
| `GetTargetTitle` | 3906 | ✅ ขอหลักสูตร/ฉายาเป้าหมายที่กำลังไล่ตามอยู่ → `TargetTitle` | 1 | `client/Durango.Logic/LearningGuideSystem.cs` |
| `RefuseSuggestion` | 9138750 | *(แคลน)* ปฏิเสธข้อเสนอเป็นพันธมิตร → `OK` | 1 | `client/ClanSystem.cs` |
| `Unfollow` | 2410 | เลิกตามติดผู้เล่นคนนั้น | 1 | `client/SocialSystem.cs` |
| `GetAvailableEmotions` | 9592634 | ✅ ขอรายการท่าทาง/อีโมติคอนที่ใช้ได้ → `AvailableEmotions`; ข้อมูล `emotions.json` | 1 | `client/SocialSystem.cs` |
| `GetSeasons` | 871245 | ขอข้อมูลซีซันที่เปิดอยู่ (ช่วงเวลา/รางวัล) → `Seasons`; ข้อมูล `season/season2_rewards_client.json` | 1 | `client/Durango.Logic/SeasonSystem.cs` |
| `AttachAccessory` | 9823459 | ติดเครื่องประดับ `AccessoryId` เข้ากับชุด — ข้อมูล `accessories.json` | 1 | `client/EquipSystem.cs` |
| `Tool_Collectibles` | 328 | เครื่องมือ dev: ส่งรายการของที่เก็บได้เข้าไป (หน้าต่างโกงการเก็บของ) | 1 | `client/Durango.UI/GatheringCheatWidget.cs` |
| `GetMusic` | 47852452 | ขอโน้ตเพลงในช่อง `Slot` ของผู้เล่นคนนั้น → `Musics` | 1 | `client/MusicManager.cs` |
| `GetRouteOfArchipelago` | 20301 | ขอเส้นทางเดินเรือทั้งหมดของหมู่เกาะจากท่านี้ → `RoutesOfArchipelago` | 1 | `client/ExploreSystem.cs` |
| `AcceptMilestone` | 800015 | *(สัตว์)* ยืนยันรับ milestone ที่สุ่มได้ → `MilestoneResult` | 1 | `client/PetManager.cs` |
| `GetTimelineOption` | 81234526 | ขอค่าตั้งของ "ไทม์ไลน์" (บันทึกเหตุการณ์ที่ดิน) → `TimelineOption`; ข้อความอยู่ `timeline_messages.json` | 1 | `client/Durango.Logic.Timeline/TimelineLogList.cs` |
| `WashBody` | 3494 | อาบน้ำ ล้างความสกปรก → `Timer`; ตั้งค่าที่ `constants.json` → `wash_body` | 1 | `client/InteractionSystem.cs` |
| `ChangeFarmingEncyclopediaMastery` | 37128 | เลือก/สลับ "ความชำนาญ" ของเมล็ดพันธุ์ในสารานุกรมเพาะปลูก — ราคาสลับที่ `costs.json` → `encyclopedia_mastery_swap` | 1 | `client/FarmingEncyclopediaSystem.cs` |
| `GrowRapidly` | 3712 | เร่งให้พืชในแปลงโตทันที → `OK` | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `PlayConcert` | 63459081 | เริ่มบรรเลงคอนเสิร์ตที่ตั้งคิวเพลงไว้ | 1 | `client/MusicManager.cs` |
| `ParticipateAcceleration` | 21112513 | เข้าร่วมลงขันเครื่องเร่งวาร์ปที่สิ่งปลูกสร้างนี้ | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `Collected` | 104 | **[เซิร์ฟ→เกม]** ผลของการเก็บของ: ได้ไอเทมอะไร สำเร็จไหม ท่าที่เล่น และแหล่งหมดหรือยัง (`RanOut`) | 1 | `client/Durango.Online/Player.cs` |
| `Block` | 4016 | บล็อกผู้เล่น → `Social` | 1 | `client/SocialSystem.cs` |
| `Depart` | 2448 | แจ้งว่าผู้เล่น "เริ่มออกเดิน" จากจุดที่ยืน (ยิงครั้งเดียวตอนเริ่มขยับด้วยมือ ก่อนสตรีม `Move`) | 1 | `client/MoveMsgGenerator.cs` |
| `Tune` | 2400 | ยืนยันตัวตนกับ **เซิร์ฟแชต (Radiotower)** ด้วย `SessionToken` → `Conversations` — ไม่ได้วิ่งบนเซิร์ฟเกม | 1 | `client/SocialSystem.cs` |
| `Keepalive` | 254 | ปิงกันหลุดการเชื่อมต่อ — ต้องมีตั้งแต่แรก | 1 | `client/Durango.Network/Connection.cs` |
| `Bleach` | 3669 | ฟอกสีของ (ล้างสีที่ย้อมไว้ออก) — โครงเหมือน `Dye` ทุกอย่าง | 1 | `client/CraftSystem.cs` |
| `AcceptSuggestion` | 9138749 | *(แคลน)* ตอบรับข้อเสนอเป็นพันธมิตร → `OK` | 1 | `client/ClanSystem.cs` |
| `GetAllySlots` | 9138745 | *(แคลน)* ขอช่องพันธมิตรที่มี/ใช้ไปแล้ว → `AllySlots`; ตั้งค่าที่ `constants.json` → `ally` | 1 | `client/ClanSystem.cs` |
| `Withdraw` | 2028 | ถอนตัวจากการเตรียมออกเรือที่ท่านี้ (ยกเลิกก่อนออกเดินทาง) (?) | 1 | `client/ExploreSystem.cs` |
| `SuggestBreak` | 9138748 | *(แคลน)* เสนอตัดพันธมิตร → `OK` | 1 | `client/ClanSystem.cs` |
| `ReturnToCamp` | 3462987 | วาร์ปกลับแคมป์ — ตั้งค่าที่ `constants.json` → `camp` | 1 | `client/MapSystem.cs` |
| `AcceptTENCoupon` | 2345690 | กรอกโค้ดคูปองรับของ → `OK` | 1 | `client/Durango.System.Config/ConfigInstance.cs` |
| `GiveUpDistribution` | 451390 | สละสิทธิ์ส่วนแบ่งของจากแหล่งเก็บของที่แบ่งกันหลายคน | 1 | `client/GatheringSystem.cs` |
| `GetAttachableAccessories` | 9823457 | ขอรายการเครื่องประดับที่ติดเข้ากับชุดปัจจุบันได้ → `AttachableAccessories` | 1 | `client/EquipSystem.cs` |
| `ExtinguishBurnable` | 2097 | ดับไฟที่สิ่งปลูกสร้างที่ติดไฟอยู่ | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `DepartTutorial` | 2306 | ออกเรือจากเกาะสอนเล่นไปโลกจริง | 1 | `client/TutorialIslandSystem.cs` |
| `SailingBack` | 3130 | ✅ ล่องเรือกลับภูมิภาคก่อนหน้า | 1 | `client/ExploreSystem.cs` |
| `Dashed` | 2491 | แจ้งว่าผู้เล่นพุ่ง/กระโดด — ยิงจาก `TryJump()` น่าจะให้เซิร์ฟหักพลังงาน (?) — ตั้งค่าที่ `constants.json` → `dash` | 1 | `client/PlayerController.cs` |
| `ToggleConversationNotification` | 4011 | เปิด/ปิดแจ้งเตือนของห้องสนทนานั้น → `OK` | 1 | `client/SocialSystem.cs` |
| `ReleaseReinFromCage` | 694353 | ปล่อยบังเหียนออกจากกรง (ยกเลิกที่เตรียมไว้) → `OK` | 1 | `client/PetManager.cs` |
| `SetSectionOrder` | 3688 | บันทึกลำดับของหมวดต่าง ๆ ในคลัง | 1 | `client/InventorySystem.cs` |
| `GetSpecialDeals` | 259680 | ขอรายการดีลพิเศษที่เปิดอยู่ → `SpecialDeals`; ข้อมูล `purchaser/special_deals.json` | 1 | `client/ShopSystem.cs` |
| `ChangeFollowMusic` | 47852459 | กดติดตาม/เลิกติดตามเพลงที่คนอื่นแชร์ (`SharedSheetId`) → `SharedSheet` | 1 | `client/MusicManager.cs` |
| `ExitConversation` | 4010 | ออกจากห้องสนทนา | 1 | `client/SocialSystem.cs` |
| `Collect` | 2026 | เก็บของจากแหล่ง (`GeneratorId` ระดับ `Level`) ด้วยเครื่องมือ `ToolItemId` → `Collected` หรือ `ToolNeeded`/`SkillNeeded`/`EnergyWarning` ถ้าเงื่อนไขไม่ผ่าน | 1 | `client/GatheringSystem.cs` |
| `SetSharedConcertMusic` | 63459180 | ตั้งเพลงที่แชร์มาเป็นเพลงในคิวคอนเสิร์ต | 1 | `client/MusicManager.cs` |
| `UnmountVehicle` | 192834 | ลงจากพาหนะ | 1 | `client/PetManager.cs` |
| `GardenDiff` | 202 | **[เซิร์ฟ→เกม]** ส่งความเปลี่ยนแปลงของพืชพรรณใน chunk นั้นเป็นข้อมูลดิบ (`byte[]`) — ระบบพืชพรรณเติบโตของโลก | 1 | `client/Durango.Online/Player.cs` |
| `SkipPostprocess` | 2450 | จ่าย `Cost` ข้ามเวลารอขั้นตอนหลังสร้าง (ตากแห้ง/บ่ม) ของสิ่งปลูกสร้าง | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `GetMemos` | 2439 | ขอบันทึก/โน้ตที่เก็บสะสมได้ → `Memos`; ข้อมูล `memos.json` | 1 | `client/MemoSystem.cs` |
| `FireBurnable` | 2096 | จุดไฟใส่สิ่งปลูกสร้างที่ติดไฟได้ | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `ReturnToHome` | 2100 | วาร์ปกลับ "บ้าน" ที่ปักไว้ (`SetAsHome`) | 1 | `client/MapSystem.cs` |
| `Resurrect` | 132 | ทำ CPR ช่วยผู้เล่นที่ล้ม โดยส่งคะแนนมินิเกม `Score` ที่ทำได้ | 1 | `client/CPRSystem.cs` |
| `DeregisterUser` | 1999 | ลบบัญชีผู้เล่นถาวร → `OK` | 1 | `client/Durango.System.Config/ConfigInstance.cs` |
| `GetAvailablePersonalResearch` | 5987336 | ขอรายการงานวิจัยส่วนตัวที่ทำได้ที่โต๊ะนี้ → `AvailablePersonalResearch` | 1 | `client/Durango.Logic/ResearchSystem.cs` |
| `GetSharedMusic` | 47852457 | ขอโน้ตเพลงที่แชร์ไว้ตาม `SheetId` → `SharedMusic` | 1 | `client/MusicManager.cs` |
| `S02PVPRefresh` | 222207 | ขอสถานะรอบ PvP ซีซัน 2 ใหม่ (จำนวนผู้รอด/สถานะเกม) → `S02PVPStatus` | 1 | `client/Durango.Logic/PvpIslandSystem.cs` |
| `TakeEffect` | 821 | รับผล/บัฟจากสิ่งปลูกสร้างที่ชาร์จไว้ — เดาจากการเป็นคู่กับ `ChargeEffect` (เซิร์ฟรับได้แล้ว) (?) | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `UprootPlant` | 3807 | ถอนต้นไม้/พืชในแปลงทิ้ง | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `Feeding` | 805 | ป้อนอาหาร `FoodIds` ให้สัตว์ที่เรียกออกมา (นอกกรง) | 1 | `client/PetManager.cs` |
| `DisappearEntity` | 101 | **[เซิร์ฟ→เกม]** แจ้งว่าสิ่งมีชีวิต/วัตถุ id นี้หายไปจากสายตาแล้ว | 1 | `client/Durango.Online/Player.cs` |
| `SetTimelineOption` | 81234528 | ตั้งว่าจะเปิดแจ้งเตือนเหตุการณ์ที่ดินไหม (`EstateNotification`) | 1 | `client/Durango.Logic.Timeline/TimelineLogList.cs` |
| `GetCollectible` | 2017 | ขอข้อมูลแหล่งเก็บของตรงนั้น (เก็บได้อะไร เหลือเท่าไร) → `Collectible` | 1 | `client/GatheringSystem.cs` |
| `PickMilestoneAgain` | 800014 | *(สัตว์)* สุ่ม milestone ใหม่ (จ่ายบัตรได้ด้วย `WithVoucher`) — ราคาที่ `costs.json` → `pet_revert_milestone` | 1 | `client/PetManager.cs` |
| `GetWarehouse` | 3683 | ขอข้อมูลคลังของสิ่งปลูกสร้างนี้ (มีหมวดอะไรบ้าง ความจุเท่าไร) → `Warehouse` | 1 | `client/Durango.Logic.Item/Inventory.cs` |
| `MiniGameDanceScore` | 4625400 | ส่งคะแนนที่ทำได้ในมินิเกมเต้น | 1 | `client/Durango.UI/MiniGameDanceGroup.cs` |
| `ReissueArchipelagoTodos` | 240005 | ขอออกรายการภารกิจหมู่เกาะชุดใหม่ (สุ่มใหม่) (?) | 1 | `client/Durango.Logic/ArchipelagoMissionSystem.cs` |
| `GetRoutes` | 2030 | ✅ ขอเส้นทางเดินเรือที่ออกจากท่านี้ได้ → `Routes` | 1 | `client/ExploreSystem.cs` |
| `S02DequeueEntree` | 222202 | ออกจากคิว Warp Rush → `OK` | 1 | `client/Durango.Logic/WarpRushSystem.cs` |
| `GetSailingBackCost` | 3110 | ✅ ถามค่าล่องเรือกลับ → `SailingBackCost` | 1 | `client/ExploreSystem.cs` |
| `ConfirmResurrection` | 56238474 | ผู้เล่นที่ล้มกดยืนยันรับการช่วยจาก `HelperEntityId` | 1 | `client/CPRSystem.cs` |

## รับได้แล้ว (33)

`ChangeMannequinDisplay`, `ChargeEffect`, `Cheat`, `CloseGate`, `DestructArtifact`, `DisappearEntityOnTile`, `Display`, `ExtendFloor`, `GetAddOns`, `GetArtifactBlueprints`, `GetClock`, `GetEstateLicenses`, `GetFavoriteProducts`, `GetGrazedPets`, `GetMusics`, `GetQuests`, `GetRecipes`, `OpenGate`, `PlaceAddOns`, `PlantSeed`, `PlayMusic`, `Ready`, `RemoveMusicFromSlot`, `RestOn`, `SaveMusicToSlot`, `Scribble`, `SetChunk`, `StopMusic`, `TakeOutItem`, `Touch`, `TurnOffMusic`, `TurnOnMusic`, `Wash`

---

# ต้องทำอะไรบ้างถึงจะใช้งานได้

ตารางนี้ตอบว่า "ถ้าจะเปิดระบบนี้ให้เล่นได้จริง ต้องเก็บ state อะไร ข้อมูลอยู่ไหน และอะไรที่ข้อมูลเกมไม่มีให้ ต้องคิดเอง"

| ระบบ | ต้องมี state อะไร (เซิร์ฟ) | ข้อมูลเกมอยู่ไฟล์ไหน | ต้องตั้ง/เขียนเอง |
|---|---|---|---|
| **ของ/กระเป๋า** (26) | รายการ item ต่อผู้เล่น (id, prototype, ระดับ, ความทนทาน, แท็ก), ลำดับการเรียง, ธงล็อก, คลังต่อสิ่งปลูกสร้างแยกเป็น section, คิวขนส่ง cargo + ภาษี | `item/prototype_data.json` (2,407), `tags.json`, `item/bonus_prototypes.json`, `constants.json` → `item`/`warehouse`/`cargo_warp` | ตัวสร้าง item id, ความจุกระเป๋า/คลังจริง, กติกาไอเทม "ไม่เสถียร" หายเมื่อออกเกาะ, สูตรค่าส่ง cargo |
| **สกิล/เลเวล** (27) | สกิลที่เรียนแล้ว + แต้มคงเหลือ, สถานะวิจัยแต่ละหมวด (เวลาเสร็จ), ค่า Statistics ที่คำนวณจากสกิล+อุปกรณ์+บัฟ, EXP/เลเวลค่าต้านทาน | `skill/skills.json`, `skill/categories.json`, `skill/modifiers.json`, `skill/rewards.json`, `statistics/player.json`, `constants.json` → `experience`/`skill_points`/`resistance` | สูตรรวมค่า Statistics (ไม่มีในข้อมูล), เวลาวิจัยแต่ละหมวด, ตารางเลเวล→แต้มสกิล |
| **ที่ดิน (Estate)** *(อยู่ในหมวดสกิล)* | ใบอนุญาตที่ดินต่อผู้เล่น/แคลน (ตำแหน่ง, ขนาด, วันหมดอายุ, สิทธิ์เข้าออก), แต้มผู้บุกเบิก | `pioneer.json` (ขนาดตามเกรด), `costs.json` → `estate`, `constants.json` → `estate` | ผังช่อง (cell grid) ของแต่ละภูมิภาค, กติกาที่ดินหมดอายุ/ถูกยึด |
| **เอาชีวิตรอด** (7) | สถานะ (status effect) ที่ติดตัว + เวลาหมดอายุ, ค่าความเหนื่อยล้าแยกหมวด, ตารางเช็คชื่อรายวันต่อบัญชี, อากาศต่อภูมิภาค | `survival/status_effects.json` (372), `survival/fatigue_categories.json`, `survival/date_time.json`, `constants.json` → `fatigue_*`/`attendance` | นาฬิกาโลก + วนกลางวัน/กลางคืน, ลูปคำนวณ fatigue, ตารางรางวัลเช็คชื่อ (มีแต่โครงใน constants) |
| **คราฟต์** (9) | คิวงานคราฟต์ต่อโต๊ะ (สูตร, วัสดุที่ใส่, เวลาเสร็จ), รายการสูตร/พิมพ์เขียวที่ปลดแล้ว + ที่กดถูกใจ | `item/recipes.json` (720 ครบ), `building/blueprints.json` (556 ครบ), `crafting_rewards*.json`, `colortable.json` (ย้อมสี) | กติกาปลดสูตร (สูตรไหนได้ตอนไหน), สูตรอัตราสำเร็จ/great success, ตรรกะ entrusted craft |
| **ต่อสู้** (4) | HP/stamina/aggro ของสัตว์และผู้เล่น, คูลดาวน์ท่า, สถานะตาย + จุดเกิด | `player/player_battle_actions.json`, `entity_types/animal.json` (214 ตัว มี attack/defense/life ครบ), `constants.json` → สูตรความเสียหาย | ลูป AI สัตว์, การจับคู่ hit/damage ฝั่งเซิร์ฟ (client เป็นคนบอกเวลา `StartAt` → ต้องมีกันโกง) |
| **สร้าง/ที่ดิน** (5) | สิ่งปลูกสร้างต่อภูมิภาค (ตำแหน่ง, ชั้น, วัสดุที่ใส่แล้ว, สถานะสร้าง/postprocess, สิทธิ์เข้าถึง, ความทนทาน) | `building/blueprints.json`, `artifact_effects.json`, `artifact_set_effects.json`, `entity_types/artifact.json` | การชนกัน/วางทับ, ระบบ "เพื่อนช่วยเร่ง postprocess", ค่าเสื่อมสภาพตามเวลา |
| **สัตว์** (25) | สัตว์เลี้ยงต่อผู้เล่น (ชนิด, แรงก์, เลเวล, EXP, milestone, active skill, ความหิว, กรงที่อยู่, กระเป๋า), คิวงาน, สถานะการทำให้เชื่อง | `pet/` ครบทั้งโฟลเดอร์ (74 ชนิด, 48 งาน, active skills, exp), `entity_types/animal.json` (`tamable`/`rein_id`/`preferred_food_tag`), `costs.json` → `pet_*` | ตารางความน่าจะเป็นของการสุ่ม milestone/rank/skill (กาชา — ไม่มีในข้อมูล), ลูปความหิว, กติกาสัตว์ตาย |
| **เควส** (35) | เควสที่รับ/ทำอยู่/จบแล้วต่อผู้เล่น, ความคืบหน้าแต่ละ todo, แต้มเควสรายหมวด, ภารกิจฝ่าย + โควตาสับเปลี่ยน, ระดับมิตรภาพฝ่าย | `quests/quests_for_client.json` (1,386 — **แค่ข้อความ**), `factions.json` (7 ฝ่าย + รางวัลครบ), `faction_talks.json`, `quests/archipelago_todos_client.json` | ⚠️ **เงื่อนไข/todo/รางวัลของเควสทั้ง 1,386 ตัวไม่มีในข้อมูล** ต้องเขียนเองทั้งหมด; ตัวสร้างภารกิจฝ่ายก็ไม่มีข้อมูล |
| **แผนที่/เดินทาง** (28) | ภูมิภาคที่มีอยู่ในโลก + อายุ, chunk ที่เปิดหมอกต่อผู้เล่น, POI ที่สำรวจแล้ว, เส้นทางเดินเรือ, ประวัติการวาร์ป (สำหรับ `WarpBack`) | `region_templates.json` (267), `archipelago_templates.json` (86), `constants.json` → `warp`/`point_of_interest`/`explorer` | การวางเส้นทางเรือระหว่างภูมิภาค, การหมดอายุ/สลับภูมิภาค, ตำแหน่ง POI จริงในแต่ละแผนที่ (มาจากไฟล์ terrain ไม่ใช่ assets) |
| **สังคม** (43) | เพื่อน/คำขอ/บล็อกต่อผู้เล่น, แคลน (สมาชิก, ยศ, กองทุน, ตรา, พันธมิตร, วิจัย), ปาร์ตี้, กล่องจดหมาย 2 ชุด (ระบบ/ผู้เล่น) | `clan.json`, `clan_research.json` (15), `emotions.json`, `constants.json` → `clan_*`/`ally`/`mail`/`letter_limits` | ⚠️ **แชตต้องมี endpoint แยก (Radiotower)** — `Tune`/`Conversations` ไม่ได้วิ่งบนเซิร์ฟเกม; ต้องเขียนเซิร์ฟแชตต่างหาก |
| **ตลาด/เงิน** (8) | ประกาศขายในตลาด, เงินค้างรับ, ประวัติคำสั่งซื้อ/ของที่ยังไม่กดรับ | `purchaser/commodities.json`, `special_deals.json`, `vouchers.json`, `cash.json` | ไม่มีการชำระเงินจริง — ต้องตัดสินใจว่าจะแจกฟรีหรือผูกกับสกุลเงินในเกม; `MarketManager` ในเซิร์ฟมีโครงแล้ว |
| **ดนตรี/คอนเสิร์ต** *(อยู่ในหมวดอื่นๆ)* | โน้ตเพลงต่อผู้เล่น (ช่องเก็บ), เพลงที่เผยแพร่ + คนติดตาม, สถานะเวทีคอนเสิร์ต (เจ้าภาพ, คิวเพลง, นักดนตรี) | — (ไม่มีไฟล์ข้อมูล ผู้เล่นแต่งเองทั้งหมด) | ทั้งระบบเผยแพร่/ติดตาม; `GetMusics`/`PlayMusic`/`SaveMusicToSlot` เซิร์ฟรับได้แล้วเป็นฐาน |
| **ซีซัน 2 / PvP** *(อยู่ในหมวดอื่นๆ)* | คิวเข้าเล่น, สถานะรอบ (ผู้รอด/พายุ), คะแนน/รางวัลอันดับ | `season/season2_rewards_client.json`, `ranking.json`, `ranking_rewards.json`, `constants.json` → `season2`/`colosseum` | ตัวจับคู่/จัดรอบทั้งหมด, แผนที่เกาะ PvP; **ควรทำท้ายสุด** |
| **โครงพื้นฐานโปรโตคอล** *(อยู่ในหมวดอื่นๆ)* | — | — | `OK`/`Error`/`Info`/`Abort`/`Timer`/`Keepalive`/`Depart` ต้องมีก่อนระบบอื่นทั้งหมด — `Timer` โดยเฉพาะ เพราะ **ทุกกิจกรรมที่มีหลอดเวลา** (ดื่มน้ำ อาบน้ำ ซ่อม คราฟต์ ลงขัน) รอ `Timer` เป็นคำตอบแรก |

## จุดที่ควรระวังตอนวางแผน

1. **`Timer` เป็นคอขวด** — คำสั่งกิจกรรมส่วนใหญ่ (`DrinkWater`, `WashBody`, `RepairItem`, `RepairArtifact`, `LookAroundMood`, `InvestToCrack`, `UseItem`) ตอบกลับด้วย `Timer` ก่อน แล้วเกมค่อยเล่นแอนิเมชัน ถ้าไม่ตอบ เกมจะค้างหลอดไว้จนหมดเวลา fallback
2. **`InventoryUpdated` คือเส้นเลือดใหญ่** — ถูกอ้างถึง 11 จุดในเกม ระบบไหนที่ให้/หักไอเทมต้องผลัก message นี้ตามทุกครั้ง ไม่งั้น UI ไม่อัปเดต
3. **เควสเป็นงานเขียนเนื้อหา ไม่ใช่งานพอร์ต** — ข้อมูล 1,386 เควสมีแต่ข้อความแสดงผล เงื่อนไขสำเร็จไม่มีเลย ควรเลือกทำเฉพาะสาย `mainstory` (101) + `daily` (24) ก่อน อย่าเปิดทั้งหมด
4. **แชตต้องมีเซิร์ฟที่สอง** — ทั้งหมวดสังคมส่วนที่เป็นการสนทนา วิ่งบน `Connections.Radiotower` ไม่ใช่ `Connections.Frontend` ถ้าวางแผนว่า "ทำหมวดสังคม" ต้องแยกงานนี้ออกมาเป็นก้อนของตัวเอง
5. **หมวดสัตว์คือระบบกาชา** — `DrawActiveSkill`/`RedrawActiveSkill`/`PickMilestone`/`PickMilestoneAgain`/`RevertPetRank`/`AcceptPetRank` เป็นชุดสุ่ม-ดูผล-ยืนยัน/รีโรล ต้องทำตารางความน่าจะเป็นเอง (ข้อมูลเดิมไม่มี) และต้องมี state ค้าง "ผลที่สุ่มได้แต่ยังไม่ยืนยัน" ต่อสัตว์หนึ่งตัว
6. **หมวดที่สคริปต์จัดผิดที่เยอะ** — อย่าใช้หัวข้อหมวดเป็นหน่วยวางแผนตรง ๆ ที่ดิน 10 ตัวอยู่ในหมวด "สกิล", เรื่องแคลนกระจายอยู่ 4 หมวด, เรื่องสัตว์อีก 4 ตัวอยู่ในหมวด "สกิล"
