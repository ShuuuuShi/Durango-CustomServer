# reload.ps1 — ปิดเซิร์ฟ → build → เปิดเซิร์ฟ → พาตัวละครกลับเข้าเกม (เครื่องมือทดสอบ)
#
# ทำไมต้องมี: วนแก้โค้ด-เทสในเกมรอบหนึ่งมีหลายขั้นตอนที่ลืมง่าย และถ้าลืม build ก่อนเปิด
# จะได้ผลของโค้ดเก่าโดยไม่รู้ตัว (เจอมาแล้ว — csproj ก๊อป data/terrains ตอน build ด้วย)
#
#   powershell -File tools/reload.ps1            ครบวง
#   powershell -File tools/reload.ps1 -NoGame    ไม่ต้องพาเข้าเกม
#   powershell -File tools/reload.ps1 -NoBuild   เปิดใหม่เฉย ๆ ไม่ build
#
# ⚠️ ไฟล์นี้ต้องเซฟเป็น UTF-8 มี BOM (PowerShell 5.1 อ่านไฟล์ไม่มี BOM เป็น ANSI แล้วไทยพัง)

param([switch]$NoGame, [switch]$NoBuild)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Exe = Join-Path $Root 'server/bin/Release/net9.0/DurangoServer.exe'
$Log = Join-Path $env:TEMP 'durango-server.log'

Write-Host 'ปิดเซิร์ฟ...' -ForegroundColor DarkGray
Get-Process DurangoServer -ErrorAction SilentlyContinue | Stop-Process -Confirm:$false
Start-Sleep -Milliseconds 1200

if (-not $NoBuild) {
    Write-Host 'build...' -ForegroundColor DarkGray
    $out = & dotnet build (Join-Path $Root 'server') -c Release -v q -nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        $out | Select-String -Pattern 'error' | Select-Object -First 8
        Write-Host 'build ไม่ผ่าน — หยุด' -ForegroundColor Red
        exit 1
    }
}

Write-Host 'เปิดเซิร์ฟ...' -ForegroundColor DarkGray
Remove-Item $Log -ErrorAction SilentlyContinue
Start-Process -FilePath $Exe -ArgumentList '--gateway-port', '8190', '--game-port', '8191', '--name', 'main' -RedirectStandardOutput $Log -RedirectStandardError "$Log.err" -WindowStyle Hidden
Start-Sleep -Seconds 6

if ($NoGame) {
    Write-Host "เซิร์ฟพร้อม · log: $Log" -ForegroundColor Green
    exit 0
}

# ตัวเกมเด้งกลับหน้า Title ตอนเซิร์ฟหาย — กดปุ่ม "เริ่ม" เข้าเกมใหม่
# ⚠️ ห้ามแตะกลางจอเพื่อปิดกล่องข้อความ: บนหน้าเลือกตัวละครตรงนั้นเป็นช่องสร้างตัวใหม่
#    (เคยพลาดมาแล้ว ได้ตัวละครใหม่แทนที่จะกลับเข้าตัวเดิม)
Write-Host 'พากลับเข้าเกม...' -ForegroundColor DarkGray
$bot = Join-Path $PSScriptRoot 'bot.ps1'
& powershell -NoProfile -File $bot raw 'tap x=497 y=111' | Out-Null
Start-Sleep -Seconds 16
& powershell -NoProfile -File $bot st
Write-Host "log เซิร์ฟ: $Log" -ForegroundColor DarkGray
