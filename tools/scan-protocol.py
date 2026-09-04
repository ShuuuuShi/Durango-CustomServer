# -*- coding: utf-8 -*-
"""scan-protocol.py — สแกนว่า "ตัวเกมยิงอะไร / เซิร์ฟรับอะไรแล้ว / ขาดอะไร"

ทำไมต้องมี: เซิร์ฟนี้พอร์ตมาจากเซิร์ฟในตัวของเกม ซึ่งรับ message แค่ส่วนเดียวของ
โปรโตคอลเต็ม เวลาจะเติม gameplay จึงต้องรู้ก่อนว่า "ตัวเกมยิงอะไรมาบ้าง" ซึ่ง
อ่านจากซอร์สได้ตรง ๆ ไม่ต้องเดา:

  client/**/*.cs                  → เกมเรียก Send(new X{..}) / Send(default(X)) / Send<X>() ที่ไหน
  server/Core/*.cs                → เซิร์ฟลงทะเบียน Recv(delegate(X msg, PacketHeader h)) อะไรไว้แล้ว
  server/GameCode/Messages/*.cs   → TypeCode ของทุก message (985 ตัว พร้อม Pack/Unpack ครบ)

ผลลัพธ์คือ "รายการงานที่เหลือ" เรียงตามจำนวนจุดที่เกมเรียกจริง — อันที่เกมเรียกบ่อย
คืออันที่ผู้เล่นเจอเร็วที่สุด

รัน:  python tools/scan-protocol.py         # สรุปลงจอ
      python tools/scan-protocol.py --md    # เขียน docs/protocol-coverage.md ด้วย
"""
import os
import re
import io
import sys
import collections

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

ROOT = os.path.dirname(os.path.abspath(os.path.dirname(__file__)))
MSG_DIR = os.path.join(ROOT, 'server', 'GameCode', 'Messages')
SRV_DIR = os.path.join(ROOT, 'server', 'Core')
CLI_DIR = os.path.join(ROOT, 'client')
SEP = os.sep


def read(path):
    with io.open(path, encoding='utf-8', errors='replace') as f:
        return f.read()


def typecodes():
    """ชื่อ message -> TypeCode (อ่านจาก public const uint TypeCode = N)"""
    out = {}
    for name in os.listdir(MSG_DIR):
        if not name.endswith('.cs'):
            continue
        m = re.search(r'public\s+const\s+uint\s+TypeCode\s*=\s*(\d+)', read(os.path.join(MSG_DIR, name)))
        if m:
            out[name[:-3]] = int(m.group(1))
    return out


def server_handlers():
    """message ที่เซิร์ฟรับได้แล้ว — Recv(delegate(X msg, PacketHeader header))"""
    pat = re.compile(r'Recv\(\s*delegate\s*\(\s*(?:Messages\.)?([A-Za-z0-9_]+)\s+\w+\s*,')
    found = set()
    for base, _, files in os.walk(SRV_DIR):
        for name in files:
            if name.endswith('.cs'):
                found.update(pat.findall(read(os.path.join(base, name))))
    return found


def client_sends():
    """message ที่ตัวเกมยิงออกไป — Send(new X{..}) / Send(default(X)) / Send<X>()"""
    pats = [
        re.compile(r'Send\w*\(\s*new\s+(?:Messages\.)?([A-Z][A-Za-z0-9_]*)\s*[{(]'),
        re.compile(r'Send\w*\(\s*default\(\s*(?:Messages\.)?([A-Z][A-Za-z0-9_]*)\s*\)'),
        re.compile(r'Send\w*<\s*(?:Messages\.)?([A-Z][A-Za-z0-9_]*)\s*>\s*\('),
    ]
    hits = collections.Counter()
    where = collections.defaultdict(set)
    for base, dirs, files in os.walk(CLI_DIR):
        dirs[:] = [d for d in dirs if d not in ('bin', 'obj')]
        for name in files:
            if not name.endswith('.cs'):
                continue
            path = os.path.join(base, name)
            text = read(path)
            rel = os.path.relpath(path, ROOT).replace(SEP, '/')
            for pat in pats:
                for msg in pat.findall(text):
                    hits[msg] += 1
                    where[msg].add(rel)
    return hits, where


