# build-android.ps1 — สร้าง APK สำหรับ Android จากซอร์สใน client\
#
#   powershell -File tools\android\build-android.ps1
#   powershell -File tools\android\build-android.ps1 -VpsHost 1.2.3.4 -GatewayPort 8890
#   powershell -File tools\android\build-android.ps1 -Local          # ชี้เซิร์ฟในเครื่อง (เทสผ่าน adb reverse)
#   powershell -File tools\android\build-android.ps1 -Install        # build แล้วลงเครื่องที่ต่อ adb อยู่
#
# ═══ ทำงานยังไง ═══
# 1. build Assembly-CSharp.dll จาก client\ (net35 · Roslyn) — ตัวเดียวกับที่ใช้บน PC
# 2. ฝังที่อยู่เซิร์ฟลง client\Durango.System\BakedCluster.cs แล้ว build ใหม่ (คืนไฟล์เดิมให้เสมอ)
# 3. เอา DLL ไปวางแทนใน APK ฐาน (แบบ Mono) แล้ว zipalign + เซ็นชื่อ  → tools\android\swap-managed.py
#
# ═══ ทำไมไม่ build จาก Unity ═══
# ซอร์สใน client\ เป็น C# 11 ที่ Roslyn คอมไพล์ แต่ Unity 2017 ใช้ mcs (C# 4) คอมไพล์ไม่ผ่าน
# ⇒ ใช้ APK ที่ Unity build ไว้แล้วเป็นฐาน แล้วสลับเฉพาะ Assembly-CSharp.dll เข้าไป
#   IL ที่ Roslyn ออกมาเป็น net35 รันบน Mono ตัวเดียวกับที่ใช้บน PC อยู่ทุกวัน
#
# ⚠️ APK ฐานต้องเป็นแบบ **Mono** เท่านั้น — ของ NEXON แท้เป็น IL2CPP สลับ DLL ไม่ได้
# ⚠️ ไฟล์นี้ต้องเซฟเป็น UTF-8 **มี BOM** (PowerShell 5.1 อ่านไฟล์ไม่มี BOM เป็น ANSI แล้วไทยพัง)

param(
  [string]$VpsHost     = '187.53.129.69',
  [int]$GatewayPort    = 8890,
  [string]$ServerName  = 'lasthuman',
  [string]$BaseApk     = '',                  # ว่าง = ใช้ tools\android\base\base-mono.apk
  [string]$Out         = '',                  # ว่าง = dist\DurangoLastHuman-<host>.apk
  [switch]$Local,                             # ชี้ 127.0.0.1 (ใช้คู่กับ adb reverse)
  [switch]$Install,                           # ลงเครื่องที่ต่อ adb อยู่หลัง build
  [switch]$SkipBuild                          # ใช้ DLL ที่ build ไว้แล้ว
)

$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$client  = Join-Path $root 'client'
$net35   = Join-Path $client 'bin\Release\net35'
$baked   = Join-Path $client 'Durango.System\BakedCluster.cs'
$swap    = Join-Path $PSScriptRoot 'swap-managed.py'

function Say($t, $c = 'Gray') { Write-Host $t -ForegroundColor $c }

if ($Local) { $VpsHost = '127.0.0.1' }
$address = "http://${VpsHost}:${GatewayPort}"
if (-not $BaseApk) { $BaseApk = Join-Path $PSScriptRoot 'base\base-mono.apk' }
if (-not $Out) {
  $tag = $VpsHost -replace '[^0-9A-Za-z]', '-'
  $Out = Join-Path $root "dist\DurangoLastHuman-$tag-$GatewayPort.apk"
}

Say "`n=== build APK สำหรับ Android ===" Cyan
Say ("เซิร์ฟ    {0}" -f $address)
Say ("APK ฐาน  {0}" -f $BaseApk)
Say ("ปลายทาง  {0}" -f $Out)
Say ''

if (-not (Test-Path $BaseApk)) {
  Say "ไม่พบ APK ฐานที่ $BaseApk" Red
  Say 'ต้องเป็น APK ที่ Unity build แบบ Mono (มี assets/bin/Data/Managed/Assembly-CSharp.dll)' Yellow
  Say 'วางไว้ที่ tools\android\base\base-mono.apk แล้วรันใหม่ (ดู tools\android\README.md)' Yellow
  exit 1
}

