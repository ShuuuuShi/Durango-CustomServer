# Gathering — โน้ตความเท่า Nexon

คู่กับ `server/Core/Player.Gathering.cs` (`CollectibleTable` + `GatheringTuning`)

## G2 — เลเวลเครื่องมือ vs เลเวลเป้า (UNKNOWN)

**สมมติฐานที่ยืนยันแล้ว:** `GatheringTuning.ToolLevel = 1` เป็นค่าคงที่ที่ทำให้ตรวจเครื่องมือเทียบเป้าไม่ได้  
ฝั่งเกมเทียบ `tag.Level >= RequiredTools[tag]` อยู่แล้ว (`client/InteractionData/GatheringData.cs` `FindBestTool` / `CanGateringWithThisTool`)  
เลเวลแท็กของเครื่องมือ = เลเวลไอเทมตอนสร้าง (`Cheats.MakeItem` คัดลอก `Item.Level` ลงทุกแท็ก)

**สิ่งที่มีใน repo (ไม่เดา):**

| แหล่ง | ฟิลด์ | ความหมาย |
|---|---|---|
| `CollectibleTable.GeneratorSpec.Level` | `prototype_data.json` → `min_level` ของไอเทมที่จะได้ | เลเวล generator ที่ส่งให้เกม + ใช้คิด effort |
| `Item.Tags[].Level` ในกระเป๋า | เลเวลไอเทมตอน `MakeItem` | เลเวลแท็กเครื่องมือที่ผู้เล่นถือ |
| `Collect.Level` จากเกม | echo ของ `Generator.Level` ที่เซิร์ฟส่งไป | ไม่ใช้เป็นตัวตัดสิน — เกมส่งค่าที่เราให้เอง เชื่อตารางเซิร์ฟดีกว่า |

**สิ่งที่ไม่มีใน assets (ค้นแล้ว):** ตารางเลเวลเครื่องมือต่อ generator ของ Nexon  
(`tool_requirements` / `generators` / `collectible_data` ใน `server/data` และไฟล์เกม = ไม่เจอ)  
สูตร `constants.json → effort_standard.collect` ใช้เลเวลคิดเวลา/แรง ไม่ใช่เกณฑ์ผ่านเครื่องมือ

**UNKNOWN:** ของจริงอาจให้เป้าที่แข็งกว่าต้องการเครื่องมือเลเวลสูงกว่าแบบไม่เท่ากับ `min_level` ของของที่ได้  
ไม่มีตัวเลขนั้นในข้อมูลที่หลุดมากับ client ⇒ **ไม่คิดสูตร/ไม่ใส่ตัวคูณที่ไม่มีแหล่งที่มา**

**การเทียบที่ทำ (เล็กสุดที่ซื่อตรง):**  
`tag.Level >= spec.Level` และใส่ค่าเดียวกันลง `Generator.ToolRequirements` ให้ฝั่งเกมเลือกเครื่องมือชุดเดียวกับเซิร์ฟ  
`ToolNeeded` / `SkillNeeded` / `EnergyWarning` ไม่เปลี่ยนเส้นทาง — แค่เลเวลใน `ToolRequirements` มาจาก spec แทนค่าคงที่ 1

หมายเหตุ: วัตถุดิบธรรมชาติชุดหลัก (`wood_log` / `stone` / `meat` / …) มี `min_level = 1` ในไฟล์ปัจจุบัน ดังนั้นพฤติกรรมตอนนี้ยังใกล้เคียง "มีเครื่องมือชนิดนั้นก็พอ" จนกว่าจะมีเลเวลเป้าจริง (เช่นต่อโหนด/เกาะ) มาใส่ที่ `spec.Level`
