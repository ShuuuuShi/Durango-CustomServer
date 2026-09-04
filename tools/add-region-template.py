# -*- coding: utf-8 -*-
"""add-region-template.py — เพิ่ม region template ของเราเองลงข้อมูลเกม

ทำไมทำได้: ตัวเกมโหลดตาราง region template จากเซิร์ฟเรา ไม่ใช่จากในตัวเกม
(client/Yaml.Util/Loader.cs:85 → GET <gateway>/assets/region_templates เมื่อ cluster_mode = Online)
⇒ เกาะที่เรา generate เองจึงมี template ของตัวเองได้ ไม่ต้องยืมของ NEXON

ทำไมต้องมี: client ข้ามเกาะที่ไม่รู้จัก template ทิ้งทั้งกลุ่มแบบเงียบ ๆ
(client/ExploreSystem.cs:307 — SingletonDict<string, RegionTemplate>.Get(templateId) == null ⇒ ไม่แสดง)
เกาะที่โปรแกรมเจนสร้างจะตั้ง region_template เป็น "gen<seed>" ซึ่งไม่มีในตารางของเกม
⇒ เจนเกาะสวยแค่ไหนก็มองไม่เห็นในหน้าล่องเรือ

วิธีทำงาน: ก๊อป template ของเกมที่ใกล้เคียงที่สุดเป็นแม่แบบ (ได้ herds/biocoms/collectible_levels
ที่จูนมาแล้วทั้งชุด) แล้วเปลี่ยนเฉพาะ level / biome / ชื่อ — ค่าที่เหลือปล่อยตามต้นฉบับ
เพราะเราไม่มีข้อมูลที่ดีกว่านั้น และการเดาเองมีแต่ทำให้เกาะเสียสมดุล

รัน:
  python tools/add-region-template.py --id gen002020 --from ri50snSub01 --level 20
  python tools/add-region-template.py --list-templates        # ดูแม่แบบที่เลือกได้
  python tools/add-region-template.py --list-mine             # ดู template ที่เราเพิ่มไว้
"""
import argparse
import io
import json
import os
import shutil
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

ROOT = os.path.dirname(os.path.abspath(os.path.dirname(__file__)))
TEMPLATES = os.path.join(ROOT, 'server', 'data', 'assets', 'region_templates.json')

ROLE_NAMES = {0: 'Sandbox', 1: 'Tutorial', 3: 'Rural', 4: 'Risky',
              5: 'Outpost', 6: 'Urban', 7: 'Safehouse', 8: 'Instance', 9: 'Personal'}

# ธงบอกว่าเป็นของเราเพิ่มเอง ไม่ใช่ข้อมูลต้นฉบับของเกม — ใช้ตอน --list-mine และตอนไล่ปัญหา
MARK = '__added_by_lasthuman__'


def load():
    with io.open(TEMPLATES, encoding='utf-8') as f:
        return json.load(f)


def save(data):
    # สำรองของเดิมไว้เสมอ ไฟล์นี้เป็นข้อมูลเกมต้นฉบับ พลาดแล้วหาใหม่ยาก
    backup = TEMPLATES + '.bak'
    if not os.path.exists(backup):
        shutil.copy2(TEMPLATES, backup)
        print('สำรองต้นฉบับไว้ที่ %s' % backup)
    with io.open(TEMPLATES, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, separators=(',', ':'))


def list_templates(data, max_level):
    rows = []
    for key, value in data.items():
        if not value.get('active'):
            continue
        level = value.get('level') or 0
        if max_level and level > max_level:
            continue
        biome = None
        effects = value.get('biome_effects')
        if isinstance(effects, dict) and effects:
            biome = list(effects.keys())[0]
        rows.append((value.get('role'), level, key, biome))
    rows.sort(key=lambda r: (r[0] or 0, r[1], r[2]))
    print('%-10s %-5s %-22s %s' % ('role', 'level', 'template', 'biome'))
    print('-' * 62)
    for role, level, key, biome in rows:
        print('%-10s %-5s %-22s %s' % (ROLE_NAMES.get(role, role), level, key, biome or '-'))


def main():
    ap = argparse.ArgumentParser(description='เพิ่ม region template ของเราเองลงข้อมูลเกม')
    ap.add_argument('--id', help='ชื่อ template ใหม่ (ต้องตรงกับ region_template ใน info.yml ของเกาะ)')
    ap.add_argument('--from', dest='src', help='template ของเกมที่ใช้เป็นแม่แบบ')
    ap.add_argument('--level', type=int, help='ระดับเกาะ (ใช้จัดโซนในหน้าล่องเรือ)')
    ap.add_argument('--role', type=int, default=None, help='บทบาท (ปกติ 4 = Risky = เกาะสำรวจ)')
    ap.add_argument('--name', default=None, help='ชื่อที่โชว์ในเกม (ไม่ใส่ = ใช้ของแม่แบบ)')
    ap.add_argument('--list-templates', action='store_true', help='ดูแม่แบบที่เลือกได้')
    ap.add_argument('--max-level', type=int, default=0, help='กรองระดับตอน --list-templates')
    ap.add_argument('--list-mine', action='store_true', help='ดู template ที่เราเพิ่มไว้')
    args = ap.parse_args()

    data = load()

    if args.list_templates:
        list_templates(data, args.max_level)
        return
    if args.list_mine:
        mine = [k for k, v in data.items() if isinstance(v, dict) and v.get(MARK)]
        print('template ที่เราเพิ่มเอง %d รายการ: %s' % (len(mine), ', '.join(mine) or '(ยังไม่มี)'))
        return

    if not (args.id and args.src and args.level):
        ap.error('ต้องระบุ --id, --from และ --level (หรือใช้ --list-templates)')
    if args.src not in data:
        ap.error('ไม่พบแม่แบบ %r — ดูรายชื่อด้วย --list-templates' % args.src)

    template = json.loads(json.dumps(data[args.src]))   # deep copy
    template['level'] = args.level
    if args.role is not None:
        template['role'] = args.role
    if args.name is not None:
        template['name'] = {args.name: None}
    template['active'] = True
    template[MARK] = True

    existed = args.id in data
    data[args.id] = template
    save(data)

    biome = None
    effects = template.get('biome_effects')
    if isinstance(effects, dict) and effects:
        biome = list(effects.keys())[0]
    print('%s template %r' % ('อัปเดต' if existed else 'เพิ่ม', args.id))
    print('  แม่แบบ : %s' % args.src)
    print('  ระดับ  : %s   บทบาท: %s   ไบโอม: %s'
          % (template['level'], ROLE_NAMES.get(template.get('role'), template.get('role')), biome or '-'))
    print('')
    print('ขั้นต่อไป: รีสตาร์ตเซิร์ฟแล้วดูล็อก [region] ว่าเกาะเข้าสารบัญหรือยัง')


main()
