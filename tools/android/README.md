# เครื่องมือ build ตัวเกมสำหรับ Android

สร้าง APK ที่ชี้ไปเซิร์ฟของเรา จากซอร์สใน `client\` ชุดเดียวกับที่ใช้บน PC

```powershell
powershell -File tools\android\build-android.ps1                          # ชี้ VPS (ค่าเริ่มต้น)
powershell -File tools\android\build-android.ps1 -VpsHost 1.2.3.4 -GatewayPort 8890
powershell -File tools\android\build-android.ps1 -Local -Install          # เทสกับเซิร์ฟในเครื่อง
```

ผลลัพธ์ออกที่ `dist\android\DurangoLastHuman-<host>-<port>.apk`

> **ชุด Android แยกโฟลเดอร์กับ PC**
> `dist\android\` APK · `dist\pc\` ชุด PC · `dist\vps\` ชุดเซิร์ฟ
>
> คนละแพลตฟอร์มคนละรอบปล่อย — เดิมกองรวมกันใน `dist\` จนแยกไม่ออกว่าไฟล์ไหนของใคร

---

## ทำงานยังไง

| ขั้น | ทำอะไร |
|---|---|
| 1 | ฝังที่อยู่เซิร์ฟลง `client\Durango.System\BakedCluster.cs` แล้ว build `Assembly-CSharp.dll` (net35) |
| 2 | คืน `BakedCluster.cs` เป็นค่าว่าง — **ทำใน `finally` จึงคืนให้เสมอแม้ build ล้มกลางคัน** |
| 3 | เอา DLL วางแทนใน APK ฐาน → `zipalign -p 4` → `apksigner` (v1+v2) |

### ทำไมไม่ build จาก Unity

ซอร์สใน `client\` เป็น **C# 11** ที่ Roslyn คอมไพล์ แต่ Unity 2017 ใช้ `mcs` (C# 4) คอมไพล์ไม่ผ่าน
⇒ ใช้ APK ที่ Unity build ไว้แล้วเป็นฐาน แล้วสลับเฉพาะ `Assembly-CSharp.dll`
IL ที่ Roslyn ออกมาเป็น net35 รันบน Mono ตัวเดียวกับที่ใช้บน PC อยู่ทุกวัน

### ทำไมต้องฝังที่อยู่ (ไม่ใช้ clusters.json เหมือน PC)

บน PC วาง `clusters.json` ข้าง `Durango.exe` ได้เลย แต่บน Android ไฟล์นั้นต้องไปอยู่ที่
`Application.persistentDataPath` ซึ่งอยู่ในพื้นที่ส่วนตัวของแอป — **ผู้เล่นวางเองไม่ได้ก่อนเปิดเกมครั้งแรก**

ลำดับที่ตัวเกมใช้ (`TitleMenuGroup` · `State.GetClusterList`):

1. `clusters.json` ที่วางไว้เอง — PC หรือ Android ที่ `adb push` เข้าไปได้
2. `BakedCluster.Json` ที่ฝังตอน build — **Android ปกติใช้ทางนี้**
3. TextAsset `offline/clusters` ในเกม — ของ NEXON เดิม

---

## สิ่งที่ต้องมีในเครื่อง

| | ตรวจด้วย |
|---|---|
| Android SDK build-tools (`zipalign` · `apksigner`) | หาเองจาก `%LOCALAPPDATA%\Android\Sdk\build-tools` (เอาเวอร์ชันใหม่สุด) |
| JDK (`keytool`) | ใช้ครั้งแรกครั้งเดียวตอนสร้าง keystore |
| Python 3 | รัน `swap-managed.py` |
| **APK ฐานแบบ Mono** | ต้องวางเองที่ `tools\android\base\base-mono.apk` |

### APK ฐาน — สำคัญที่สุด

ต้องเป็น APK ที่ Unity build แบบ **Mono** คือมี `assets/bin/Data/Managed/Assembly-CSharp.dll`
เป็นไฟล์ธรรมดา (เหมือนฝั่ง PC) ⇒ สลับ DLL ได้ตรง ๆ

⚠️ **APK ต้นฉบับของ NEXON เป็น IL2CPP** — ไม่มี `Managed/*.dll` เลยสักไฟล์ สลับแบบนี้ไม่ได้
ต้องไปแพตช์ `global-metadata.dat` แทน ซึ่งเป็นคนละเรื่องคนละเครื่องมือ
`swap-managed.py` ตรวจให้และหยุดพร้อมบอกเหตุผลทั้งสองกรณี

ไฟล์ฐานกับ keystore **ไม่ได้เข้า git** (ใหญ่หลักร้อย MB และเป็นความลับ) — ดู `.gitignore`

---

## keystore

สร้างอัตโนมัติครั้งแรกที่ `tools\android\keys\durangoth.keystore`

⚠️ **เก็บให้ดี ห้ามหาย** — Android ให้อัปเดตทับได้เฉพาะ APK ที่เซ็นด้วยคีย์เดียวกัน
เปลี่ยนคีย์ = ผู้เล่นต้องถอนแล้วลงใหม่ ⇒ ข้อมูลในเครื่องหายหมด **รวม `account.key`
ซึ่งแปลว่าเข้าตัวละครเดิมไม่ได้อีก** (ดู `client\Durango.System\DeviceAccount.cs`)

---

## เทสกับเซิร์ฟในเครื่อง

```powershell
powershell -File tools\android\build-android.ps1 -Local -Install
adb reverse tcp:8890 tcp:8890     # gateway
adb reverse tcp:8891 tcp:8891     # game
```

`adb reverse` ต่อพอร์ตของมือถือกลับมาที่เครื่อง PC — ต้องสั่งใหม่ทุกครั้งที่เสียบสายใหม่

## ปัญหาที่เจอบ่อย

| อาการ | สาเหตุ |
|---|---|
| `adb install` ขึ้น `INSTALL_FAILED_UPDATE_INCOMPATIBLE` | เคยลงด้วยคีย์อื่น → `adb uninstall com.nexon.durango.global` |
| เข้าเกมแล้วค้างหน้า Title | ที่อยู่เซิร์ฟผิด หรือเซิร์ฟไม่ได้เปิดพอร์ตให้ภายนอก — ลอง `curl http://<host>:<port>/knock?platform=Android` |
| ขึ้น 401 ตอนเข้าเกม | เซิร์ฟรุ่นใหม่บังคับมีกุญแจบัญชี แต่ APK เป็นรุ่นเก่า → build ใหม่ |
