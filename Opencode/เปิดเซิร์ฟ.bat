@echo off
REM ==========================================================
REM  Durango - server launcher (double-click me)
REM  All Thai text lives in tools\start-server.ps1, not here:
REM  cmd.exe reads .bat as codepage 874/ANSI, so Thai text
REM  inside a UTF-8 .bat comes out as garbage.
REM ==========================================================
chcp 874 >nul 2>&1
title Durango - server launcher
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\start-server.ps1"
if errorlevel 1 (
  echo.
  echo [!] start-server.ps1 failed to run - see the message above.
  pause
)
