# -*- coding: utf-8 -*-
"""derive-workbench-tags.py — สร้างตาราง "โต๊ะตัวไหนให้แท็กอะไร"

═══ ปัญหา ═══
สูตรคราฟต์ 587 จาก 720 สูตรต้องมีโต๊ะ โดยระบุเป็น `workbench_tags: {แท็ก: ระดับ}`
ฝั่งเกมเช็คด้วย `workbench.GetTag(id).Level >= ระดับ` (client/Crafting/Recipe.cs:59-68)
และ `Artifact.Tags` มีที่มาเดียวคือเซิร์ฟส่ง `AppearArtifact.Tags` มาให้

**แต่ในข้อมูลเกมที่เรามี ไม่มีไฟล์ไหนบอกว่าโต๊ะตัวไหนให้แท็กอะไร** — ตรวจครบแล้ว:
  • entity_types/artifact.json  ไม่มีฟิลด์ที่มีคำว่า tag เลยสักตัว (มีแต่ components ที่เป็นรายชื่อล้วน)
  • building/blueprints.json    มีแค่ tool_tags = "ใช้เครื่องมืออะไรสร้าง" ไม่ใช่ "สร้างเสร็จให้แท็กอะไร"
  • performance.json → workbench มีแค่ craft_capacity / entrust_*
  • tags.json                   นิยามแท็ก 1,146 ตัว แต่ไม่ได้ชี้กลับมาที่ artifact
  • grep หาแท็ก "kiln" ทั้ง data/assets เจอแค่ 2 ที่: recipes.json (ฝั่งขอ) กับ tags.json (ฝั่งนิยาม)
⇒ ตารางนี้อยู่บนเซิร์ฟจริงของ NEXON ซึ่งไม่มีซอร์ส **ต้องสร้างขึ้นเอง**

═══ ตารางนี้เป็นของเรา ไม่ใช่ของ NEXON ═══
แต่ไม่ได้เดาลอย ๆ — อิงหลักฐาน 2 ชั้นที่ตรวจสอบย้อนได้:
  1. **ชื่อภายในของ artifact** (`__name__`) เช่น kiln_04 · loom_02 · weapon_table_01
     ชื่อพวกนี้ใช้คำเดียวกับ id ของแท็กตรง ๆ
  2. **ไอคอนของแท็กใน tags.json** ซึ่งชี้ไปที่โมเดล artifact เช่น
     tag "cook" → icon "furniture_workbench_bonfire" ⇒ เตาไฟ
     tag "alcohol_ripen" → icon "warp_oak_01"        ⇒ ถังไม้โอ๊ก (barrel_oak_01)
     tag "cook_filter"   → icon "warp_dutch_01"      ⇒ กาแฟดริป (coffee_dutch_01)

ทุกแถวกำกับความมั่นใจไว้:
  แน่  = ชื่อ artifact กับ id แท็กตรงกันตรง ๆ หรือไอคอนชี้ชัด
  เดา  = อนุมานจากบริบท — **ตรงนี้แก้ได้เลยถ้าเจอข้อมูลจริงทีหลัง**

ระดับที่ให้: ใช้ max_level ของ blueprint ตัวนั้น (ปกติ 60) เพราะเซิร์ฟยังไม่ได้เก็บ
"เลเวลของสิ่งปลูกสร้างแต่ละหลัง" ⇒ ถ้าให้ระดับต่ำไว้ก่อน สูตรระดับสูงจะคราฟต์ไม่ได้
ทั้งที่ผู้เล่นสร้างโต๊ะถูกตัวแล้ว ซึ่งดูเหมือนบั๊กมากกว่าเป็นการจำกัด

รัน:
    python tools/derive-workbench-tags.py
    python tools/derive-workbench-tags.py --check     # ดูว่าสูตรไหนยังไม่มีโต๊ะรองรับ
"""
import argparse
import io
import json
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ASSETS = os.path.join(ROOT, 'server', 'data', 'assets')
OUT = os.path.join(ASSETS, 'derived', 'workbench_tags.json')

