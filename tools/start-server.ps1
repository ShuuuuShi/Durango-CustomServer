# start-server.ps1 — เมนูเปิดเซิร์ฟ Durango (เรียกจาก "เปิดเซิร์ฟ.bat")
#
# เซิร์ฟตัวนี้พอร์ตตรงมาจากเซิร์ฟในตัวของเกม (Durango.Online) จึงใช้พอร์ตชุดเดียวกับที่
# ตัวเกมฝังมา:
#   gateway 8190   HTTP  — /knock /sessions /entry /players /terrains
#   game    8191   TCP   — handshake GetClock → Auth → Ready แล้วเข้าโลก
# game\server.txt ต้องชี้ 127.0.0.1:8190 (ค่าเริ่มต้นตรงอยู่แล้ว)
#
# ⚠️ เซิร์ฟตัวนี้ **ไม่มี** --radiotower / --cluster-mode Online / --region-role / --admin-token
#    / --whitelist (ของเซิร์ฟตัวเก่า) ใส่ไปแล้วเซิร์ฟไม่รู้จัก flag → ดับทันที
#
# ⚠️ ไฟล์นี้ต้องเซฟเป็น UTF-8 **มี BOM** เท่านั้น — PowerShell 5.1 อ่านไฟล์ที่ไม่มี BOM
#    เป็น ANSI แล้วภาษาไทยจะกลายเป็นขยะทั้งไฟล์ (ส่วน .bat ต้องเป็น ASCII ล้วน)

$ErrorActionPreference = 'Continue'

$root    = Split-Path -Parent $PSScriptRoot
$server  = Join-Path $root 'server'
$exe     = Join-Path $server 'bin\Release\net9.0\DurangoServer.exe'
$logDir  = Join-Path $server 'logs'
$gameDir = Join-Path $root 'game'
$gameExe = Join-Path $gameDir 'Durango.exe'

$GatewayPort = 8190
$GamePort    = 8191

# flag ที่ต้องมีทุกโหมด
$CoreArgs = @(
    '--gateway-port', "$GatewayPort",
    '--game-port',    "$GamePort",
    '--name',         'main'
)

function Say($text, $color = 'Gray') { Write-Host $text -ForegroundColor $color }

function Get-ServerProc { Get-Process DurangoServer -ErrorAction SilentlyContinue }
function Get-GameProc   { Get-Process Durango -ErrorAction SilentlyContinue }

function Stop-Server {
    $p = Get-ServerProc
    if (-not $p) { Say '  เซิร์ฟไม่ได้เปิดอยู่' 'DarkGray'; return }
    Say '  กำลังปิดเซิร์ฟ...' 'Yellow'
    Stop-Process -Id $p.Id -Force
    Start-Sleep -Seconds 2
    Say '  ปิดแล้ว (โลกถูก autosave ทุก 60 วินาทีอยู่แล้ว)' 'Green'
}

function Build-Server {
    Say '  build เซิร์ฟ...' 'Cyan'
    # ต้อง kill ก่อน ไม่งั้นไฟล์ .exe ถูกล็อก แล้ว build ตกด้วย MSB3021
    if (Get-ServerProc) { Stop-Server }
    Push-Location $server
    $out = & dotnet build -c Release -v q --nologo 2>&1
    Pop-Location
    $errs = $out | Select-String ' error '
    if ($errs) {
        Say '  build ไม่ผ่าน:' 'Red'
        $errs | Select-Object -First 8 | ForEach-Object { Say "    $_" 'Red' }
        return $false
    }
    Say '  build ผ่าน' 'Green'
    return $true
}

