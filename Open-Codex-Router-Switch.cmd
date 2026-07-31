@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0dist\CodexRouterSwitch.exe" (
  echo CodexRouterSwitch.exe is missing.
  echo Run Build-Exe.ps1 first.
  pause
  exit /b 1
)
start "Codex Router Switch" "%~dp0dist\CodexRouterSwitch.exe"
endlocal
