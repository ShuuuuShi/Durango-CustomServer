# -*- coding: utf-8 -*-
"""เตรียม asset + manifest.json สำหรับ GitHub Release ของตัวเกม

วิธีใช้
    # อัปเดตใหญ่ — อัป zip เต็ม (หน้า release จบในตัว)
    python tools/make-release.py --tag lasthuman-2026-09-08 --version 0.2.0 \
        --pack dist/pc/DurangoLastHuman-test --notes "0.2.0 — ..."

    # hotfix — อัปแค่ ~6 MB ไม่ต้องอัป zip 850 MB
    python tools/make-release.py --tag lasthuman-2026-09-08 --version 0.2.1 \
        --pack dist/pc/DurangoLastHuman-test --patch-only --prev-tag lasthuman-2026-09-07b

รูปแบบ manifest ลอกจาก release เดิมเป๊ะ เพราะตัวแพตช์อยู่ใน DinoWorldLauncher.exe
ซึ่งไม่มีซอร์สในโปรเจกต์ ⇒ ห้ามเดารูปแบบ

⚠️ manifest.json ต้องขึ้นพร้อม release เสมอ — เคยพลาดปล่อย release ที่ไม่มีไฟล์นี้
   แล้ว releases/latest/download/manifest.json ตอบ 404 = เครื่องผู้เล่นทุกเครื่องอัปเดตไม่ได้
   ⇒ สร้าง release เป็น --draft ก่อน อัปครบแล้วค่อย --draft=false --latest

⚠️ --patch-only ตั้ง ZipUrl ให้ชี้ zip ของ release ก่อนหน้า ใช้ได้เพราะ launcher
   ติดตั้งจาก zip แล้วแพตช์ตาม Files[] ต่อเสมอ (zip ของ 0.1.8 ข้างในเป็น version.txt = 0.1.6
   ด้วยซ้ำ แล้วถูกแพตช์ขึ้นมา) — ข้อแลกคือหน้า release ใหม่ไม่มีปุ่ม zip ให้กด
   ต้องเขียนลิงก์ชี้ไป release เก่าใน notes เอง
"""
import argparse
import hashlib
import io
import json
import os
import shutil
import sys
import urllib.request

sys.stdout.reconfigure(encoding='utf-8')

BS = chr(92)   # แบ็กสแลช — เขียนแบบนี้กันเครื่องมือที่กินอักขระหนีตอนส่งสคริปต์
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__))).replace(BS, '/') + '/'
REPO = 'ShuuuuShi/Durango-TH-Client'
DL = 'https://github.com/' + REPO + '/releases/download/'
ZIPNAME = 'DurangoLastHuman-test.zip'

ap = argparse.ArgumentParser()
ap.add_argument('--tag', required=True, help='tag ของ release เช่น lasthuman-2026-09-08')
ap.add_argument('--version', required=True, help='เลขเวอร์ชันที่ manifest ประกาศ เช่น 0.2.0')
ap.add_argument('--pack', default='dist/pc/DurangoLastHuman-test', help='โฟลเดอร์ชุดเกมที่แพ็กไว้')
ap.add_argument('--stage', default='dist/release', help='โฟลเดอร์พักไฟล์ที่จะอัป')
ap.add_argument('--notes', default='', help='ข้อความ Notes ใน manifest')
ap.add_argument('--patch-only', action='store_true', help='ไม่อัป zip — ชี้ ZipUrl ไป release เก่า')
ap.add_argument('--prev-tag', default='', help='ใช้คู่กับ --patch-only: tag ที่มี zip อยู่แล้ว')
args = ap.parse_args()

if args.patch_only and not args.prev_tag:
    sys.exit('--patch-only ต้องระบุ --prev-tag ด้วย (tag ที่มี zip อยู่แล้ว)')

pack_rel = args.pack.replace(BS, '/').rstrip('/')
stage_rel = args.stage.replace(BS, '/').rstrip('/')
PACK = ROOT + pack_rel + '/'
STAGE = ROOT + stage_rel + '/'
TAG = args.tag
VERSION = args.version
BASE = DL + TAG + '/'
NOTES = args.notes or (VERSION + ' — อัปเดต')

if not os.path.isdir(PACK):
    sys.exit('ไม่พบโฟลเดอร์ชุดเกม: ' + PACK)
os.makedirs(STAGE, exist_ok=True)


def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