function Start-Server {
    if (Get-ServerProc) {
        Say '  มีเซิร์ฟเปิดค้างอยู่แล้ว — ปิดก่อน (เมนู 3) หรือใช้ตัวเดิมต่อ' 'Yellow'
        return
    }
    if (-not (Test-Path $exe)) {
        Say '  ยังไม่มีไฟล์ที่ build ไว้ — build ให้ก่อน' 'Yellow'
        if (-not (Build-Server)) { return }
    }
    if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Force $logDir | Out-Null }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $log   = Join-Path $logDir "server-$stamp.log"
    $exeDir = Split-Path $exe

    Start-Process -FilePath $exe -ArgumentList $CoreArgs -WorkingDirectory $exeDir `
        -RedirectStandardOutput $log -RedirectStandardError "$log.err" -WindowStyle Hidden

    Say '  รอเซิร์ฟตื่น...' 'DarkGray'
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 700
        if (Test-Path $log) {
            if (Select-String -Path $log -Pattern 'gateway http' -Quiet -ErrorAction SilentlyContinue) { break }
        }
    }

    if (Get-ServerProc) {
        Say ''
        Say "  เซิร์ฟเปิดแล้ว   gateway $GatewayPort  /  เกม $GamePort" 'Green'
        Say "  log: $log" 'DarkGray'
        Say ''
        Say '  ถ้าจะให้เครื่องอื่น (มือถือ/เพื่อนใน LAN) เข้าได้ ต้องรันครั้งเดียวแบบ Run as administrator:' 'DarkGray'
        Say "    netsh http add urlacl url=http://*:$GatewayPort/ user=Everyone" 'DarkGray'
    } else {
        Say '  เซิร์ฟดับทันที — ดู log ท้ายไฟล์ด้านล่าง' 'Red'
        if (Test-Path $log) { Get-Content $log -Tail 15 | ForEach-Object { Say "    $_" 'DarkGray' } }
    }
}

function Test-Server {
    if (-not (Test-Path $exe)) { Say '  ยังไม่ได้ build' 'Yellow'; return }
    if (-not (Get-ServerProc)) { Say '  ต้องเปิดเซิร์ฟก่อน (เมนู 1) — selftest เป็นตัวไคลเอนต์จำลองที่ยิงเข้าเซิร์ฟ' 'Yellow'; return }
    Say '  ยิง selftest เข้าเซิร์ฟที่เปิดอยู่...' 'Cyan'
    Push-Location (Split-Path $exe)
    & $exe --selftest --gateway-port $GatewayPort --game-port $GamePort 2>&1 |
        ForEach-Object { Write-Host "    $_" }
    Pop-Location
}

function Start-Game {
    if (Get-GameProc) { Say '  เกมเปิดอยู่แล้ว' 'Yellow'; return }
    if (-not (Test-Path $gameExe)) { Say "  ไม่เจอไฟล์เกมที่ $gameExe" 'Red'; return }
    # เกมบังคับเปิดผ่าน launcher (client\LauncherGate.cs) — ถ้าไม่มี token เกมจะฆ่าตัวเองทันที
    # ทำเหมือน launcher เป๊ะ: สุ่ม token เขียน launcher.session แล้วส่ง env ให้ตรงกัน
    $token = [guid]::NewGuid().ToString('N')
    Set-Content -Path (Join-Path $gameDir 'launcher.session') -Value $token -Encoding ascii -NoNewline
    $env:DINOWORLD_LAUNCH = $token
    Start-Process -FilePath $gameExe -WorkingDirectory $gameDir
    Say "  เปิดเกมแล้ว (game\server.txt ต้องชี้ 127.0.0.1:$GatewayPort)" 'Green'
}

function Show-Log {
    if (-not (Test-Path $logDir)) { Say '  ยังไม่มี log' 'DarkGray'; return }
    $newest = Get-ChildItem $logDir -Filter 'server-*.log' -ErrorAction SilentlyContinue |
              Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $newest) { Say '  ยังไม่มี log' 'DarkGray'; return }
    Say "  $($newest.FullName)" 'DarkGray'
    Say ''
    Get-Content $newest.FullName -Tail 30 | ForEach-Object { Write-Host "  $_" }
}

function Show-Status {
    $sp = Get-ServerProc
    $gp = Get-GameProc
    if ($sp) { $s = "เปิดอยู่ (pid $($sp.Id))"; $sc = 'Green' } else { $s = 'ปิดอยู่'; $sc = 'DarkGray' }
    if ($gp) { $g = 'เปิดอยู่'; $gc = 'Green' } else { $g = 'ปิดอยู่'; $gc = 'DarkGray' }
    Write-Host '  เซิร์ฟ: ' -NoNewline
    Write-Host $s -ForegroundColor $sc -NoNewline
    Write-Host '     เกม: ' -NoNewline
    Write-Host $g -ForegroundColor $gc
}

while ($true) {
    Clear-Host
    Say ''
    Say '  ==================================================' 'DarkYellow'
    Say '   Durango — เปิดเซิร์ฟ (Durango.Online)' 'Yellow'
    Say '  ==================================================' 'DarkYellow'
    Say ''
    Show-Status
    Say ''
    Say "   1  เปิดเซิร์ฟ            (gateway $GatewayPort / เกม $GamePort)"
    Say '   2  build ใหม่แล้วเปิด'
    Say '   3  หยุดเซิร์ฟ'
    Say '   4  selftest              (เช็ค handshake — ต้องเปิดเซิร์ฟก่อน)'
    Say '   5  เปิดเกม'
    Say '   6  ดู log ล่าสุด 30 บรรทัด'
    Say '   0  ออก'
    Say ''
    $choice = Read-Host '  เลือก'
    Say ''
    switch ($choice) {
        '1' { Start-Server }
        '2' { if (Build-Server) { Start-Server } }
        '3' { Stop-Server }
        '4' { Test-Server }
        '5' { Start-Game }
        '6' { Show-Log }
        '0' { return }
        default { Say '  ไม่มีตัวเลือกนี้' 'DarkGray' }
    }
    Say ''
    Read-Host '  กด Enter เพื่อกลับเมนู' | Out-Null
}