# แท็ก → (รายการคำที่ต้องมีในชื่อ artifact, ความมั่นใจ, เหตุผล)
# เรียงจากแท็กที่สูตรเรียกหาบ่อยสุดลงมา
RULES = [
    ('table', ['fur_table', 'worktable', 'closet_table', 'kitchen_table', 'allround',
               'heavy_tech', 'light_tech', 'living_tech'],
     'แน่', 'ชื่อมีคำว่า table ตรง ๆ · icon ของแท็กคือ furniture_fur_table_01 · '
            '*_tech เป็นโต๊ะช่างชุดใหญ่ที่ระบบคราฟต์เรียกรวมว่าโต๊ะทั่วไป'),

    ('cook', ['bonfire', 'camping_grill', 'square_fire', 'kitchen'],
     'แน่', 'icon ของแท็กคือ furniture_workbench_bonfire ⇒ กองไฟ · ครัวก็ทำอาหารได้'),

    ('kiln', ['kiln', 'furnace'],
     'แน่', 'ชื่อตรงกับ id แท็ก · furnace (เตาหลอม) เป็นเตาเผาชนิดเดียวกัน'),

    ('table_clothes', ['loom', 'clotheshorse', 'fur_table'],
     'เดา', 'icon เป็น furniture_fur_table_01 เหมือนแท็ก table ⇒ ใช้โต๊ะทั่วไปได้ '
            'และกี่ทอผ้า/ราวตากผ้าเป็นของสายเสื้อผ้า'),

    ('table_weapon', ['weapon_table'],
     'แน่', 'ชื่อตรงกับ id แท็ก'),

    ('dryer', ['dryingrack', 'hide_drying', 'clotheshorse'],
     'แน่', 'icon คือ recipe_dry · ทั้งสามอย่างคือราวตาก/แร็คตากของ'),

    ('loom', ['loom'],
     'แน่', 'ชื่อตรงกับ id แท็ก · icon คือ building_workbench_loom'),

    ('table_medicine', ['medicine_table'],
     'แน่', 'ชื่อตรงกับ id แท็ก · icon คือ worktable_medicine'),

    ('kitchen', ['kitchen'],
     'แน่', 'ชื่อตรงกับ id แท็ก · icon คือ workbench_kitchen_01'),

    ('fertilizer_maker', ['fertilizer_maker'],
     'แน่', 'ชื่อตรงกับ id แท็ก · icon คือ workbench_fertilizer_maker_01'),

    ('dye_work_table', ['dye_01', 'dye_02', 'dye_03', 'dye_rack'],
     'แน่', 'ชุดย้อมสี — ชื่อขึ้นต้นด้วย dye'),

    ('dye_medicine_lab', ['medicine_table', 'dye_rack'],
     'เดา', 'icon คือ tag_purpose_medicine เหมือนกับ dye_work_table ⇒ อยู่สายยา+ย้อมสี'),

    ('table_jewelry', ['fur_table_jewel'],
     'แน่', 'มีโต๊ะชื่อ fur_table_jewel ตัวเดียวในเกม'),

    ('kitchen_lava', ['kitchen_04'],
     'เดา', 'icon คือ workbench_kitchen_03 ⇒ ครัวรุ่นสูงสุด · เลือกครัวเลขมากสุดที่มี'),

    ('cook_filter', ['coffee_dutch'],
     'แน่', 'icon คือ warp_dutch_01 ⇒ ชุดกาแฟดริป (coffee_dutch_01)'),

    ('alcohol_ripen', ['barrel_oak'],
     'แน่', 'icon คือ warp_oak_01 ⇒ ถังไม้โอ๊กหมักเหล้า (barrel_oak_01)'),

    ('urban_worktable', ['worktable_warp'],
     'เดา', 'โต๊ะตระกูล _warp เป็นของที่ได้จากรูวาร์ป/เมือง ⇒ ตรงกับคำว่า urban'),

    ('urban_kitchen', ['kitchen_04', 'kitchen_03'],
     'เดา', 'ไม่มีครัวที่ชื่อมี warp/urban ⇒ ให้ครัวรุ่นสูงใช้แทน ไม่งั้นสูตร 4 ตัวนี้ตายสนิท'),

    ('urban_dye_medicine_lab', ['dye_rack'],
     'เดา', 'สาย urban ของ dye_medicine_lab ⇒ ให้ชั้นวางย้อมสีซึ่งเป็นตัวรุ่นใหม่สุด'),
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--check', action='store_true', help='รายงานว่าสูตรไหนยังไม่มีโต๊ะรองรับ')
    args = ap.parse_args()

    with io.open(os.path.join(ASSETS, 'entity_types', 'artifact.json'), encoding='utf-8') as f:
        artifacts = json.load(f)
    with io.open(os.path.join(ASSETS, 'building', 'blueprints.json'), encoding='utf-8') as f:
        blueprints = json.load(f)
    with io.open(os.path.join(ASSETS, 'item', 'recipes.json'), encoding='utf-8') as f:
        recipes = json.load(f)

    # โต๊ะ = artifact ที่มี component Workbench หรือ Laboratory
    benches = {}
    for type_id, v in artifacts.items():
        comps = v.get('components')
        if isinstance(comps, list) and ({'Workbench', 'Laboratory'} & set(comps)):
            benches[v.get('__name__')] = type_id

    result, confidence = {}, {}
    for tag, needles, level, why in RULES:
        confidence[tag] = {'ความมั่นใจ': level, 'เหตุผล': why, 'โต๊ะ': []}
        for name in sorted(benches):
            if not any(n in name for n in needles):
                continue
            bp = blueprints.get(name) or {}
            max_level = int(bp.get('max_level') or 60)
            result.setdefault(name, {})[tag] = max_level
            confidence[tag]['โต๊ะ'].append(name)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    payload = {
        '_อ่านก่อน': 'ตารางนี้เราสร้างเอง ไม่ใช่ข้อมูลของ NEXON — ดูเหตุผลแต่ละแท็กใน _ที่มา '
                     'และวิธีสร้างใหม่ใน tools/derive-workbench-tags.py',
        '_ที่มา': confidence,
        'โต๊ะ': result,
    }
    with io.open(OUT, 'w', encoding='utf-8') as f:
        json.dump(payload, f, ensure_ascii=False, indent=2)

    print('เขียน %s' % OUT)
    print('  โต๊ะที่ได้แท็ก %d ตัว จากโต๊ะทั้งหมด %d ตัว' % (len(result), len(benches)))
    sure = [t for t, _, lv, _ in [(r[0], r[1], r[2], r[3]) for r in RULES] if lv == 'แน่']
    print('  แท็ก แน่ %d · เดา %d' % (len(sure), len(RULES) - len(sure)))

    # ตรวจว่าสูตรไหนยังคราฟต์ไม่ได้เพราะไม่มีโต๊ะตัวไหนให้แท็กนั้น
    provided = set()
    for tags in result.values():
        provided |= set(tags)
    blocked, ok = 0, 0
    missing = {}
    for key, v in recipes.items():
        want = v.get('workbench_tags') or {}
        if not want:
            ok += 1
            continue
        if set(want) & provided:
            ok += 1
        else:
            blocked += 1
            for t in want:
                missing[t] = missing.get(t, 0) + 1
    print('  สูตรที่คราฟต์ได้ %d · ยังติด %d' % (ok, blocked))
    if missing:
        print('  แท็กที่ยังไม่มีโต๊ะรองรับ: %s' % sorted(missing.items(), key=lambda kv: -kv[1]))

    if args.check:
        print('\nรายละเอียดต่อแท็ก:')
        for tag, _, level, why in RULES:
            info = confidence[tag]
            print('  %-24s [%s] โต๊ะ %d ตัว — %s' % (tag, level, len(info['โต๊ะ']), why))
            print('      %s' % ', '.join(info['โต๊ะ'][:8]))


main()
