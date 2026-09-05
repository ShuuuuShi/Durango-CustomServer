# -*- coding: utf-8 -*-
"""swap-managed.py — เอา DLL ที่ build จาก client/ ใส่แทนใน APK แล้ว zipalign + เซ็นชื่อ

    python swap-managed.py <in.apk> <out.apk> --put <ไฟล์.dll> [--put ...]
                           [--file <path-ใน-zip>=<ไฟล์ในเครื่อง>]
                           [--keystore <path>] [--alias <ชื่อ>] [--pass <รหัส>]
                           [--build-tools <path>]

═══ ทำไมสลับ DLL ได้ ═══
APK ตัวที่ใช้เป็นฐานถูก build แบบ **Mono** (ไม่ใช่ IL2CPP) ⇒ มี
assets/bin/Data/Managed/Assembly-CSharp.dll เป็นไฟล์ธรรมดาเหมือนฝั่ง PC เป๊ะ
⇒ วาง DLL ชุด net35 ที่ Roslyn build จาก client/ ทับได้ตรง ๆ

⚠️ APK ต้นฉบับของ NEXON (durango-wild-lands-5.2.1) เป็น **IL2CPP** — ไม่มี Managed/*.dll เลย
   สลับแบบนี้ไม่ได้ ต้องไปแพตช์ global-metadata.dat แทน (คนละเรื่อง คนละเครื่องมือ)
   เครื่องมือนี้จะตรวจให้และหยุดพร้อมบอกเหตุผล

═══ สิ่งที่ต้องมีในเครื่อง ═══
* Android SDK build-tools (zipalign · apksigner) — ค่าเริ่มต้นหาเองจาก %LOCALAPPDATA%\\Android\\Sdk
* JDK (keytool) สำหรับสร้าง keystore ครั้งแรก

⚠️ keystore ต้องเป็นตัวเดิมทุกครั้ง — Android ให้อัปเดตทับได้เฉพาะ APK ที่เซ็นด้วยคีย์เดียวกัน
   เปลี่ยนคีย์ = ผู้เล่นต้องถอนแล้วลงใหม่ (ข้อมูลในเครื่องหาย รวม account.key ⇒ **ตัวละครหาย**)
"""
import os
import subprocess
import sys
import zipfile

# ⚠️ รันผ่าน PowerShell แล้ว stdout เป็น cp874 ⇒ พิมพ์ไทย/สัญลักษณ์แล้ว UnicodeEncodeError ทั้งสคริปต์
# (เจอจริงตอนเขียนเครื่องมือนี้ — build ผ่านหมดแล้วมาตายตอน print)
try:
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')
except Exception:
    pass

MANAGED = 'assets/bin/Data/Managed/'
SIG_SUFFIXES = ('.RSA', '.SF', '.MF', '.DSA', '.EC')


def find_build_tools(explicit=None):
    """หาโฟลเดอร์ build-tools — เอาเวอร์ชันใหม่สุดที่มี zipalign กับ apksigner ครบ"""
    if explicit:
        return explicit
    root = os.path.join(os.environ.get('LOCALAPPDATA', ''), 'Android', 'Sdk', 'build-tools')
    if not os.path.isdir(root):
        return None
    for name in sorted(os.listdir(root), reverse=True):
        cand = os.path.join(root, name)
        if os.path.exists(os.path.join(cand, 'zipalign.exe')) and \
           os.path.exists(os.path.join(cand, 'apksigner.bat')):
            return cand
    return None


def die(msg):
    print('❌ ' + msg)
    sys.exit(1)


