# -*- coding: utf-8 -*-
"""add-herds.py — เติม herds.yml (จุดเกิดฝูงสัตว์) ให้เกาะที่เจนเอง

ทำไมต้องมี: เกาะของ NEXON มี herds.yml มาในไฟล์ terrain อยู่แล้ว แต่ตัวเจนเกาะของเรา
(tools/mapgen) ไม่ได้ออกไฟล์นี้ ⇒ เซิร์ฟรู้ว่า "เกาะนี้ต้องมีสัตว์อะไรบ้าง" (จาก
region_templates.json) แต่ไม่รู้ว่า "ให้เกิดตรงไหน" ⇒ เกาะที่เจนเองไม่มีสัตว์เลยสักตัว

ไฟล์ที่เขียนออกไปต้องอ่านได้ด้วย server/Support/TerrainHerds.cs และมีชื่อกลุ่มตรงกับ
คีย์ใน region_templates.json → herds (ที่ใช้จริงในเกาะของเรามีแค่ land กับ beach)

    herds:
      land:
      - id: 1
        tile:
        - 120
        - 88

การเลือกจุด:
  * "พื้นดิน" = ไบต์ใน whole.ocean เท่ากับ 0 — ตรวจกับเกาะจริงแล้ว: ช่องที่ผู้เล่นยืนอยู่
    (182,162 บน ri18tp01) ได้ 0 ส่วนช่องกลางทะเลได้ค่าสูง
  * whole.ocean เป็นตาราง (N+1)×(N+1) ไม่ใช่ N×N (66049 ไบต์สำหรับเกาะ 256) ⇒ stride = N+1
  * เว้นขอบแมพ 12 ช่อง กันสัตว์เกิดตกขอบ
  * beach = ช่องดินที่มีน้ำอยู่ในรัศมี 2 ช่อง · land = ช่องดินที่ไม่ติดน้ำ
  * กระจายด้วยตะแกรง (เลือกช่องที่ดีที่สุดในแต่ละช่องตะแกรง) ไม่ใช่สุ่มล้วน ⇒ ฝูงไม่กระจุก
  * ผลคงที่ทุกครั้งที่รันด้วยเกาะเดิม (สุ่มจากชื่อเกาะ) ⇒ เปิดเซิร์ฟใหม่สัตว์อยู่ที่เดิม

รัน:
    python tools/add-herds.py --id ri18tp01
    python tools/add-herds.py --all            # ทุกเกาะที่ยังไม่มี herds.yml
"""
import argparse
import io
import json
import os
import random
import shutil
import sys
import zipfile

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
TERRAIN_DIR = os.path.join(ROOT, 'server', 'data', 'terrains')
TEMPLATES = os.path.join(ROOT, 'server', 'data', 'assets', 'region_templates.json')

EDGE_MARGIN = 12       # เว้นจากขอบแมพ (ช่อง)
BEACH_RADIUS = 2       # ดินที่มีน้ำในรัศมีนี้นับเป็นชายหาด


def classify(ocean, n):
    """คืน (จุดที่เป็นดินใน, จุดที่เป็นชายหาด) — พิกัดเป็น tile"""
    stride = n + 1 if len(ocean) >= (n + 1) * (n + 1) else n

    def is_water(x, y):
        if x < 0 or y < 0 or x >= n or y >= n:
            return True
        i = y * stride + x
        return i >= len(ocean) or ocean[i] != 0

    inland, beach = [], []
    for y in range(EDGE_MARGIN, n - EDGE_MARGIN):
        for x in range(EDGE_MARGIN, n - EDGE_MARGIN):
            if is_water(x, y):
                continue
            near_water = False
            for dy in range(-BEACH_RADIUS, BEACH_RADIUS + 1):
                for dx in range(-BEACH_RADIUS, BEACH_RADIUS + 1):
                    if is_water(x + dx, y + dy):
                        near_water = True
                        break
                if near_water:
                    break
            (beach if near_water else inland).append((x, y))
    return inland, beach


