# รายการแพ็กเกจ / Dependencies — Durango_LastHuman

อัปเดต: 2026-09-09 (Asia/Bangkok)  
สโคป: สแกนจาก `.csproj` ใน repo + `dotnet list package` (เซิร์ฟ) + DLL อ้างอิงฝั่งเกม/มอด  
**ไม่รวม** `bin/` `obj/` และแพ็กเกจภายในที่ NuGet ดึงมาเองแบบ transitive นอกจากที่ระบุ

---

## สรุปสั้นๆ

| โปรเจกต์ | TFM | NuGet (PackageReference) | อื่นๆ |
|---|---|---|---|
| `server/DurangoServer.csproj` | `net9.0` | **3** แพ็กเกจ | assets/locales/admin คัดลอกตอน build |
| `tools/VoiceRelay/VoiceRelay.csproj` | `net9.0` | **ไม่มี** (BCL อย่างเดียว) | — |
| `tools/MemoryBotMod/MemoryBotMod.csproj` | `net35` | ไม่มี | Reference DLL จาก `game/Durango_Data/Managed/` |
| `tools/ProximityVoiceMod/ProximityVoiceMod.csproj` | `net35` | ไม่มี | Reference DLL จาก Managed |
| `client/Assembly-CSharp.csproj` | `net35` | ไม่มี | Reference Unity + third-party ใน Managed (~75 DLL ทั้งโฟลเดอร์) |

---

## 1. เซิร์ฟเวอร์ — NuGet (`DurangoServer`)

ไฟล์: `server/DurangoServer.csproj`  
คำสั่งตรวจ: `dotnet list server/DurangoServer.csproj package`

| แพ็กเกจ | เวอร์ชันที่ขอ | เวอร์ชันที่ resolve | ใช้ทำอะไร (โดยย่อ) |
|---|---|---|---|
| [MsgPack.Cli](https://www.nuget.org/packages/MsgPack.Cli) | 1.0.1 | 1.0.1 | ซีเรียลไลซ์ MessagePack ตามโปรโตคอลเกม |
| [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json) | 13.0.3 | 13.0.3 | JSON (HTTP gateway, config, assets) |
| [Snappier](https://www.nuget.org/packages/Snappier) | 1.3.1 | 1.3.1 | บีบอัด Snappy (คู่กับฝั่งเกม `snappy.net`) |

ไม่มี `Directory.Packages.props` / central package management ใน repo นี้

---

## 2. VoiceRelay — ไม่มี NuGet

ไฟล์: `tools/VoiceRelay/VoiceRelay.csproj`  
- `net9.0` exe  
- ไม่มี `PackageReference` — พึ่ง .NET BCL

---

## 3. มอด client (net35) — DLL จากเกม ไม่ใช่ NuGet

### 3.1 MemoryBotMod

| Reference | HintPath |
|---|---|
| DurangoClientModSdk | `game/Durango_Data/Managed/DurangoClientModSdk.dll` |
| Assembly-CSharp | `game/Durango_Data/Managed/Assembly-CSharp.dll` |
| UnityEngine | `…/UnityEngine.dll` |
| UnityEngine.CoreModule | `…/UnityEngine.CoreModule.dll` |
| UnityEngine.JSONSerializeModule | `…/UnityEngine.JSONSerializeModule.dll` |
| UnityEngine.ImageConversionModule | `…/UnityEngine.ImageConversionModule.dll` |
| UnityEngine.ScreenCaptureModule | `…/UnityEngine.ScreenCaptureModule.dll` |
| UnityEngine.IMGUIModule | `…/UnityEngine.IMGUIModule.dll` |

### 3.2 ProximityVoiceMod

| Reference | HintPath |
|---|---|
| DurangoClientModSdk | `game/Durango_Data/Managed/DurangoClientModSdk.dll` |
| Assembly-CSharp | `…/Assembly-CSharp.dll` |
| UnityEngine (+ Core / Audio / Physics / IMGUI modules) | ใต้ `game/Durango_Data/Managed/` |

---

## 4. Client `Assembly-CSharp` — third-party ที่ไม่ใช่ Unity module

อ้างอิงจาก `client/Assembly-CSharp.csproj` (ไม่ใช่ PackageReference):

| Assembly | หมายเหตุ |
|---|---|
| NCalc | สูตรในเกม |
| MsgPack | MessagePack ฝั่ง client (คู่ MsgPack.Cli ฝั่งเซิร์ฟ) |
| snappy.net | Snappy ฝั่ง client (คู่ Snappier) |
| ICSharpCode.SharpZipLib | zip |
| Vectrosity | วาดเส้น/เวกเตอร์ |
| Wwise | เสียง |
| ExternalLibrary | ไลบรารีภายนอกที่มากับเกม |
| Debug | assembly ของเกม |
| Assembly-CSharp-firstpass | Unity firstpass |
| UnityEngine.* / UnityEngine.UI | เอนจิน (หลายโมดูล) |

โฟลเดอร์ `game/Durango_Data/Managed/` มี DLL รวมประมาณ **75** ไฟล์ (นับตอนเขียนเอกสาร) — ส่วนใหญ่เป็น Unity modules + เกม ไม่ได้มาจาก NuGet ของ repo

---

## 5. สิ่งที่ไม่ได้เป็น NuGet แต่มากับเซิร์ฟตอน build

จาก `DurangoServer.csproj` (CopyToOutputDirectory):

- `server/data/locales/**/*.mo`
- `server/data/terrains/*.zip`
- `server/data/assets/**/*.json`
- `server/admin/**`

---

## 6. วิธีอัปเดตรายการนี้

```powershell
cd C:\Users\thana\Desktop\Durango_LastHuman
dotnet list server\DurangoServer.csproj package
# แก้อัปเดตตารางใน docs/PACKAGES.md ให้ตรง Resolved
```

ถ้าเพิ่ม `PackageReference` ใหม่ในเซิร์ฟหรือเครื่องมือ — อัปเดตเอกสารนี้ใน PR เดียวกัน

---

## 7. หมายเหตุสำหรับ agent / ผู้ดูแล

- เซิร์ฟวงปิดพึ่ง NuGet จริงๆ แค่ **3** ตัวด้านบน  
- มอดและ client พึ่ง DLL ใน `game/` — อย่าคาดหวัง `dotnet restore` จะดึง Unity ให้  
- อย่า commit `bin/` `obj/` หรือแพ็กเกจที่ restore แล้วทับ repo
