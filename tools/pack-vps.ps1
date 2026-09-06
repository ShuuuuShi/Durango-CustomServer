# pack-vps.ps1 — แพ็กเซิร์ฟไปรันบน VPS + แพ็กตัวเกมให้คนเทส
#
# ทำอะไร:
#   1. build เซิร์ฟแบบ self-contained สำหรับ Linux (VPS ไม่ต้องลง .NET)
#   2. เอา terrain มาแค่ 3 เกาะ (ตัวเลือกด้านล่าง) — เกาะน้อย = อัปโหลดเร็ว โลกเล็ก คุมได้
#   3. เขียน run.sh + systemd unit + README ภาษาไทย
#   4. ก๊อปตัวเกมเป็นชุดแจก แล้วชี้ clusters.json ไปที่ VPS
#
# ⚠️ พอร์ตต้องไม่ชนกับเซิร์ฟเก่าบนเครื่องเดียวกัน — เช็คด้วย ss -lnt บน VPS ก่อนเสมอ
#    5 ก.ย. 2026 เซิร์ฟเก่ากินไปแล้ว: 8190-8192 · 8290/8291 · 8390-8392 · 8490-8492 ·
#    8590-8592 · 8690-8692  (ยังมี 8080 AMP · 8443/8790 nginx · 8787 node)
#    ⇒ ชุดนี้ใช้ 8890 (gateway/HTTP) กับ 8891 (game/TCP)
#
# ผังปลายทาง (dist\) — **แยกคนละโฟลเดอร์ต่อแพลตฟอร์ม อย่าเอามากองรวมกัน**
#   dist\vps\      ชุดเซิร์ฟ           (ไฟล์นี้สร้าง)
#   dist\pc\       ตัวเกมฝั่ง PC       (ไฟล์นี้สร้าง)
#   dist\android\  APK                (tools\android\build-android.ps1 สร้าง)
#
#   powershell -File tools\pack-vps.ps1
#   powershell -File tools\pack-vps.ps1 -VpsHost 1.2.3.4 -GatewayPort 8590 -SkipGame

param(
    [string]$VpsHost      = '187.53.129.69',
    [int]$GatewayPort     = 8890,
    [int]$GamePort        = 8891,
    [string]$ServerName   = 'lasthuman',
    [string[]]$Islands    = @('ri18tp01', 'ri35de', 'ri50sn'),
    [string]$StartIsland  = 'ri18tp01',
    [string]$Rid          = 'linux-x64',
    [string]$Out          = "$PSScriptRoot\..\dist",
    [switch]$SkipGame
)

$ErrorActionPreference = 'Stop'
$Root     = Split-Path -Parent $PSScriptRoot
$VpsOut   = Join-Path $Out 'vps'
$GameOut  = Join-Path $Out 'pc\DurangoLastHuman-test'
$Address  = "http://${VpsHost}:${GatewayPort}"

function Say([string]$t, [string]$c = 'Gray') { Write-Host $t -ForegroundColor $c }

Say "`n=== แพ็กชุดขึ้น VPS ===" Cyan
Say ("เซิร์ฟ  {0}  (gateway {1} · game {2})" -f $VpsHost, $GatewayPort, $GamePort)
Say ("เกาะ    {0}  · เริ่มที่ {1}" -f ($Islands -join ', '), $StartIsland)
Say ("ปลายทาง {0}" -f $Out) ; Say ''

# ── 1. build เซิร์ฟ ─────────────────────────────────────────────────────────────
Say '1) build เซิร์ฟแบบ self-contained (ไม่ต้องลง .NET บน VPS)' Cyan
if (Test-Path $VpsOut) { Remove-Item $VpsOut -Recurse -Force }
$publish = Join-Path $VpsOut 'server'
& dotnet publish (Join-Path $Root 'server') -c Release -r $Rid --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=none -o $publish 2>&1 | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { Say 'build ไม่ผ่าน — หยุด' Red; exit 1 }

# ── 2. เหลือ terrain แค่เกาะที่เลือก ─────────────────────────────────────────────
Say "`n2) ตัด terrain ให้เหลือ $($Islands.Count) เกาะ" Cyan
$terrainDir = Join-Path $publish 'data\terrains'
Get-ChildItem $terrainDir -Filter *.zip | ForEach-Object {
    if ($Islands -notcontains $_.BaseName) { Remove-Item $_.FullName -Force }
}
Get-ChildItem $terrainDir -Filter *.zip | ForEach-Object { Say ("   เก็บ {0} ({1:N0} KB)" -f $_.BaseName, ($_.Length / 1KB)) }