# กลุ่มระบบ — ใช้จัดหมวดให้อ่านง่าย (คำใน message name)
GROUPS = [
    (u'ต่อสู้/ล่า',      ('Damage', 'Damaged', 'Attack', 'Hunt', 'Combat', 'Battle', 'Dead', 'Revive', 'Respawn')),
    (u'สัตว์/เลี้ยง',    ('Animal', 'Pet', 'Tame', 'Domestic', 'Graze', 'Butcher', 'Herd')),
    (u'คราฟต์/สูตร',     ('Craft', 'Recipe', 'Blueprint', 'Make', 'Workbench', 'Produce')),
    (u'สกิล/เลเวล',      ('Skill', 'Level', 'Exp', 'Ability', 'Proficien', 'Talent', 'Stat')),
    (u'เอาชีวิตรอด',     ('Gauge', 'Hunger', 'Stamina', 'Sick', 'Wet', 'Temperature', 'Survival', 'Eat', 'Drink', 'Sleep')),
    (u'เควส',            ('Quest', 'Mission', 'Epic', 'Goal', 'Achievement', 'Attendance')),
    (u'ของ/กระเป๋า',     ('Item', 'Inventory', 'Storage', 'Equip', 'Dump', 'TakeOut', 'Dye', 'Cargo')),
    (u'สร้าง/ที่ดิน',    ('Artifact', 'Estate', 'Building', 'Floor', 'AddOn', 'Gate', 'Display', 'Modular')),
    (u'สังคม',           ('Clan', 'Party', 'Friend', 'Social', 'Mail', 'Chat', 'Say', 'Faction', 'Band', 'Message')),
    (u'ตลาด/เงิน',       ('Market', 'Product', 'Shop', 'Buy', 'Sell', 'Wallet', 'Money', 'Trade', 'Purchase')),
    (u'แผนที่/เดินทาง',  ('Chunk', 'Teleport', 'Travel', 'Warp', 'Map', 'Move', 'Region', 'Island', 'Explore')),
]


def group_of(name):
    for label, keys in GROUPS:
        for key in keys:
            if key.lower() in name.lower():
                return label
    return u'อื่น ๆ'


def main():
    codes = typecodes()
    srv = server_handlers()
    hits, where = client_sends()

    sends = dict((n, c) for n, c in hits.items() if n in codes)   # นับเฉพาะที่เป็น message จริง
    missing = sorted(set(sends) - srv, key=lambda n: -sends[n])
    handled = sorted(set(sends) & srv)

    print(u'message ทั้งหมดในโปรโตคอล : %d' % len(codes))
    print(u'เซิร์ฟรับได้ตอนนี้        : %d' % len(srv))
    print(u'ตัวเกมยิงออกมาจริง        : %d  (รับได้แล้ว %d | ยังไม่รับ %d)'
          % (len(sends), len(handled), len(missing)))
    print(u'')

    by_group = collections.defaultdict(list)
    for n in missing:
        by_group[group_of(n)].append(n)

    print(u'== ที่เกมยิงมาแต่เซิร์ฟยังไม่รับ (แยกตามระบบ) ==')
    for label, _ in GROUPS + [(u'อื่น ๆ', ())]:
        names = by_group.get(label)
        if not names:
            continue
        total = sum(sends[n] for n in names)
        print(u'')
        print(u'-- %s : %d message (เกมเรียกรวม %d จุด)' % (label, len(names), total))
        for n in names:
            print(u'   %-32s type=%-10d เรียก %-3d จุด   %s'
                  % (n, codes[n], sends[n], sorted(where[n])[0]))

    if '--md' in sys.argv:
        docs = os.path.join(ROOT, 'docs')
        if not os.path.isdir(docs):
            os.makedirs(docs)
        out = os.path.join(docs, 'protocol-coverage.md')
        with io.open(out, 'w', encoding='utf-8') as f:
            f.write(u'# โปรโตคอล — เกมยิงอะไร เซิร์ฟรับอะไรแล้ว\n\n')
            f.write(u'สร้างด้วย `python tools/scan-protocol.py --md` — อ่านจากซอร์สจริง ไม่ได้เดา\n\n')
            f.write(u'| | จำนวน |\n|---|---:|\n')
            f.write(u'| message ทั้งหมดในโปรโตคอล | %d |\n' % len(codes))
            f.write(u'| เซิร์ฟรับได้ตอนนี้ | %d |\n' % len(srv))
            f.write(u'| ตัวเกมยิงออกมาจริง | %d |\n' % len(sends))
            f.write(u'| ยังไม่มี handler | %d |\n\n' % len(missing))
            for label, _ in GROUPS + [(u'อื่น ๆ', ())]:
                names = by_group.get(label)
                if not names:
                    continue
                f.write(u'## %s\n\n' % label)
                f.write(u'| message | TypeCode | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |\n|---|---:|---:|---|\n')
                for n in names:
                    f.write(u'| `%s` | %d | %d | `%s` |\n' % (n, codes[n], sends[n], sorted(where[n])[0]))
                f.write(u'\n')
            f.write(u'## รับได้แล้ว (%d)\n\n' % len(handled))
            f.write(u', '.join(u'`%s`' % n for n in handled) + u'\n')
        print(u'\nเขียน %s แล้ว' % out)


main()
