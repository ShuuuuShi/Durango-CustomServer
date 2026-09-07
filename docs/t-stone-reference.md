# t_stone_reference — สิ่งที่ค้นในชุดข้อมูล (7 ก.ย. 2026)

ตัวแปรสูตรของ NEXON ที่สูตรเศรษฐกิจหลายตัวอ้างถึง **แต่ไม่มีค่าตัวเลขในไฟล์ที่สกัดมา**

ห้ามเดาเรทเอง — กฎโปรเจกต์และงาน GetCapsulatingCost ใช้ข้อนี้ตรง ๆ

## สูตรที่อ้างตัวแปรนี้ใน `server/data/assets/constants.json`

| ที่ | สูตร |
|---|---|
| `sailing.travel_cost` | `t_stone_reference * 12` |
| `build.capsulating.cost.outside` | `t_stone_reference * level` |
| `poi_finding_reward.t_stone_warphole` | `t_stone_reference * 4` |
| `poi_finding_reward.t_stone_biocom` | `t_stone_reference * 4` |
| `all_pois_finding_reward.t_stone_amount` | `t_stone_reference * 40` |
| `warp.warp_cost` | `t_stone_reference * 12 * (dist / 300.)` |
| `warp_accelerator.participation_fee.amount` | `int(round(t_stone_reference * 30,-1))` |

`build.capsulating.cost.inside` เป็น `"0"` — คิดได้ทันที ไม่ต้องใช้ตัวแปรนี้

ตัวแปรสูตรอีกตัวที่คล้ายกันและก็ไม่มีค่าในไฟล์: `exp_tstone` (`faction_support.support_fee`)

## ที่ค้นแล้วไม่เจอค่าตัวเลข

ค้นทั้ง repo (ไม่เดาจากวิกิ/ความจำผู้เล่น):

- **`constants.json`** — มีแต่สตริงสูตรด้านบน ไม่มีคีย์ `"t_stone_reference": <number>`
- **`costs.json`** — ไม่มี
- **`region_templates.json`** (267 แม่แบบ) — ฟิลด์ที่มีจริงคือ level / role / biome / herds / weather / … **ไม่มี** ช่องเงินหรือ reference
- **assets อื่นทั้งต้น** (`*.json` / `*.yml`) — สตริง `t_stone_reference` โผล่แค่ใน `constants.json`
- **ซอร์สเกม `client/`** — ไม่มีสัญลักษณ์นี้ (ค่าเก็บถามเซิร์ฟด้วย `GetCapsulatingCost` ไม่คิดฝั่งเกม)
- **`region_templates` / island `config.json`** — มี `RegionLevel` สำหรับสูตรความล้า ไม่ใช่เรท T Stone

ข้อสรุป: นี่คือ **ตัวแปรบริบทฝั่งเซิร์ฟต้นฉบับ** (น่าจะผูกกับภูมิภาคตอนรัน) ที่ชุด client assets ไม่ได้พกค่ามาด้วย

`BuildTuning.TStoneReference` จึงคืน `null` และจะอ่านอัตโนมัติถ้าวันหนึ่งมีคีย์ตัวเลขชื่อนี้ใน `constants.json`

## inside / outside ของค่าเก็บแคปซูล

ไม่ใช่ "ในอาคาร / นอกอาคาร"

ฝั่งเกมเตือนตอนปักสิ่งปลูกสร้างนอกที่ดิน (`client/UITable.cs` → `WarningEstateOut`):

> 부족 영토와 사유지 바깥에 배치된 건축물은 포장 시 비용이 발생합니다.

แปล: สิ่งปลูกสร้างที่อยู่นอกที่ดินส่วนตัวและเขตแคลน **เก็บแล้วคิดเงิน**

ดังนั้น:

| สูตร | ความหมาย | คิดได้ไหม |
|---|---|---|
| `inside: "0"` | footprint ทั้งก้อนอยู่บนที่ดิน (Player / PersonalPlayer / ClanEstate / System) | ได้ — 0 T Stone |
| `outside: "t_stone_reference * level"` | มีช่องใดช่องหนึ่งอยู่นอกที่ดิน | ไม่ได้ — ไม่มี `t_stone_reference` |

`level` ในสูตรนอกที่ดินคือเลเวลสิ่งปลูกสร้าง (`ArtifactState.Level`) ไม่ใช่เลเวลเกาะ

## พฤติกรรมเซิร์ฟตอนนี้

`HandleGetCapsulatingCostMsg` / `CapsulateArtifact` ใช้สูตรชุดเดียวกันผ่าน `BuildTuning.TryEvalCapsulatingCost`:

- บนที่ดิน → ส่ง/หัก **0** (ค่าจริงจากไฟล์)
- นอกที่ดินที่คิดสูตรไม่ได้ → ส่ง **0 แบบ STUB** (คอมเมนต์ `STUB` ใน `Player.Building.cs`) เพื่อให้กล่องยืนยันยังเด้ง แล้ว `CapsulateArtifact` เดินต่อ
- ถ้าสูตรคิดได้และจำนวน &gt; 0 → โชว์จำนวนนั้น แล้วหัก T Stone ตอนเก็บจริง (เงินไม่พอ = `Abort`)
- ของที่แตะไม่ได้ / ไม่เจอหลัง → `Abort` ข้อความชัด ไม่ส่งราคาปลอม

ไม่เดาว่า `t_stone_reference` เท่ากับเลเวลเกาะหรือเลขกลม ๆ เช่น 100
