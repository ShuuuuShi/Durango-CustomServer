# เปิด UDP Voice Relay สำหรับ Proximity Voice
param(
  [string]$Bind = '0.0.0.0',
  [int]$Port = 8192,
  [string]$Secret = $(if ($env:DURANGO_VOICE_SECRET) { $env:DURANGO_VOICE_SECRET } else { 'lasthuman-voice' })
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'tools\VoiceRelay\VoiceRelay.csproj'

Write-Host "Building VoiceRelay..." -ForegroundColor Cyan
dotnet build $proj -c Release | Out-Host

$exe = Join-Path $root 'tools\VoiceRelay\bin\Release\net9.0\VoiceRelay.exe'
if (!(Test-Path $exe)) { throw "VoiceRelay.exe not found: $exe" }

Write-Host "Starting voice relay udp://${Bind}:${Port}" -ForegroundColor Green
& $exe --bind $Bind --port $Port --secret $Secret
