#!/bin/sh
# สำรองไฟล์เซฟของ Durango LastHuman — เรียกจาก systemd timer วันละครั้ง
#
# ทำไมต้องมี: ไฟล์เซฟอยู่ที่เดียวบนดิสก์เดียว ถ้าไฟล์เสีย/ดิสก์พัง/เผลอลบ = ตัวละครทุกคนหายถาวร
# เซิร์ฟมี .bak คู่ทุกไฟล์อยู่แล้ว แต่มันกันแค่ "ปิดกลางคันตอนเขียน" ไม่ได้กันเรื่องพวกนี้
#
# ไม่ต้องหยุดเซิร์ฟ: SafeSave เขียนแบบสลับเข้าที่ (เขียนไฟล์ชั่วคราวแล้ว rename ทับ)
# ⇒ tar อ่านได้ไฟล์ที่สมบูรณ์เสมอ ไม่เจอไฟล์ครึ่ง ๆ กลาง ๆ

SRC=/opt/durango-lasthuman/server/AppData-nx
DEST=/opt/durango-lasthuman/backups
KEEP=14

[ -d "$SRC" ] || { echo "ไม่พบ $SRC — ข้าม"; exit 0; }
mkdir -p "$DEST"

STAMP=$(date +%Y%m%d-%H%M)
FILE="$DEST/saves-$STAMP.tar.gz"

tar czf "$FILE" -C /opt/durango-lasthuman/server AppData-nx || {
    echo "สำรองไม่สำเร็จ"; rm -f "$FILE"; exit 1
}

SIZE=$(du -h "$FILE" | cut -f1)
echo "สำรองแล้ว $FILE ($SIZE)"

# เก็บย้อนหลัง $KEEP ชุด ที่เหลือลบทิ้ง (ไม่งั้นดิสก์เต็มในอีกไม่กี่เดือน)
ls -1t "$DEST"/saves-*.tar.gz 2>/dev/null | tail -n +$((KEEP + 1)) | while read old; do
    echo "ลบชุดเก่า $(basename "$old")"
    rm -f "$old"
done