# กันไฟล์ค้างที่ไม่ต้องใช้บนเซิร์ฟจริง
foreach ($junk in 'AppData-nx', 'logs') {
    $p = Join-Path $publish $junk
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}

# ── 3. สคริปต์รัน + systemd ─────────────────────────────────────────────────────
Say "`n3) เขียนสคริปต์รัน" Cyan
$runSh = @"
#!/bin/sh
# เปิดเซิร์ฟ Durango LastHuman
# ⚠️ พอร์ตชุดนี้แยกจากเซิร์ฟเก่าบนเครื่องเดียวกัน
# (เก่ากินไปแล้ว 8190-8192 · 8290/8291 · 8390-8392 · 8490-8492 · 8590-8592 · 8690-8692)
cd "`$(dirname "`$0")/server"
chmod +x ./DurangoServer 2>/dev/null
exec ./DurangoServer \
  --name $ServerName \
  --gateway-port $GatewayPort \
  --game-port $GamePort \
  --terrain $StartIsland \
  --public-host $VpsHost \
  --cluster-mode Online \
  --max-players 50
"@ + "`n"
[System.IO.File]::WriteAllText((Join-Path $VpsOut 'run.sh'), ($runSh -replace "`r`n", "`n"), (New-Object System.Text.UTF8Encoding $false))

$unit = @"
[Unit]
Description=Durango LastHuman ($ServerName)
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/durango-lasthuman
ExecStart=/opt/durango-lasthuman/run.sh
Restart=always
RestartSec=5
# ให้เซิร์ฟเซฟโลกก่อนตายเวลาสั่ง restart (Program.cs ดัก SIGTERM ผ่าน ProcessExit)
KillSignal=SIGTERM
TimeoutStopSec=30
StandardOutput=append:/var/log/durango-lasthuman.log
StandardError=append:/var/log/durango-lasthuman.err

[Install]
WantedBy=multi-user.target
"@
[System.IO.File]::WriteAllText((Join-Path $VpsOut 'durango-lasthuman.service'), ($unit -replace "`r`n", "`n"), (New-Object System.Text.UTF8Encoding $false))

$readme = @"
# Durango LastHuman — ชุดขึ้น VPS (รอบทดสอบ)

เซิร์ฟ: **$VpsHost**  ·  gateway/HTTP **$GatewayPort**  ·  game/TCP **$GamePort**
เกาะ: $($Islands -join ', ')  (เริ่มที่ **$StartIsland**)

พอร์ตชุดนี้ **แยกจากเซิร์ฟเก่า** ที่ใช้ 8190/8191, 8290/8291, 8390/8391 อยู่แล้ว
⇒ รันพร้อมกันบนเครื่องเดียวได้ ไม่ชนกัน

---

## ติดตั้ง (ทำครั้งเดียว)

