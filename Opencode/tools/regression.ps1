# regression.ps1 — เทสทุกระบบที่ทำมา ผ่าน BotBridge พร้อมถ่ายรูปก่อน/หลังทุกด่าน
#
# ทำไมต้องมี: ระบบเยอะขึ้นเรื่อย ๆ และหลายอันพังแบบ "เงียบ" (ไม่มี error แค่จอว่าง)
# สคริปต์นี้เดินครบทุกระบบในรอบเดียว แล้วเก็บรูปไว้เทียบว่าเปลี่ยนอะไรจริงบ้าง
#
#   powershell -File tools\regression.ps1
#   powershell -File tools\regression.ps1 -Out D:\shots
#
# ⚠️ เป็นเครื่องมือทดสอบ ใช้ BotBridge ซึ่งต้องลบก่อนเปิดจริง
# ⚠️ ไฟล์นี้ต้องเซฟเป็น UTF-8 มี BOM (PowerShell 5.1 อ่านไฟล์ไม่มี BOM เป็น ANSI แล้วไทยพัง)

param(
    [string]$Out = "$PSScriptRoot\..\test-shots",
    [switch]$SkipRestart
)

$ErrorActionPreference = 'Continue'
$Root = Split-Path -Parent $PSScriptRoot
$Bot = Join-Path $PSScriptRoot 'bot.ps1'
$ServerLog = Join-Path $env:TEMP 'durango-server.log'
if (-not (Test-Path $Out)) { New-Item -ItemType Directory -Path $Out -Force | Out-Null }
Get-ChildItem $Out -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force

$script:Step = 0
$script:Results = @()

function Bot([string]$cmd) { & powershell -NoProfile -File $Bot raw $cmd 2>$null }
function State { try { (& powershell -NoProfile -File $Bot state 2>$null) | ConvertFrom-Json } catch { $null } }

function Shot([string]$name) {
    $script:Step++
    $file = Join-Path $Out ("{0:d2}-{1}.png" -f $script:Step, $name)
    Bot ("shot file={0}" -f $file) | Out-Null
    Start-Sleep -Milliseconds 1400   # CaptureScreenshot เขียนไฟล์หลังเฟรมถัดไป
    if (Test-Path $file) { return $file }
    Write-Host ("   (ถ่ายรูป {0} ไม่สำเร็จ)" -f $name) -ForegroundColor DarkYellow
    return $null
}

function Check([string]$system, [bool]$pass, [string]$detail) {
    $mark = if ($pass) { 'ผ่าน' } else { 'ไม่ผ่าน' }
    $color = if ($pass) { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1} — {2}" -f $mark, $system, $detail) -ForegroundColor $color
    $script:Results += [pscustomobject]@{ 'ระบบ' = $system; 'ผล' = $mark; 'รายละเอียด' = $detail }
}

function LogHas([string]$pattern) {
    if (-not (Test-Path $ServerLog)) { return $false }
    $txt = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($ServerLog))
    return $txt -match [regex]::Escape($pattern)
}

Write-Host "`n=== เทสทุกระบบ ===" -ForegroundColor Cyan
Write-Host "รูปเก็บที่ $Out`n"

# ── 0. เข้าเกม ───────────────────────────────────────────────────────────────────
if (-not $SkipRestart) {
    Write-Host '0) เปิดเซิร์ฟ + เปิดเกมใหม่แล้วเข้าโลก' -ForegroundColor Cyan
    # เซิร์ฟอาจตายไปแล้ว (เคยเจอ crash ระหว่างเทส) — ปิด/build/เปิดใหม่ให้แน่ใจว่าใช้โค้ดล่าสุด
    & powershell -NoProfile -File (Join-Path $PSScriptRoot 'reload.ps1') -NoGame | Out-Null
    if (-not (Get-Process DurangoServer -ErrorAction SilentlyContinue)) {
        Write-Host 'เปิดเซิร์ฟไม่สำเร็จ (build ไม่ผ่าน?) — หยุด' -ForegroundColor Red
        exit 1
    }
    Get-Process Durango -ErrorAction SilentlyContinue | Stop-Process -Confirm:$false
    Start-Sleep -Seconds 3
    # BotBridge ต้องเปิดเอง (ชุดแจกจะได้ไม่มีช่องโกง) — ตั้ง env ให้เฉพาะตอนเทส
    $env:DURANGO_BOT = '1'
    Start-Process -FilePath (Join-Path $Root 'game\Durango.exe') -WorkingDirectory (Join-Path $Root 'game')
    Start-Sleep -Seconds 50
}
$titleShot = Shot 'title'
Bot 'tap x=497 y=111' | Out-Null
Start-Sleep -Seconds 22
$s = State
Check 'เข้าเกม' ($null -ne $s -and $s.ok) $(if ($s.ok) { "อยู่ฉาก $($s.scene) tile [$($s.player.tile[0]),$($s.player.tile[1])]" } else { 'เข้าโลกไม่ได้' })
if (-not $s.ok) { Write-Host 'เข้าเกมไม่ได้ — หยุด' -ForegroundColor Red; exit 1 }
Shot 'in-world' | Out-Null

