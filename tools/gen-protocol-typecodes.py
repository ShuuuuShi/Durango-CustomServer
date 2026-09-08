# -*- coding: utf-8 -*-
import os, re, io
from collections import defaultdict
from datetime import datetime, timezone, timedelta

ROOT = r"C:\Users\thana\Desktop\Durango_LastHuman"
MSG = os.path.join(ROOT, "server", "GameCode", "Messages")
rows = []
for name in sorted(os.listdir(MSG)):
    if not name.endswith(".cs"):
        continue
    text = io.open(os.path.join(MSG, name), encoding="utf-8", errors="replace").read()
    m = re.search(r"public\s+const\s+uint\s+TypeCode\s*=\s*(\d+)", text)
    rows.append((name[:-3], int(m.group(1)) if m else None))

rows_ok = [(n, c) for n, c in rows if c is not None]
rows_miss = [n for n, c in rows if c is None]
byc = defaultdict(list)
for n, c in rows_ok:
    byc[c].append(n)
dups = {c: ns for c, ns in byc.items() if len(ns) > 1}
now = datetime.now(timezone(timedelta(hours=7))).strftime("%Y-%m-%d %H:%M ICT")

out = []
out.append("# รายการหมายเลขแพ็กเก็ตโปรโตคอล (TypeCode)")
out.append("")
out.append(f"อัปเดต: {now}")
out.append("แหล่งความจริง: `server/GameCode/Messages/*.cs` — **ห้ามแก้** โฟลเดอร์นี้ (โปรโตคอลคู่ client)")
out.append("สแกนด้วย regex `public const uint TypeCode = N`")
out.append("")
out.append("## สรุป")
out.append("")
out.append("| | จำนวน |")
out.append("|---|---:|")
out.append(f"| ไฟล์ `.cs` ใน Messages | {len(rows)} |")
out.append(f"| มี TypeCode | {len(rows_ok)} |")
out.append(f"| ไม่พบ TypeCode ในไฟล์ | {len(rows_miss)} |")
out.append(f"| TypeCode ซ้ำ (ผิดปกติ) | {len(dups)} |")
out.append("")
out.append("## คำเตือน")
out.append("")
out.append("- ตัวเลขเหล่านี้ต้องตรงกับ client เดิม — **ห้ามเปลี่ยน TypeCode / ห้ามลบ message / ห้ามสลับเลข**")
out.append("- งานเซิร์ฟใหม่ใช้เลขเดิมนี้เป็นสัญญา — implement handler อย่างเดียว อย่า invent เลขใหม่แทนของเดิม")
out.append("- coverage แยกอยู่ที่ `docs/protocol-coverage.md` — ไฟล์นี้เป็น**แคตตาล็อกเลขล้วน**")
out.append("- สร้างใหม่: `python tools/gen-protocol-typecodes.py`")
out.append("")
if dups:
    out.append("## TypeCode ซ้ำ")
    out.append("")
    for c in sorted(dups):
        out.append("- `{}` → {}".format(c, ", ".join("`"+n+"`" for n in dups[c])))
    out.append("")
if rows_miss:
    out.append("## ไฟล์ที่ไม่มี TypeCode")
    out.append("")
    for n in rows_miss:
        out.append(f"- `{n}.cs`")
    out.append("")
out.append("## ตารางทั้งหมด (เรียงตามชื่อ message)")
out.append("")
out.append("| Message | TypeCode |")
out.append("|---|---:|")
for n, c in sorted(rows_ok, key=lambda x: x[0].lower()):
    out.append(f"| `{n}` | {c} |")
out.append("")
out.append("## ตารางทั้งหมด (เรียงตาม TypeCode)")
out.append("")
out.append("| TypeCode | Message |")
out.append("|---:|---|")
for n, c in sorted(rows_ok, key=lambda x: (x[1], x[0].lower())):
    out.append(f"| {c} | `{n}` |")
out.append("")

path = os.path.join(ROOT, "docs", "PROTOCOL-TYPECODES.md")
io.open(path, "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")
print("wrote", path)
print("ok", len(rows_ok), "missing", len(rows_miss), "dups", len(dups))