# ── asset ที่ต้องอัป (ชื่อไฟล์บน release → ที่มา) ────────────────────────────
io.open(STAGE + 'version.txt', 'w', encoding='utf-8', newline='').write(VERSION)

assets = {
    'Assembly-CSharp.dll': PACK + 'Durango_Data/Managed/Assembly-CSharp.dll',
    'messages-th.mo':      PACK + 'locales/th_TH/LC_MESSAGES/messages.mo',
    'clusters.json':       PACK + 'clusters.json',
    'server.txt':          PACK + 'server.txt',
    'version.txt':         STAGE + 'version.txt',
}
if not args.patch_only:
    assets[ZIPNAME] = ROOT + pack_rel + '.zip'

for name, src in assets.items():
    if not os.path.exists(src):
        sys.exit('ไม่พบไฟล์ที่ต้องอัป: ' + src)
    dst = STAGE + name
    if os.path.abspath(src) != os.path.abspath(dst):
        shutil.copy2(src, dst)

info = {name: (sha256(STAGE + name), os.path.getsize(STAGE + name)) for name in assets}
for name in sorted(info):
    h, size = info[name]
    print('%-32s %12d  %s' % (name, size, h[:16]))

# ── ไฟล์ที่ตัวแพตช์จะเอาไปวางทับในเครื่องผู้เล่น ──────────────────────────────
# สามเส้นทาง locale เหมือน manifest เดิม — T.ParseCultureInfo แปลง '_' เป็น '-'
# แต่เครื่องผู้เล่นตั้งค่าไว้ต่างกันได้ ⇒ วางให้ครบทั้งสามแบบ
files = []


def add(path, asset):
    h, size = info[asset]
    files.append({'Path': path, 'Url': BASE + asset, 'Sha256': h, 'Size': size})


add('Durango_Data/Managed/Assembly-CSharp.dll', 'Assembly-CSharp.dll')
add('locales/th/LC_MESSAGES/messages.mo', 'messages-th.mo')
add('locales/th-TH/LC_MESSAGES/messages.mo', 'messages-th.mo')
add('locales/th_TH/LC_MESSAGES/messages.mo', 'messages-th.mo')
add('clusters.json', 'clusters.json')
add('server.txt', 'server.txt')
add('version.txt', 'version.txt')

if args.patch_only:
    # ดึง ZipUrl/Sha256 จาก manifest ของ release ก่อนหน้ามาใช้ต่อ
    # ⚠️ ห้ามเดา hash — ต้องเป็นค่าจริงของไฟล์นั้น ไม่งั้น launcher ตรวจแล้วไม่ผ่าน
    with urllib.request.urlopen(DL + args.prev_tag + '/manifest.json') as r:
        prev = json.loads(r.read().decode('utf-8'))
    zip_url, zip_sha = prev['ZipUrl'], prev['Sha256']
    print('โหมดแพตช์: ใช้ zip ของ %s (sha %s...)' % (args.prev_tag, zip_sha[:16]))
else:
    zip_url, zip_sha = BASE + ZIPNAME, info[ZIPNAME][0]

manifest = {
    'Version': VERSION,
    'ZipUrl': zip_url,
    'Sha256': zip_sha,
    'Notes': NOTES,
    'Files': files,
}
io.open(STAGE + 'manifest.json', 'w', encoding='utf-8', newline='').write(
    json.dumps(manifest, ensure_ascii=False, indent=2))

# md5 ของ zip — release เดิมมีไฟล์นี้ด้วย (โหมดแพตช์ไม่มี zip จึงข้าม)
if not args.patch_only:
    md5 = hashlib.md5()
    with open(STAGE + ZIPNAME, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            md5.update(chunk)
    io.open(STAGE + ZIPNAME + '.md5', 'w', encoding='utf-8', newline='').write(
        md5.hexdigest() + '  ' + ZIPNAME + chr(10))

names = sorted(os.listdir(STAGE))
print()
print('พร้อมอัป %d ไฟล์ที่ %s' % (len(names), STAGE))
print('คำสั่งถัดไป (สร้างเป็น draft ก่อน แล้วค่อยเผยแพร่):')
print('  gh release create %s --repo %s --title "..." --notes-file notes.md --draft %s'
      % (TAG, REPO, ' '.join(stage_rel + '/' + n for n in names)))
print('  gh release edit %s --repo %s --draft=false --latest' % (TAG, REPO))