``````sh
# บนเครื่องตัวเอง — อัปขึ้น VPS
scp -r dist/vps/* root@$($VpsHost):/opt/durango-lasthuman/

# บน VPS
chmod +x /opt/durango-lasthuman/run.sh /opt/durango-lasthuman/server/DurangoServer
cp /opt/durango-lasthuman/durango-lasthuman.service /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now durango-lasthuman

# เปิดพอร์ตให้คนนอกเข้าได้
ufw allow $GatewayPort/tcp
ufw allow $GamePort/tcp
``````

## เช็คว่าขึ้นจริง

``````sh
systemctl status durango-lasthuman
curl "http://127.0.0.1:$GatewayPort/knock?platform=Windows"     # ต้องได้ 200
tail -f /var/log/durango-lasthuman.log
``````

จากเครื่องตัวเอง: เปิด ``$Address/knock?platform=Windows`` ในเบราว์เซอร์ ต้องเห็นข้อความตอบกลับ
ถ้าไม่ตอบ = ไฟร์วอลล์ยังปิดอยู่ หรือ VPS ไม่ได้เปิดพอร์ตนั้นให้ภายนอก

## คำสั่งประจำ

| อยากทำ | คำสั่ง |
|---|---|
| รีสตาร์ต (เซฟก่อนปิดให้เอง) | ``systemctl restart durango-lasthuman`` |
| หยุด | ``systemctl stop durango-lasthuman`` |
| ดู log สด | ``tail -f /var/log/durango-lasthuman.log`` |
| ดูของที่เกมยิงมาแต่ยังไม่มี handler | ``grep 'ไม่มี handler' /var/log/durango-lasthuman.log`` |

## ไฟล์เซฟอยู่ไหน

``/opt/durango-lasthuman/server/AppData-nx/offline/$ServerName/``
* ``<slot>.player`` — ตัวละคร (ของในกระเป๋า · สัตว์เลี้ยง · จุดที่สำรวจแล้ว)
* ``<slot>.world`` — เกาะตั้งต้น  ·  ``regions/<เกาะ>.world`` — เกาะอื่น
* มี ``.bak`` คู่ทุกไฟล์ (เขียนแบบสลับเข้าที่ ปิดกลางคันแล้วไฟล์ยังอยู่ครบ)

**สำรองก่อนอัปเวอร์ชันใหม่ทุกครั้ง:**
``````sh
tar czf ~/lasthuman-saves-`$(date +%F-%H%M).tar.gz -C /opt/durango-lasthuman/server AppData-nx
``````

## อัปเวอร์ชันใหม่

``````sh
systemctl stop durango-lasthuman
# อัปเฉพาะโฟลเดอร์ server (อย่าทับ AppData-nx)
rsync -a --exclude AppData-nx dist/vps/server/ root@$($VpsHost):/opt/durango-lasthuman/server/
systemctl start durango-lasthuman
``````

## หน้าโหลดตัวเกม (nginx พอร์ต 8892)

ชุดเกมวางไว้ที่ ``/opt/durango-lasthuman/download/`` แล้ว nginx เสิร์ฟด้วยไฟล์คอนฟิกของตัวเอง
``/etc/nginx/sites-available/lasthuman-download.conf`` — **แยกจาก durango-bundles.conf ของโปรเจกต์เก่า**
ไม่ได้แตะของเดิมเลย

* ลิงก์: ``http://$($VpsHost):8892/download/DurangoLastHuman-test.zip``
* หน้ารายการ: ``http://$($VpsHost):8892/download/``
* เช็ค md5 ได้ที่ไฟล์ ``.md5`` ข้าง ๆ

อัปชุดเกมใหม่:
``````sh
scp dist/pc/DurangoLastHuman-test.zip root@$($VpsHost):/opt/durango-lasthuman/download/
ssh root@$($VpsHost) 'cd /opt/durango-lasthuman/download && md5sum DurangoLastHuman-test.zip > DurangoLastHuman-test.zip.md5'
``````

ปิดหน้าโหลดชั่วคราว: ``rm /etc/nginx/sites-enabled/lasthuman-download.conf && systemctl reload nginx``

## จะเพิ่มเกาะทีหลัง

ก๊อป ``.zip`` ของเกาะจาก ``server/data/terrains/`` ขึ้นไปวางที่
``/opt/durango-lasthuman/server/data/terrains/`` แล้ว restart — เซิร์ฟไล่หาเกาะจากไฟล์ในโฟลเดอร์นี้เอง
"@
[System.IO.File]::WriteAllText((Join-Path $VpsOut 'README.md'), $readme, (New-Object System.Text.UTF8Encoding $false))

$size = (Get-ChildItem $VpsOut -Recurse -File | Measure-Object Length -Sum).Sum
Say ("   ชุดเซิร์ฟ {0:N0} MB ที่ {1}" -f ($size / 1MB), $VpsOut) Green

# ── 4. ตัวเกมชุดแจก ─────────────────────────────────────────────────────────────
if (-not $SkipGame) {
    Say "`n4) แพ็กตัวเกม" Cyan
    $gameSrc = Join-Path $Root 'game'
    if (-not (Test-Path $gameSrc)) { Say '   ไม่พบโฟลเดอร์ game — ข้าม' Yellow }
    else {
        if (Test-Path $GameOut) { Remove-Item $GameOut -Recurse -Force }
        # ตัดของที่ไม่ควรติดไปกับชุดแจก: เซฟของเครื่องเรา · log · DLL สำรอง · session ของ launcher
        #
        # ⚠️ account.key สำคัญที่สุด — เป็น "กุญแจบัญชีประจำเครื่อง" ของคนแพ็ก
        # ติดไปกับชุดแจกเมื่อไหร่ = ผู้เล่นทุกคนใช้กุญแจเดียวกัน ⇒ เห็นและเข้าตัวละครเดียวกันหมด
        # ⇒ ระบบบัญชีทั้งระบบไร้ความหมายทันที (client/Durango.System/DeviceAccount.cs สร้างใหม่
        #    ให้เองถ้าไม่มีไฟล์ ⇒ ต้องไม่มีมาแต่แรก)
        $exclude = @('AppData', 'Users', 'MemoryBotCaptures', 'mods', 'logs')
        & robocopy $gameSrc $GameOut /E /NFL /NDL /NJH /NJS /NP `
            /XD $exclude /XF '*.log' '*.bak-*' 'launcher.session' '*.pid' 'account.key' | Out-Null
        if ($LASTEXITCODE -ge 8) { Say '   ก๊อปตัวเกมไม่สำเร็จ' Red; exit 1 }

        # ชี้ไปเซิร์ฟ VPS — ไฟล์นี้ตัวเกมอ่านตอนขึ้นหน้า Title (TitleMenuGroup.ReadLocalClusterJson)
        $clusters = @{
            clusters = @{
                $ServerName = @{
                    gateway_url_root = $Address
                    name             = @{ en_US = 'LastHuman'; th_TH = 'LastHuman'; ko_KR = 'LastHuman' }
                }
            }
            offline  = $false
        } | ConvertTo-Json -Depth 6
        [System.IO.File]::WriteAllText((Join-Path $GameOut 'clusters.json'), $clusters, (New-Object System.Text.UTF8Encoding $false))
        [System.IO.File]::WriteAllText((Join-Path $GameOut 'server.txt'), "${VpsHost}:${GatewayPort}", (New-Object System.Text.UTF8Encoding $false))

        $note = @"
# Durango LastHuman — ชุดทดสอบ

ต่อเซิร์ฟ: **$Address**

## วิธีเล่น
เปิด ``DinoWorldLauncher.exe`` (ห้ามเปิด ``Durango.exe`` ตรง ๆ — ตัวเกมมีด่านกันที่ต้องผ่าน launcher ก่อน)

## เปลี่ยนเซิร์ฟ
แก้ ``gateway_url_root`` ใน ``clusters.json`` ข้างตัวเกม แล้วเปิดใหม่ — ไม่ต้อง build อะไรเลย

## หมายเหตุ
* ชุดนี้เป็น **รอบทดสอบ** ของหาย/โลกรีเซ็ตได้ตลอด
* เครื่องมือทดสอบ (BotBridge) **ปิดอยู่** ในชุดนี้ — เปิดได้ด้วย env ``DURANGO_BOT=1`` เฉพาะเครื่องที่ใช้เทส
"@
        # ⚠️ ชื่อไฟล์ต้องเป็น ASCII — tar/zip ของ Windows แปลชื่อไทยเป็น CP437 ไม่ได้
        #    ("Can't translate Pathname ... to CP437") แล้วไฟล์นั้นหายไปจาก zip เงียบ ๆ
        [System.IO.File]::WriteAllText((Join-Path $GameOut 'READ-ME-FIRST.md'), $note, (New-Object System.Text.UTF8Encoding $false))
        # ไฟล์ชื่อไทยที่ติดมากับตัวเกมเดิมก็เจอปัญหาเดียวกัน — เปลี่ยนชื่อให้เป็น ASCII
        Get-ChildItem $GameOut -File | Where-Object { $_.Name -match '[^\x00-\x7f]' } | ForEach-Object {
            Rename-Item $_.FullName ('readme-th' + $_.Extension) -ErrorAction SilentlyContinue
        }

        $gsize = (Get-ChildItem $GameOut -Recurse -File | Measure-Object Length -Sum).Sum
        Say ("   ชุดเกม {0:N0} MB ที่ {1}" -f ($gsize / 1MB), $GameOut) Green
    }
}

Say "`n=== เสร็จ ===" Cyan
Say "อัปเซิร์ฟ:  scp -r dist/vps/* root@${VpsHost}:/opt/durango-lasthuman/"
Say "อ่านต่อ:    dist/vps/README.md"