def spread(points, count, rng):
    """เลือก count จุดจาก points ให้กระจายทั่วแมพ — แบ่งตะแกรงแล้วหยิบช่องละจุด"""
    if not points or count <= 0:
        return []
    if len(points) <= count:
        return list(points)

    # ตะแกรงจัตุรัสที่มีช่องพอ ๆ กับจำนวนที่ต้องการ
    side = max(1, int(count ** 0.5 + 0.999))
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    x0, x1 = min(xs), max(xs) + 1
    y0, y1 = min(ys), max(ys) + 1
    cw = max(1, (x1 - x0) // side)
    ch = max(1, (y1 - y0) // side)

    buckets = {}
    for p in points:
        key = ((p[0] - x0) // cw, (p[1] - y0) // ch)
        buckets.setdefault(key, []).append(p)

    keys = sorted(buckets)
    rng.shuffle(keys)
    picked, i = [], 0
    # วนหยิบช่องละจุดจนครบ ช่องไหนหมดก็ข้าม (ช่องที่ดินเยอะจะถูกหยิบซ้ำในรอบหลัง)
    while len(picked) < count and keys:
        empty = []
        for key in keys:
            bucket = buckets[key]
            if not bucket:
                empty.append(key)
                continue
            picked.append(bucket.pop(rng.randrange(len(bucket))))
            if len(picked) >= count:
                break
        for key in empty:
            keys.remove(key)
        i += 1
        if i > count + 5:
            break
    return picked


def build_yaml(groups):
    lines = ['herds:']
    hid = 1
    for name in sorted(groups):
        points = groups[name]
        if not points:
            continue
        lines.append('  %s:' % name)
        for (x, y) in points:
            lines.append('  - id: %d' % hid)
            lines.append('    tile:')
            lines.append('    - %d' % x)
            lines.append('    - %d' % y)
            hid += 1
    return '\n'.join(lines) + '\n'


def add_to(region_id, templates, force=False):
    path = os.path.join(TERRAIN_DIR, region_id + '.zip')
    if not os.path.exists(path):
        print('  ข้าม %s: ไม่พบไฟล์' % region_id)
        return False

    with zipfile.ZipFile(path) as z:
        names = z.namelist()
        if 'herds.yml' in names and not force:
            print('  ข้าม %s: มี herds.yml อยู่แล้ว' % region_id)
            return False
        if 'whole.ocean' not in names or 'info.yml' not in names:
            print('  ข้าม %s: ไฟล์เกาะไม่ครบ' % region_id)
            return False
        ocean = z.read('whole.ocean')
        info = json.loads(z.read('info.yml').decode('utf-8'))
        keep = {n: z.read(n) for n in names if n != 'herds.yml'}

    n = int(info['tile_count'][0])
    template_id = info.get('region_template') or region_id
    wanted = (templates.get(template_id) or {}).get('herds') or {}
    if not wanted:
        print('  ข้าม %s: แม่แบบ %s ไม่ได้สั่งให้มีฝูงสัตว์' % (region_id, template_id))
        return False

    inland, beach = classify(ocean, n)
    if not inland and not beach:
        print('  ข้าม %s: หาพื้นดินไม่เจอ' % region_id)
        return False
    # เกาะที่ไม่มีดินในเลย (เกาะเล็ก) ให้ใช้ชายหาดแทน จะได้ไม่เสียฝูงไปเปล่า ๆ
    if not inland:
        inland = beach

    rng = random.Random(region_id)
    groups = {}
    for name, spec in wanted.items():
        count = int(spec.get('total_count') or 0)
        if count <= 0:
            continue
        pool = beach if name == 'beach' else inland
        if not pool:
            pool = inland or beach
        groups[name] = spread(pool, count, rng)

    text = build_yaml(groups)
    tmp = path + '.tmp'
    with zipfile.ZipFile(tmp, 'w', zipfile.ZIP_DEFLATED) as z:
        for name in sorted(keep):
            z.writestr(name, keep[name])
        z.writestr('herds.yml', text)
    shutil.move(tmp, path)

    summary = ' · '.join('%s %d' % (k, len(v)) for k, v in sorted(groups.items()))
    print('  %-12s เพิ่ม herds.yml — %s  (ดินใน %d ช่อง · ชายหาด %d ช่อง)'
          % (region_id, summary, len(inland), len(beach)))
    return True


def main():
    ap = argparse.ArgumentParser(description='เติม herds.yml ให้เกาะที่เจนเอง')
    ap.add_argument('--id', help='ชื่อเกาะ (ไม่ใส่ .zip)')
    ap.add_argument('--all', action='store_true', help='ทุกเกาะที่ยังไม่มี herds.yml')
    ap.add_argument('--force', action='store_true', help='เขียนทับของเดิม')
    args = ap.parse_args()

    if not args.id and not args.all:
        ap.error('ต้องระบุ --id หรือ --all')

    with io.open(TEMPLATES, encoding='utf-8') as f:
        templates = json.load(f)

    ids = ([os.path.basename(p)[:-4] for p in sorted(os.listdir(TERRAIN_DIR)) if p.endswith('.zip')]
           if args.all else [args.id])

    print('เติมจุดเกิดฝูงสัตว์ (%d เกาะ)' % len(ids))
    changed = sum(1 for rid in ids if add_to(rid, templates, args.force))
    print('เสร็จ — แก้ไป %d เกาะ' % changed)
    if changed:
        print('⚠️ ต้อง `dotnet build server -c Release` ก่อน เพราะ csproj ก๊อป terrain zip ตอน build')


main()
