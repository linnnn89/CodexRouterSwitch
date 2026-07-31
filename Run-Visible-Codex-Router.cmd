@echo off
setlocal
title Codex Router - Visible Console
set "ROUTER_SWITCH_CODEX_HOME=%CODEX_ROUTER_SWITCH_CODEX_HOME%"
if not defined ROUTER_SWITCH_CODEX_HOME set "ROUTER_SWITCH_CODEX_HOME=%USERPROFILE%\.codex"
echo.
echo ============================================================
echo  CODEX ROUTER IS RUNNING IN THIS VISIBLE WINDOW
echo ============================================================
echo.
echo Keep this window open while the Router switch is ON.
echo Use the switch application to turn Router OFF safely.
echo Router log:
echo %ROUTER_SWITCH_CODEX_HOME%\codex-router\router.log
echo.
call "%ROUTER_SWITCH_CODEX_HOME%\codex-router\start-codex-router.cmd"
echo.
echo The Router process has stopped.
echo Check router.log if this was unexpected.
pause
endlocal