def main():
    a = sys.argv[1:]
    if len(a) < 2:
        print(__doc__)
        sys.exit(2)

    src, out = a[0], a[1]

    def arg(k, d=None):
        return a[a.index(k) + 1] if k in a else d

    puts = [a[i + 1] for i in range(len(a) - 1) if a[i] == '--put']
    extra = {}
    for i in range(len(a) - 1):
        if a[i] == '--file':
            zp, lp = a[i + 1].split('=', 1)
            extra[zp] = lp

    # ⚠ ต้องอิงโฟลเดอร์ของ**เครื่องมือ** ไม่ใช่ของไฟล์ผลลัพธ์ — ผลลัพธ์ย้ายไป dist\android\ แล้ว
    #   อิงปลายทาง = ย้ายที่วางไฟล์เมื่อไหร่ก็สร้าง keystore ใหม่เมื่อนั้น ⇒ เซ็นคนละคีย์ ⇒ ผู้เล่นอัปเดตทับไม่ได้
    keystore = arg('--keystore', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'keys', 'durangoth.keystore'))
    alias = arg('--alias', 'durangoth')
    pw = arg('--pass', 'durangoth-2026')

    sdk = find_build_tools(arg('--build-tools'))
    if not sdk:
        die('ไม่พบ Android SDK build-tools (ต้องมี zipalign กับ apksigner)\n'
            '   ลงผ่าน Android Studio หรือระบุเองด้วย --build-tools <path>')
    zipalign = os.path.join(sdk, 'zipalign.exe')
    apksigner = os.path.join(sdk, 'apksigner.bat')
    print('ใช้ build-tools:', sdk)

    if not os.path.exists(src):
        die('ไม่พบ APK ต้นทาง: ' + src)
    for p in puts + list(extra.values()):
        if not os.path.exists(p):
            die('ไม่พบไฟล์ที่จะใส่: ' + p)

    # ── ตรวจว่าเป็น APK แบบ Mono จริง ────────────────────────────────────────
    with zipfile.ZipFile(src) as z:
        names = set(z.namelist())
    if MANAGED + 'Assembly-CSharp.dll' not in names:
        il2cpp = [n for n in names if 'global-metadata' in n or 'libil2cpp' in n]
        die('APK นี้ไม่มี ' + MANAGED + 'Assembly-CSharp.dll\n' +
            ('   เป็น APK แบบ IL2CPP (เจอ ' + il2cpp[0] + ') — สลับ DLL แบบนี้ไม่ได้'
             if il2cpp else '   ไม่ใช่ APK ของ Unity แบบ Mono'))

    replace = {MANAGED + os.path.basename(p): p for p in puts}
    replace.update(extra)

    tmp = out + '.unsigned.zip'
    aligned = out + '.aligned.apk'
    for stale in (tmp, aligned):
        if os.path.exists(stale):
            os.remove(stale)
    os.makedirs(os.path.dirname(os.path.abspath(out)) or '.', exist_ok=True)

    # ── 1) ประกอบ zip ใหม่ ────────────────────────────────────────────────────
    # คงวิธีบีบอัดเดิมของแต่ละ entry ไว้ (บาง entry ต้อง STORED ไม่งั้น Unity อ่านไม่ออก)
    # และทิ้งลายเซ็นเดิม เพราะเราจะเซ็นใหม่
    swapped, added = 0, 0
    with zipfile.ZipFile(src) as zin, zipfile.ZipFile(tmp, 'w') as zout:
        seen = set()
        for info in zin.infolist():
            n = info.filename
            if n.startswith('META-INF/') and n.upper().endswith(SIG_SUFFIXES):
                continue
            if n in replace:
                data = open(replace[n], 'rb').read()
                swapped += 1
                seen.add(n)
            else:
                data = zin.read(info)
            zi = zipfile.ZipInfo(n, date_time=info.date_time)
            zi.compress_type = info.compress_type
            zi.external_attr = info.external_attr
            zout.writestr(zi, data)

        # ไฟล์ที่ APK เดิมไม่มี (เช่น DurangoClientModSdk.dll · 0Harmony.dll) ต้องเติมเข้าไปใหม่
        for zp, lp in replace.items():
            if zp in seen:
                continue
            zi = zipfile.ZipInfo(zp)
            # STORED สำหรับของที่ engine ต้อง seek อ่าน (bundle) · DLL บีบอัดได้ปกติ
            zi.compress_type = zipfile.ZIP_STORED if zp.endswith('.bundle') else zipfile.ZIP_DEFLATED
            zout.writestr(zi, open(lp, 'rb').read())
            added += 1

    print('สลับไฟล์ %d · เพิ่มใหม่ %d · zip %.1f MB' % (swapped, added, os.path.getsize(tmp) / 1048576))

    # ── 2) zipalign 4 ─────────────────────────────────────────────────────────
    # ไฟล์ที่ไม่บีบอัด (.so · resources.arsc) ต้องเรียงตรงขอบ 4 ไบต์ ไม่งั้น Android ปฏิเสธ
    subprocess.check_call([zipalign, '-f', '-p', '4', tmp, aligned])

    # ── 3) keystore (สร้างครั้งแรกครั้งเดียว) ─────────────────────────────────
    if not os.path.exists(keystore):
        os.makedirs(os.path.dirname(keystore), exist_ok=True)
        subprocess.check_call([
            'keytool', '-genkeypair', '-v', '-keystore', keystore, '-alias', alias,
            '-keyalg', 'RSA', '-keysize', '2048', '-validity', '10000',
            '-storepass', pw, '-keypass', pw,
            '-dname', 'CN=DurangoTH, OU=Community, O=DurangoTH, C=TH',
        ])
        print('สร้าง keystore ใหม่:', keystore)
        print('⚠️ เก็บไฟล์นี้ให้ดี — หายแล้วผู้เล่นอัปเดตทับไม่ได้ ต้องถอนแล้วลงใหม่')

    # ── 4) เซ็นชื่อ (v1+v2 — Android เก่าต้อง v1 · ใหม่ต้อง v2) ───────────────
    subprocess.check_call([
        apksigner, 'sign', '--ks', keystore, '--ks-key-alias', alias,
        '--ks-pass', 'pass:' + pw, '--key-pass', 'pass:' + pw,
        '--v1-signing-enabled', 'true', '--v2-signing-enabled', 'true',
        '--out', out, aligned,
    ])
    subprocess.check_call([apksigner, 'verify', '--print-certs', out])

    os.remove(tmp)
    os.remove(aligned)
    print('เสร็จ: %s (%.1f MB)' % (out, os.path.getsize(out) / 1048576))


if __name__ == '__main__':
    main()
