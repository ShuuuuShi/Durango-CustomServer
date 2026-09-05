# ไฟล์ดูแลเซิร์ฟบน VPS

ของจริงที่ติดตั้งไว้แล้วบน `187.53.129.69` (5 ก.ย. 2026) — เก็บไว้ที่นี่เพื่อไม่ให้หายและ
สร้างใหม่ได้ถ้าต้องย้ายเครื่อง

| ไฟล์ | ติดตั้งไปที่ | ทำอะไร |
|---|---|---|
| `backup.sh` | `/opt/durango-lasthuman/backup.sh` | tar ไฟล์เซฟ เก็บย้อนหลัง 14 ชุด ลบชุดเก่าเอง |
| `durango-lasthuman-backup.service` | `/etc/systemd/system/` | ตัวเรียก `backup.sh` |
| `durango-lasthuman-backup.timer` | `/etc/systemd/system/` | ยิงวันละครั้ง ตี 4 (สุ่มถ่วง 5 นาที) · `Persistent=true` = เครื่องดับข้ามรอบแล้วตามเก็บให้ |
| `logrotate-durango-lasthuman` | `/etc/logrotate.d/durango-lasthuman` | หมุน log รายสัปดาห์ หรือเมื่อเกิน 200 MB เก็บ 8 รอบ |

## ทำไมต้องมี

* **backup** — ไฟล์เซฟอยู่ที่เดียวบนดิสก์เดียว ไฟล์เสีย/ดิสก์พัง/เผลอลบ = ตัวละครทุกคนหายถาวร
  (`.bak` ที่ `SafeSave` ทำให้ กันได้แค่ "ปิดกลางคันตอนเขียน" ไม่ได้กันเรื่องพวกนี้)
  ไม่ต้องหยุดเซิร์ฟตอนสำรอง เพราะ `SafeSave` เขียนแบบสลับเข้าที่ ⇒ `tar` อ่านได้ไฟล์สมบูรณ์เสมอ
* **logrotate** — เซิร์ฟเขียน log แบบ append ผ่าน systemd ไม่มีใครหมุนให้ โตจนดิสก์เต็มได้
  ใช้ `copytruncate` เพราะ systemd ถือ fd ค้างไว้ ถ้า rename เฉย ๆ มันจะเขียนลงไฟล์เก่าต่อ
  ⚠️ ตอนติดตั้งพบว่า **VPS ไม่มี logrotate เลย** ต้อง `apt-get install logrotate` ก่อน

## ติดตั้งใหม่ (ถ้าย้ายเครื่อง)

```sh
apt-get install -y logrotate
scp tools/vps/backup.sh root@<host>:/opt/durango-lasthuman/backup.sh
chmod +x /opt/durango-lasthuman/backup.sh
scp tools/vps/durango-lasthuman-backup.* root@<host>:/etc/systemd/system/
scp tools/vps/logrotate-durango-lasthuman root@<host>:/etc/logrotate.d/durango-lasthuman
systemctl daemon-reload
systemctl enable --now durango-lasthuman-backup.timer
```

## คำสั่งประจำ

```sh
systemctl list-timers durango-lasthuman-backup     # ดูว่ารอบหน้าเมื่อไร
systemctl start durango-lasthuman-backup.service   # สำรองเดี๋ยวนี้เลย
cat /var/log/durango-lasthuman-backup.log          # ดูผลการสำรอง
ls -la /opt/durango-lasthuman/backups/             # ดูชุดที่มี
```

กู้คืน: หยุดเซิร์ฟ → แตกไฟล์ทับ → เปิดใหม่

```sh
systemctl stop durango-lasthuman
tar xzf /opt/durango-lasthuman/backups/saves-<วันเวลา>.tar.gz -C /opt/durango-lasthuman/server
systemctl start durango-lasthuman
```
