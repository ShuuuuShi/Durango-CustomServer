#!/bin/sh
# test-accounts.sh — พิสูจน์ว่าช่องโหว่ระบบบัญชีที่ audit เจอถูกปิดแล้วจริง
#
# ยิง HTTP ตรง ๆ แบบเดียวกับที่คนโกงจะทำ ไม่ผ่านตัวเกม
#
#   sh tools/test-accounts.sh [http://127.0.0.1:8190]
#
# ⚠️ เครื่องมือทดสอบ — รันกับเซิร์ฟทดสอบเท่านั้น (สร้างตัวละครทิ้งไว้ ต้องลบเอง)
#
# ⚠️ เซิร์ฟต้องรันแบบ detached (Start-Process) ไม่ใช่ `./DurangoServer.exe &` ใน shell
#    ไม่งั้นมันตายตามตอนคำสั่งจบ แล้วจะดูเหมือนเกม "ค้างหน้าโหลด" ทั้งที่เป็นเพราะเซิร์ฟหายไป

GW="${1:-http://127.0.0.1:8190}"
A="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"   # กุญแจบัญชีของ "ผู้เล่น A"
B="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"   # กุญแจบัญชีของ "ผู้เล่น B" (คนที่จะมาแอบ)

pass=0
fail=0
ok()   { echo "  [ผ่าน]    $1"; pass=$((pass+1)); }
bad()  { echo "  [ไม่ผ่าน] $1"; fail=$((fail+1)); }

echo "=== เทสระบบบัญชี ที่ $GW ==="
echo

# ── 1. ไม่มีกุญแจบัญชี = ขอ session ไม่ได้ ────────────────────────────────────
echo "1) ขอ session โดยไม่ส่ง account_id (ตัวเกมรุ่นเก่า / สคริปต์ดิบ)"
# ⚠️ ต้องมี body เสมอ — HTTP.sys ตอบ 411 Length Required ก่อนถึงโค้ดเราถ้า POST ตัวเปล่า
code=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$GW/sessions" -d "platform=Windows")
if [ "$code" = "401" ]; then ok "ถูกปฏิเสธ (401)"; else bad "ได้ $code — ควรเป็น 401"; fi
echo

# ── 2. บัญชี A สร้างตัวละคร ───────────────────────────────────────────────────
echo "2) บัญชี A ขอ session แล้วสร้างตัวละคร"
sa=$(curl -s -X POST "$GW/sessions" -d "account_id=$A")
ta=$(echo "$sa" | sed -n 's/.*"session_token": *"\([^"]*\)".*/\1/p')
if [ -n "$ta" ]; then ok "ได้ token ของ A"; else bad "ขอ session ไม่ได้: $sa"; fi

pa=$(curl -s -X POST "$GW/players" -H "Authorization: $ta" \
     -d "name=ผู้เล่นA&job=0&gender=male")
ea=$(echo "$pa" | sed -n 's/.*"entity_id": *"\([^"]*\)".*/\1/p')
if [ -n "$ea" ]; then ok "สร้างตัวละคร A แล้ว ($ea)"; else bad "สร้างไม่ได้: $pa"; echo "  (เทสที่เหลือพึ่งค่านี้ — หยุด)"; exit 1; fi
echo

# ── 3. บัญชี B ต้องไม่เห็นตัวละครของ A ───────────────────────────────────────
echo "3) บัญชี B ขอรายชื่อตัวละคร — ต้องไม่เห็นของ A"
ab=$(curl -s -X POST "$GW/accounts" -d "account_id=$B")
if echo "$ab" | grep -q "$ea"; then
    bad "B เห็นตัวละครของ A ในรายการ!  $ab"
else
    ok "B ไม่เห็นตัวละครของ A"
fi

aa=$(curl -s -X POST "$GW/accounts" -d "account_id=$A")
if echo "$aa" | grep -q "$ea"; then ok "A เห็นตัวละครตัวเอง"; else bad "A ไม่เห็นตัวเอง!  $aa"; fi
echo

# ── 4. ไม่มีกุญแจ = ไม่เห็นอะไรเลย ───────────────────────────────────────────
echo "4) ขอรายชื่อโดยไม่ส่ง account_id"
an=$(curl -s -X POST "$GW/accounts" -d "platform=Windows")
if echo "$an" | grep -q "$ea"; then bad "เห็นตัวละครโดยไม่ต้องมีบัญชี!  $an"; else ok "ไม่เห็นอะไรเลย"; fi
echo

# ── 5. การโจมตีหลักจาก audit: B ยึดตัวละครของ A ผ่าน /entry ──────────────────
echo "5) B เอา token ตัวเอง ยิง /entry ชี้ไปที่ตัวละครของ A (curl 2 บรรทัดยึดตัวละคร)"
sb=$(curl -s -X POST "$GW/sessions" -d "account_id=$B")
tb=$(echo "$sb" | sed -n 's/.*"session_token": *"\([^"]*\)".*/\1/p')
curl -s -o /dev/null "$GW/entry?entity_id=$ea" -H "Authorization: $tb"
# ถ้าผูกสำเร็จ เซิร์ฟจะพิมพ์ "ผูก session เข้ากับตัวละคร" — เช็คจากฝั่ง log
# ที่นี่เช็คทางอ้อม: ขอ session ใหม่ด้วย player ที่อ้าง entity ของ A
sc=$(curl -s -X POST "$GW/sessions" -d "account_id=$B" \
     --data-urlencode "player={\"player_info\":{\"player_entity_id\":\"$ea\"}}")
uc=$(echo "$sc" | sed -n 's/.*"user_id": *"\([^"]*\)".*/\1/p')
if [ -n "$uc" ] && [ "$uc" = "$ea" ]; then
    bad "B ขอ session ที่ผูกกับตัวละครของ A ได้!"
else
    ok "B ขอ session สวมตัวละคร A ไม่ได้ (ได้ $uc แทน)"
fi
echo

# ── 6. /players ไม่มี token = เขียนทับตัวละครสล็อตแรกไม่ได้ ──────────────────
echo "6) ยิง POST /players โดยไม่มี Authorization"
code=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$GW/players" -d "name=โดนแฮก&job=0&gender=male")
if [ "$code" = "401" ]; then ok "ถูกปฏิเสธ (401)"; else bad "ได้ $code — ควรเป็น 401"; fi
echo

# ── 7. ตัวละครกำพร้า (เซฟเก่าที่ไม่มี owner_key) ต้องมองไม่เห็น ──────────────
echo "7) ตัวละครกำพร้าจากเซฟเก่า — ต้องไม่โผล่ให้ใครเห็น (ถ้าไม่ได้เปิด --adopt-orphans)"
orphan=$(curl -s -X POST "$GW/accounts" -d "account_id=$A" | grep -o 'SkillTestFarmer')
if [ -n "$orphan" ]; then
    bad "บัญชี A เห็นตัวละครกำพร้า — เซิร์ฟเปิด --adopt-orphans อยู่หรือเปล่า?"
else
    ok "ตัวละครกำพร้าไม่โผล่"
fi
echo

echo "═══ ผ่าน $pass · ไม่ผ่าน $fail ═══"
[ "$fail" -eq 0 ]
