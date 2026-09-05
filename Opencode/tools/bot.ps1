# bot.ps1 — สั่งตัวเกมจากภายนอกผ่าน BotBridge (เครื่องมือทดสอบ)
#
# BotBridge เปิด TCP 8192 อยู่ในตัวเกม (client\BotBridge.cs) รับคำสั่งบรรทัดเดียว ตอบ JSON บรรทัดเดียว
# มันไม่แย่งเมาส์ — ปุ่ม UI กดผ่าน UICamera.GetInputTouch เดิน pipeline เดิมทั้ง raycast→OnPress→OnClick
# ⚠️ เป็นของทดสอบ ลบ BotBridge.cs กับบล็อกใน GameManager.OnAwake ก่อนเปิดจริง
#
#   powershell -File tools\bot.ps1 state              # อ่านสถานะทั้งหมด
#   powershell -File tools\bot.ps1 st                 # สรุปสั้น (tile/หลอด/ของรอบตัว)
#   powershell -File tools\bot.ps1 start              # กดปุ่ม "เริ่ม" บนหน้า Title เข้าเกม
#   powershell -File tools\bot.ps1 gather             # เก็บของธรรมชาติที่ใกล้สุด
#   powershell -File tools\bot.ps1 raw "move x=34000 y=31000"
#   powershell -File tools\bot.ps1 watch 20           # ดูหลอดเปลี่ยนทุก 2 วิ 20 รอบ
#
# ⚠️ ไฟล์นี้ต้องเซฟเป็น UTF-8 มี BOM (PowerShell 5.1 อ่านไฟล์ไม่มี BOM เป็น ANSI แล้วไทยพัง)

param(
    [Parameter(Position = 0)][string]$Cmd = 'st',
    [Parameter(Position = 1)][string]$Arg = ''
)

$BotPort = 8192

function Send-Bot([string]$line) {
    try {
        $c = New-Object System.Net.Sockets.TcpClient
        $c.Connect('127.0.0.1', $BotPort)
        $s = $c.GetStream(); $s.ReadTimeout = 10000
        $w = New-Object System.IO.StreamWriter($s); $w.AutoFlush = $true
        $r = New-Object System.IO.StreamReader($s)
        $w.WriteLine($line)
        $out = $r.ReadLine()
        $c.Close()
        return $out
    } catch {
        return "{`"ok`":false,`"error`":`"$($_.Exception.Message -replace '"','')`"}"
    }
}

function Get-State { return (Send-Bot 'state') | ConvertFrom-Json }

function Show-Short {
    $j = Get-State
    if (-not $j.ok) { Write-Host "ยังเข้าเกมไม่ได้ ($($j.error)) — ลอง: bot.ps1 start" -ForegroundColor Yellow; return }
    $p = $j.player
    Write-Host ("ฉาก {0} · tile [{1},{2}] · {3}" -f $j.scene, $p.tile[0], $p.tile[1], $(if ($p.moving) { 'กำลังเดิน' } else { 'ยืนอยู่' }))
    Write-Host ("  เลือด {0:N0}/{1:N0}   แรง {2:N1}/{3:N0}   เหนื่อย {4:N2}/{5:N0}" -f `
        $p.life[0], $p.life[1], $p.stamina[0], $p.stamina[1], $p.fatigue[0], $p.fatigue[1])
    Write-Host ("  ของในกระเป๋า {0} ชิ้น · ของธรรมชาติรอบตัว {1} · สัตว์ {2}" -f $j.inv.Count, $j.naturals.Count, $j.animals.Count)
    if ($j.naturals.Count -gt 0) {
        $near = $j.naturals | Sort-Object dist | Select-Object -First 3
        foreach ($n in $near) { Write-Host ("    ชนิด {0} ที่ [{1},{2}] ห่าง {3:N0}" -f $n.type, $n.tile[0], $n.tile[1], $n.dist) -ForegroundColor DarkGray }
    }
    if ($j.inv.Count -gt 0) {
        foreach ($i in $j.inv) { Write-Host ("    มี {0} x{1}" -f $i.proto, $i.count) -ForegroundColor DarkGray }
    }
}

switch ($Cmd) {
    'st' { Show-Short }

    'state' { Send-Bot 'state' }

    'start' {
        # ปุ่ม "เริ่ม" บนหน้า Title — พิกัดนี้ผูกกับหน้าต่างขนาด 1024x576
        # (Unity นับ y จากล่างซ้าย ต่างจาก screenshot ที่นับจากบนซ้าย)
        Write-Host 'กดปุ่มเริ่ม...' -ForegroundColor Cyan
        Send-Bot 'tap x=497 y=111' | Out-Null
        Start-Sleep -Seconds 14
        Show-Short
    }

    'gather' {
        Write-Host ('gather -> ' + (Send-Bot 'gather'))
        Start-Sleep -Seconds 8
        Show-Short
    }

    'raw' {
        if (-not $Arg) { Write-Host 'ต้องใส่คำสั่ง เช่น: bot.ps1 raw "move x=34000 y=31000"' -ForegroundColor Yellow; break }
        Send-Bot $Arg
    }

    'watch' {
        $n = 10
        if ($Arg -and [int]::TryParse($Arg, [ref]$n)) { } else { $n = 10 }
        for ($i = 0; $i -lt $n; $i++) {
            $j = Get-State
            if ($j.ok) {
                $p = $j.player
                Write-Host ("[{0:HH:mm:ss}] เลือด {1,6:N1}  แรง {2,6:N1}  เหนื่อย {3,6:N2}  ของ {4}" -f `
                    (Get-Date), $p.life[0], $p.stamina[0], $p.fatigue[0], $j.inv.Count)
            } else {
                Write-Host ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $j.error) -ForegroundColor DarkGray
            }
            Start-Sleep -Seconds 2
        }
    }

    default {
        Write-Host "คำสั่ง: st | state | start | gather | raw <คำสั่ง> | watch <รอบ>" -ForegroundColor Yellow
        Write-Host "คำสั่งดิบที่ BotBridge รับ: ping state move stop tap gather attack butcher action menu use log"
    }
}
