# nexonSRC — ต้นฉบับจริงของ NEXON (ยังไม่มีมอดของเรา)

ถอดจาก `Original_Game/Durango_Ver_PC_Final/Durango_Data/Managed/Assembly-CSharp.dll`
(14 ม.ค. 2020 · 6,313,984 bytes) ด้วย **ILSpy 11.0.0.9375** เมื่อ 1 ก.ย. 2026 · ได้ 3,755 ไฟล์

## ทำไมต้องมีชุดนี้

`extracted/` (AssetRipper) **ไม่ใช่ต้นฉบับ** — ถอดมาจาก DLL ที่เราแพตช์ไปแล้ว
(มี `ClientModLoader.cs`, `ClientMethodOverrideManager.cs` ซึ่งเป็นโค้ดของเราเอง)
ก่อนหน้านี้เราเผลอใช้มันเป็นฐานเทียบ ทำให้สรุปผิดได้ เช่นเคยสรุปว่า `Platform_PC.UsePCUI => false`
เป็นค่าของ NEXON ทั้งที่อาจเป็นค่าที่เราแก้เอง — ชุดนี้มีไว้ตัดปัญหานั้น

**ใช้ชุดนี้เป็นฐานเทียบเสมอ ไม่ใช่ `extracted/`**

## สถานะการบิลด์

บิลด์ไม่ผ่าน 100% — เหลือ 4 จุดที่ decompiler เขียนกลับเป็น C# ไม่ได้ (9 error)
ทดสอบแล้วว่า **ILSpy 9.1 กับ 11.0 ให้ผลเท่ากันเป๊ะ** อัปเวอร์ชันไม่ช่วย:

| ปัญหา | จำนวน | สาเหตุ |
|---|---|---|
| `__ldftn` ไม่มีใน C# | 3 | function pointer เขียนเป็น C# ไม่ได้ |
| `CS0571` เรียก accessor ตรง (`NpcAIDog`, `StateBasedAI`) | 3 | แปลง `get_X()` กลับเป็น property ไม่สำเร็จ |
| `PetUtil` ตัวแปร `item` ชนกัน | 2 | **ชื่อตัวแปรท้องถิ่นไม่มีอยู่ใน IL** — ไม่มีเครื่องมือไหนกู้ได้ |

หมายเหตุการตั้งค่า csproj (ทำไว้ให้แล้ว):
- ต้องเติม `<Reference Include="UnityEngine">` เอง — ตัว generator ลืมใส่ (ไม่งั้นได้ CS0012 18 จุด)
- ตั้ง `<LangVersion>latest</LangVersion>` เพื่อตัด `CS9058` (async/iterator) ออก

## สิ่งที่ไม่มีวันกู้ได้ (ไม่ได้อยู่ใน DLL ตั้งแต่แรก)

คอมเมนต์ · ชื่อตัวแปรท้องถิ่น · `#region` · การจัดรูปแบบ · ค่าคงที่ที่ถูก inline
จะกู้ได้ต้องมีไฟล์ `.pdb` ซึ่งชุดที่แจกจริงไม่มี (ตรวจแล้วใน `Managed/`)

## วิธีถอดซ้ำ

ILSpy 11 ที่แจกเป็น zip ของ Windows **ไม่มี `ilspycmd.exe`** มีแต่ GUI
ให้เปิด `ILSpy.exe` แล้ว File → Save Code หรือเขียนตัวเรียก `ICSharpCode.Decompiler.dll` เอง
(`WholeProjectDecompiler.DecompileProject`) — ตัวที่ใช้ครั้งนี้อยู่ใน scratchpad ของ session
