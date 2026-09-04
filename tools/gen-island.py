# -*- coding: utf-8 -*-
"""gen-island.py — เจนเกาะใหม่แล้วเสียบเข้าเซิร์ฟให้พร้อมเล่นในคำสั่งเดียว

ทำไมต้องมี: ตัวเจนเกาะเดิม (tools/mapgen/map_core.py) ออกไฟล์มาไม่ครบสำหรับเซิร์ฟนี้
  - ไม่เขียน pois.yml ⇒ เกาะไม่มีท่าเรือ ⇒ ล่องเรือออกจากเกาะไม่ได้
  - ไม่เขียน herds.yml ⇒ เกาะไม่มีสัตว์เลยสักตัว (เรียก add-herds.py ต่อให้แล้ว)
  - ตั้ง region_template เป็น "gen<seed>" ซึ่งเกมไม่รู้จัก ⇒ client ข้ามเกาะทิ้งทั้งกลุ่ม
    (client/ExploreSystem.cs:307)
  - ออกเป็นโฟลเดอร์ แต่ TerrainLoader อ่านเฉพาะ .zip (server/Core/TerrainLoader.cs)
สคริปต์นี้ทำสามอย่างที่ขาดให้ครบ แล้วลงทะเบียน template ให้ด้วย

ท่าเรือ: วางที่จุดเกิดของเกาะ — ในเกาะของ NEXON port_points ตรงกับ entry_points 13 จาก 14 ลูก
(อีก 3 ลูกห่างกันแค่ 2-4 tile) และตรรกะเลือกจุดเกิดของตัวเจน (_pick_entry_point) คือ
"น้ำตื้นหน้าหาดของแผ่นดินหลัก" ซึ่งเป็นเงื่อนไขเดียวกับที่ท่าเรือต้องการอยู่แล้ว

รัน:
  python tools/gen-island.py --id ri15sv01 --theme savanna  --level 15 --template-from ri15sa171228
  python tools/gen-island.py --id ri18tp01 --theme tropical --level 18 --template-from ri18tr_01_01
  python tools/gen-island.py --list-themes
"""
import argparse
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, os.path.join(HERE, 'mapgen'))

TERRAIN_DIR = os.path.join(ROOT, 'server', 'data', 'terrains')


def write_pois(directory, entry, extra_ports):
    """เขียน pois.yml ฟอร์แมตเดียวกับที่ server/Support/TerrainPois.cs อ่าน (list-of-pair)"""
    points = [tuple(entry)] + [tuple(p) for p in extra_ports]
    lines = ['port_points:']
    for x, y in points:
        lines.append('- - %d' % x)
        lines.append('  - %d' % y)
    lines.append('warpholes: {}')
    lines.append('craters: []')
    lines.append('camp_artifacts: []')
    text = '\n'.join(lines) + '\n'
    with io.open(os.path.join(directory, 'pois.yml'), 'w', encoding='utf-8') as f:
        f.write(text)
    return points


def main():
    ap = argparse.ArgumentParser(description='เจนเกาะใหม่แล้วเสียบเข้าเซิร์ฟ')
    ap.add_argument('--id', help='ชื่อเกาะ = ชื่อไฟล์ zip = RegionId ในเซิร์ฟ')
    ap.add_argument('--theme', help='ธีมภูมิประเทศ')
    ap.add_argument('--level', type=int, help='ระดับเกาะ (ใช้จัดโซนในหน้าล่องเรือ)')
    ap.add_argument('--template-from', dest='tpl_from',
                    help='template ของเกมที่ใช้เป็นแม่แบบ (ได้ herds/collectible ที่จูนมาแล้ว)')
    ap.add_argument('--size', type=int, default=256, help='ขนาดแมพ (ต้องจัตุรัส · 64-2048)')
    ap.add_argument('--seed', type=int, default=None, help='ไม่ใส่ = สุ่มจากชื่อเกาะ')
    ap.add_argument('--role', type=int, default=4, help='4 = Risky (เกาะสำรวจ) ค่าปริยาย')
    ap.add_argument('--list-themes', action='store_true')
    args = ap.parse_args()

    import terrain_themes as T
    if args.list_themes:
        print('ธีมที่มี: %s' % ', '.join(sorted(T.THEMES)))
        for key in sorted(T.THEMES):
            print('  %-12s %s' % (key, T.THEMES[key].get('label', '')))
        return
    if not (args.id and args.theme and args.level):
        ap.error('ต้องระบุ --id, --theme, --level (หรือใช้ --list-themes)')
    if args.theme not in T.THEMES:
        ap.error('ไม่รู้จักธีม %r — ดูรายชื่อด้วย --list-themes' % args.theme)

    import map_core as core

    seed = args.seed if args.seed is not None else abs(hash(args.id)) % 900000 + 1000
    params = dict(core.DEFAULTS)
    params.update({'width': args.size, 'height': args.size, 'theme': args.theme, 'seed': seed})

    print('เจนเกาะ %s — ธีม %s · %dx%d · seed %d' % (args.id, args.theme, args.size, args.size, seed))
    gen = core.MapGenerator(params)
    gen.generate()

    stats = gen.stats()
    print('  พื้นดิน %.0f%% · หน้าผา %.0f%% · แม่น้ำ %d สาย · ของธรรมชาติ %d · แลนด์มาร์ก %d'
          % (stats['land_pct'], stats['cliff_pct'], stats['rivers'], stats['garden'], stats['landmarks']))
    print('  จุดเกิด/ท่าเรือ tile %s' % (tuple(gen.entry),))

    tmp = tempfile.mkdtemp(prefix='island-')
    try:
        gen.export(tmp)

        # region_template ต้องเป็นชื่อที่ลงทะเบียนไว้ ไม่ใช่ "gen<seed>" ที่เกมไม่รู้จัก
        info_path = os.path.join(tmp, 'info.yml')
        with io.open(info_path, encoding='utf-8') as f:
            info = json.load(f)
        template_id = args.id
        info['region_template'] = template_id
        with io.open(info_path, 'w', encoding='utf-8') as f:
            json.dump(info, f, ensure_ascii=False, indent=2)

        ports = write_pois(tmp, gen.entry, [])

        out = os.path.join(TERRAIN_DIR, args.id + '.zip')
        with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
            for name in sorted(os.listdir(tmp)):
                z.write(os.path.join(tmp, name), name)
        size_mb = os.path.getsize(out) / 1048576.0
        print('  เขียน %s (%.1f MB · %d ไฟล์)' % (out, size_mb, len(os.listdir(tmp))))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    if args.tpl_from:
        print('')
        subprocess.check_call([sys.executable, os.path.join(HERE, 'add-region-template.py'),
                               '--id', template_id, '--from', args.tpl_from,
                               '--level', str(args.level), '--role', str(args.role)])
        # ต้องทำหลังลงทะเบียน template เพราะ add-herds อ่านว่าแม่แบบสั่งฝูงอะไรไว้กี่ฝูง
        print('')
        subprocess.check_call([sys.executable, os.path.join(HERE, 'add-herds.py'),
                               '--id', args.id, '--force'])
    else:
        print('')
        print('⚠️ ไม่ได้ระบุ --template-from ⇒ เกมจะยังไม่รู้จักเกาะนี้')
        print('   ลงทะเบียนภายหลังด้วย: python tools/add-region-template.py --id %s --from <แม่แบบ> --level %d'
              % (template_id, args.level))


main()
