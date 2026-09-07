# Durango LastHuman

เซิร์ฟเวอร์ส่วนตัวของเกม **Durango: Wild Lands** สร้างจากโค้ดเซิร์ฟที่ NEXON ฝังมาในตัวเกมเอง
พอร์ตขึ้น .NET 9 แล้วเปลี่ยนชื่อ `Durango.Offline` → **`Durango.Online`** เพราะโปรเจกต์นี้ทำเวอร์ชันออนไลน์เท่านั้น

## โครงสร้าง

```
server/          เซิร์ฟ .NET 9
  Core/          ตัวเซิร์ฟ (Host, GameServer, Gateway, World, Player, ...)
  Support/       shim แทนของที่อยู่ใน Unity (Yaml/prototype/สี/แปลภาษา)
  GameCode/      ชั้น protocol + data ของเกม (Messages/ 852 ตัว TypeCode ตรงต้นฉบับ 100%)
  Shims/         UnityEngine shim (Mathf/Vector/Random/Debug)
  data/          terrain 14 แผนที่ · assets (prototype/recipe/artifact/pet) · config.json
client/          ซอร์สเกม (Assembly-CSharp) ของ NEXON แท้ 3,755 ไฟล์
game/            ตัวเกมที่เล่นได้จริง (ไม่อยู่ใน git — ก๊อปมาจากชุดแจก)
tools/           สคริปต์ build / เปิดเซิร์ฟ / สแกนโปรโตคอล
docs/           ROADMAP.md · ROADMAP-NEXT.md · TODO.md · NEXON-SERVER-ARCHITECTURE.md · ITEM-SYSTEM-MAP.md · protocol-coverage.md
```

## เริ่มใช้งาน

ดับเบิลคลิก **`เปิดเซิร์ฟ.bat`** แล้วเลือกจากเมนู:

| เมนู | ทำอะไร |
|---|---|
| 1 | เปิดเซิร์ฟ (gateway 8190 / เกม 8191) |
| 2 | build ใหม่แล้วเปิด |
| 3 | หยุดเซิร์ฟ |
| 4 | selftest — เช็ค handshake (ต้องเปิดเซิร์ฟก่อน) |
| 5 | เปิดเกม |
| 6 | ดู log ล่าสุด |

หรือสั่งเองจากบรรทัดคำสั่ง:

```bash
dotnet build server -c Release
dotnet build client -c Release
powershell -File tools\build-client.ps1     # build ซอร์สเกมแล้ววาง DLL ลง game\
```

## พอร์ต

| พอร์ต | ใช้ทำอะไร |
|---|---|
| 8190 | HTTP gateway — `/knock` `/sessions` `/entry` `/players` `/terrains` |
| 8191 | TCP game — handshake `GetClock` → `Auth` → `Ready` แล้วเข้าโลก |

`game/server.txt` ต้องชี้ `127.0.0.1:8190` (ค่าเริ่มต้นตรงอยู่แล้ว)

ถ้าจะให้เครื่องอื่น (มือถือ/เพื่อนใน LAN) เข้าได้ ต้องรันครั้งเดียวแบบ **Run as administrator**:

```bash
netsh http add urlacl url=http://*:8190/ user=Everyone
```

ไม่งั้นเซิร์ฟจะตกไปฟังแค่ loopback (log บอกเอง: `wildcard bind denied, falling back to loopback`)

## รู้ว่าเซิร์ฟยังขาดอะไร

```bash
python tools/scan-protocol.py          # สรุปลงจอ แยกตามระบบ
python tools/scan-protocol.py --md     # เขียน docs/protocol-coverage.md
```

สคริปต์อ่านจากซอร์สจริง: `Send(new X{..})` ในซอร์สเกม เทียบกับ `Recv(delegate(X msg, ..))` ในเซิร์ฟ
ตอนเล่นจริงเซิร์ฟก็พิมพ์บอกเองด้วย — `[conn] ไม่มี handler สำหรับ type=NNNN`
เอาเลขไปหาชื่อ: `grep -l "TypeCode = NNNN" server/GameCode/Messages/*.cs`

## สถานะ

- เซิร์ฟ build ผ่าน · selftest ผ่าน (handshake + entity + chunk streaming)
- ซอร์สเกม build ผ่าน · เกมเปิดได้
- รับ message ได้ **42 ชนิด** จากที่เกมยิงออกมา **391** — งานต่อไปดู [docs/ROADMAP.md](docs/ROADMAP.md)
- งานถัดไปที่ตกลงกันไว้: **ระบบล่องเรือ/หมู่เกาะ** (ต้องรื้อให้เซิร์ฟรองรับหลาย region ก่อน)

## แผนงานล่าสุด

- รายการติ๊ก: [docs/TODO.md](docs/TODO.md)
- แผนเฟสถัดไป: [docs/ROADMAP-NEXT.md](docs/ROADMAP-NEXT.md)
- สถาปัตยกรรม Nexon (อ้างอิง): [docs/NEXON-SERVER-ARCHITECTURE.md](docs/NEXON-SERVER-ARCHITECTURE.md)
- ประวัติ/รายละเอียดเดิม: [docs/ROADMAP.md](docs/ROADMAP.md)
- แผนที่ระบบไอเทม/เลเวล/แอต/บัฟ: [docs/ITEM-SYSTEM-MAP.md](docs/ITEM-SYSTEM-MAP.md)