# ── 1. เกาะ + สัตว์ป่าเกิดบนเกาะ ─────────────────────────────────────────────────
Write-Host "`n1) สัตว์ป่าบนเกาะ" -ForegroundColor Cyan
Check 'สัตว์ป่าเกิดบนเกาะ' ($s.animals.Count -gt 0) "เห็นสัตว์ $($s.animals.Count) ตัว"

# ── 2. หลอดเอาชีวิตรอด ───────────────────────────────────────────────────────────
Write-Host "`n2) หลอดเอาชีวิตรอด" -ForegroundColor Cyan
$st1 = $s.player.stamina[0]
Bot ("move x={0} y={1}" -f ($s.player.tile[0] * 200 + 1200), ($s.player.tile[1] * 200)) | Out-Null
Start-Sleep -Seconds 8
$s2 = State
Check 'หลอดแรงลดตอนเดิน' ($s2.player.stamina[0] -lt $st1) ("แรง {0:N1} → {1:N1}" -f $st1, $s2.player.stamina[0])

# ── 3. เก็บของธรรมชาติ ───────────────────────────────────────────────────────────
Write-Host "`n3) เก็บของธรรมชาติ" -ForegroundColor Cyan
$before = (State).inv.Count
Shot 'gather-before' | Out-Null
Bot 'gather' | Out-Null
Start-Sleep -Seconds 8
$after = (State).inv.Count
Shot 'gather-after' | Out-Null
Check 'เก็บของธรรมชาติ' ($after -gt $before) "ของในกระเป๋า $before → $after ชิ้น"

# ── 4. เสกของ + สวมอาวุธ (ทดสอบระบบของ/กระเป๋า) ─────────────────────────────────
Write-Host "`n4) ระบบของ + สวมอุปกรณ์" -ForegroundColor Cyan
$before = (State).inv.Count
Bot 'cheat it axe_twohand_metal_03 60' | Out-Null
Bot 'cheat it capture_tool_01 60' | Out-Null
Start-Sleep -Seconds 3
$after = (State).inv.Count
Check 'ของเข้ากระเป๋า' ($after -gt $before) "ของในกระเป๋า $before → $after ชิ้น"
Bot 'equip proto=axe_twohand_metal_03 slot=main' | Out-Null
Start-Sleep -Seconds 2
Shot 'equipped' | Out-Null

# ── 5. ล่าสัตว์ (อาวุธมีผลกับดาเมจ) ──────────────────────────────────────────────
Write-Host "`n5) ล่าสัตว์" -ForegroundColor Cyan
$s = State
$prey = $s.animals | Where-Object { $_.alive } | Select-Object -First 1
if ($prey) {
    $tx = [int]($prey.pos[0] / 200); $ty = [int]($prey.pos[1] / 200)
    Bot ("cheat m {0} {1}" -f ($tx - 1), $ty) | Out-Null
    Start-Sleep -Seconds 4
    Shot 'hunt-before' | Out-Null
    1..3 | ForEach-Object { Bot ("attack id={0}" -f $prey.id) | Out-Null; Start-Sleep -Milliseconds 1600 }
    Start-Sleep -Seconds 2
    Shot 'hunt-after' | Out-Null
    Check 'ตีสัตว์เข้า' (LogHas 'ล่าสัตว์] ตี') 'ดาเมจขึ้นในล็อกเซิร์ฟ'
} else {
    Check 'ตีสัตว์เข้า' $false 'ไม่เจอสัตว์ที่ยังไม่ตาย'
}

# ── 6. กรงสัตว์ ──────────────────────────────────────────────────────────────────
Write-Host "`n6) กรงสัตว์" -ForegroundColor Cyan
$s = State
Bot ("cheat prop 7035 position:{0},{1} size:2,2" -f ($s.player.tile[0] + 2), $s.player.tile[1]) | Out-Null
Start-Sleep -Seconds 4
Shot 'cage-placed' | Out-Null
Bot 'touch' | Out-Null
Start-Sleep -Seconds 3
$menus = (Bot 'menus') | ConvertFrom-Json
$hasCage = $false
if ($menus.menus) { $hasCage = [bool]($menus.menus | Where-Object { $_.action -eq 'Cage' }) }
Shot 'cage-menu' | Out-Null
Check 'แตะกรงได้เมนู' $hasCage $(if ($hasCage) { 'เมนู Cage โผล่ (ไม่ disabled)' } else { "เมนูที่ได้: $($menus.menus.action -join ', ')" })

# ── 7. สรุป ──────────────────────────────────────────────────────────────────────
Write-Host "`n=== สรุป ===" -ForegroundColor Cyan
$script:Results | Format-Table -AutoSize
$pass = ($script:Results | Where-Object { $_.'ผล' -eq 'ผ่าน' }).Count
Write-Host ("ผ่าน {0} จาก {1} · รูป {2} ใบที่ {3}" -f $pass, $script:Results.Count, $script:Step, $Out) -ForegroundColor Cyan
