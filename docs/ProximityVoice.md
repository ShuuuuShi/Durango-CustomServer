# Proximity Voice (แชทเสียงแบบ GTA)

พูดใกล้ได้ยินใกล้ ไกลแล้วจาง/หาย — ส่งเสียงผ่าน **UDP Voice Relay** แยกจาก packet เกม

## สิ่งที่ต้องมี

1. `game/Durango_Data/Managed/DurangoClientMods.dll` + `DurangoClientModSdk.dll` + `0Harmony.dll`
2. เกมที่เรียก `ClientModLoader.LoadAll()` ตอนบูต (LastHuman ใส่ hook ใน `GameManager.Start` แล้ว)
3. มอด `game/mods/ProximityVoice/ProximityVoiceMod.dll`
4. โปรเซส `VoiceRelay` เปิดอยู่ (พอร์ต UDP 8192)

## เปิด Voice Relay

```bash
dotnet run --project tools/VoiceRelay -c Release -- --bind 0.0.0.0 --port 8192 --secret lasthuman-voice
```

หรือตั้ง env `DURANGO_VOICE_SECRET`

## ติดตั้งมอด

```bash
dotnet build tools/ProximityVoiceMod -c Release
mkdir game/mods/ProximityVoice
copy tools/ProximityVoiceMod/bin/Release/net35/ProximityVoiceMod.dll game/mods/ProximityVoice/
```

อย่าวาง `DurangoClientModSdk.dll` ลงใน `mods/`

## ปุ่มควบคุม (ค่าเริ่มต้น)

| ปุ่ม | ทำอะไร |
|---|---|
| `V` (กดค้าง) | พูด (PTT) |
| `LeftAlt` + PTT | กระซิบ (~5m) |
| `LeftShift` + PTT | ตะโกน (~70m) |
| `B` | สลับโหมดปาร์ตี้ (คุยไกล ไม่จำกัดระยะ) |
| `N` | สลับโหมดวิทยุ |
| `M` | ปิด/เปิดไมค์ตัวเอง |
| `F8` | เปิด/ปิด HUD |

ระยะปกติ: เต็มเสียง ≤ 8m, จางถึง 35m แล้วหาย

## ตั้งค่า

แก้ `game/mods/ProximityVoice/voice.json` หรือ env:

- `DURANGO_VOICE_HOST`
- `DURANGO_VOICE_PORT`
- `DURANGO_VOICE_SECRET`
- `DURANGO_VOICE_ROOM`

ห้อง (`roomId`) ถ้าไม่ล็อกไว้ จะใช้ `GameManager.ClusterKey` / region อัตโนมัติ

## ตรวจว่าโหลดแล้ว

ดู `game/clientmods.log` ต้องมีประมาณ:

```text
[clientmods] โหลด 'ProximityVoice' v1.0.0 ... สำเร็จ (ครบ 3 เฟส)
```

## สถาปัตยกรรมสั้น ๆ

```text
ไมค์ → µ-law → UDP → VoiceRelay → UDP → AudioSource 3D (ลดดังตามระยะ)
```

ไม่ใช้ `PlayerVoice` ของเกม และไม่แตะ anti-cheat / MsgPack

## แอดมิน (เบื้องต้น)

ใน HUD กด `A-Mute` ที่ชื่อคน = ส่งคำสั่ง mute ไป relay (คนที่รู้ secret ของห้อง)

คำสั่ง relay รองรับ: `mute` / `unmute` / `deaf` / `undeaf` / `kick`