# ── 1. ฝังที่อยู่เซิร์ฟ แล้ว build ─────────────────────────────────────────────
# ⚠️ ต้องคืนไฟล์เดิมให้ได้เสมอแม้ build ล้มกลางคัน ไม่งั้น repo ค้างที่อยู่เซิร์ฟไว้
$bakedBackup = $null
try {
  if (-not $SkipBuild) {
    $clusters = @{
      clusters = @{ $ServerName = @{
          gateway_url_root = $address
          name             = @{ en_US = 'LastHuman'; th_TH = 'LastHuman'; ko_KR = 'LastHuman' }
      } }
      offline  = $false
    } | ConvertTo-Json -Depth 6 -Compress

    $bakedBackup = Get-Content $baked -Raw -Encoding UTF8
    # ฝังเป็น C# string literal — escape " และ \ ให้เรียบร้อย
    $literal = $clusters.Replace('\', '\\').Replace('"', '\"')
    $patched = $bakedBackup -replace 'public const string Json = "";', "public const string Json = `"$literal`";"
    if ($patched -eq $bakedBackup) { throw "แทนที่ BakedCluster.Json ไม่สำเร็จ — ไฟล์ $baked เปลี่ยนรูปแบบไปหรือเปล่า" }
    [System.IO.File]::WriteAllText($baked, $patched, (New-Object System.Text.UTF8Encoding $false))
    Say '1) ฝังที่อยู่เซิร์ฟลง BakedCluster.cs แล้ว build' Cyan

    & dotnet build (Join-Path $client 'Assembly-CSharp.csproj') -c Release -v q --nologo 2>&1 |
      Select-String -Pattern ' error ' | Select-Object -First 5
    if ($LASTEXITCODE -ne 0) { throw 'build ฝั่ง client ไม่ผ่าน' }
  } else {
    Say '1) ข้าม build (ใช้ DLL ที่มีอยู่)' Yellow
  }
}
finally {
  if ($bakedBackup) {
    [System.IO.File]::WriteAllText($baked, $bakedBackup, (New-Object System.Text.UTF8Encoding $false))
    Say '   คืน BakedCluster.cs กลับเป็นค่าว่างแล้ว (ไม่ให้ที่อยู่เซิร์ฟค้างใน repo)' DarkGray
  }
}

# ── 2. รวบรวม DLL ที่ต้องใส่ ───────────────────────────────────────────────────
# ตัวที่ขาดไม่ได้คือ Assembly-CSharp.dll อย่างเดียว
# ⚠️ ตรวจแล้ว (6 ก.ย. 2026) DLL ของโปรเจกต์นี้ **ไม่ได้อ้าง** DurangoClientModSdk / 0Harmony เลย
#    (build-client.ps1 ยังก๊อปสองตัวนั้นอยู่เพราะเป็นของค้างจากตอนที่ยังมีระบบ mod)
#    ⇒ ใส่ให้เฉพาะเมื่อ build ออกมาจริง จะได้ไม่ยัด DLL ที่ไม่มีใครเรียกเข้า APK เปล่า ๆ
$required = @('Assembly-CSharp.dll')
$optional = @('DurangoClientModSdk.dll', '0Harmony.dll')
$putArgs = @()
foreach ($d in $required) {
  $p = Join-Path $net35 $d
  if (-not (Test-Path $p)) { Say "ไม่พบ $p — build ฝั่ง client ก่อน" Red; exit 1 }
  Say ("   ใส่ {0} ({1:N2} MB)" -f $d, ((Get-Item $p).Length / 1MB))
  $putArgs += @('--put', $p)
}
foreach ($d in $optional) {
  $p = Join-Path $net35 $d
  if (Test-Path $p) {
    Say ("   ใส่ {0} ({1:N2} MB · ตัวเสริม)" -f $d, ((Get-Item $p).Length / 1MB))
    $putArgs += @('--put', $p)
  }
}

# ── 3. สลับ DLL + เซ็นชื่อ ─────────────────────────────────────────────────────
Say "`n2) สลับ DLL ลง APK แล้วเซ็นชื่อ" Cyan
$keystore = Join-Path $PSScriptRoot 'keys\durangoth.keystore'
& python $swap $BaseApk $Out @putArgs --keystore $keystore
if ($LASTEXITCODE -ne 0) { Say 'สร้าง APK ไม่สำเร็จ' Red; exit 1 }

# ── 4. ลงเครื่อง (ถ้าสั่ง) ─────────────────────────────────────────────────────
if ($Install) {
  Say "`n3) ติดตั้งลงเครื่องที่ต่อ adb อยู่" Cyan
  & adb install -r $Out
  if ($LASTEXITCODE -ne 0) {
    Say 'ติดตั้งไม่สำเร็จ — ถ้าเคยลงด้วยคีย์อื่นต้องถอนก่อน: adb uninstall com.nexon.durango.global' Yellow
  }
}

Say "`n=== เสร็จ ===" Cyan
Say ("APK: {0}" -f $Out)
if ($Local) {
  Say 'โหมด -Local: ต้องต่อพอร์ตเข้าเครื่องก่อนเปิดเกม' Yellow
  Say ("  adb reverse tcp:{0} tcp:{0}" -f $GatewayPort)
  Say ("  adb reverse tcp:{0} tcp:{0}" -f ($GatewayPort + 1))
}
