@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem ============================================================================
rem  SimpleWall setup -- adds the ONE thing the app cannot do for itself.
rem
rem  The OSC firewall rule. With the Windows firewall on (the default, all
rem  profiles) and no rule for this app, packets from the Stream Deck are
rem  dropped SILENTLY: no error, no log line, nothing on screen. The app happily
rem  reports "listening on 7000" the whole time, and loopback still works, so it
rem  looks fine from the machine itself. Proven on the dev VM: before the rule,
rem  every packet from another machine vanished; after it, every one arrived.
rem
rem  Windows normally prompts on first bind -- but only in an interactive session
rem  with someone there to click Allow. The wall PC autostarts unattended, so the
rem  prompt never gets answered. This rule is why the Stream Deck works at all.
rem
rem  Autostart is NOT set here, on purpose: it is HKCU (no admin), the operator
rem  ticks it in the app's Settings tab, and the app confirms on screen that it
rem  points at THIS copy. A second source here would only disagree the first time
rem  anyone touched msconfig.
rem
rem  Run once, as administrator. Double-clicking is enough -- it elevates itself.
rem  If you change the OSC port in Settings, re-run with the new port:
rem      install.bat 7001
rem ============================================================================

set "PORT=%~1"
if "%PORT%"=="" set "PORT=7000"

rem --- Elevate if we are not already administrator ------------------------------
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Administrator rights are required to add the firewall rule.
    echo Re-launching elevated -- click "Yes" on the prompt...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList '%PORT%' -Verb RunAs"
    exit /b
)

echo(
echo   SimpleWall setup -- OSC firewall rule (inbound UDP %PORT%)
echo   =========================================================
echo(

rem --- .NET Framework 4.8 pre-flight (warn only, never blocks) -------------------
rem The wall PC was measured at Release=0x80eb1 (528049) on 2026-07-16. 528040 is
rem the 4.8 threshold. If this is lower or missing you are not on the machine this
rem was built for -- the app targets net48 and will not start without it.
set "REL="
for /f "tokens=3" %%v in ('reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release 2^>nul ^| find "Release"') do set /a "REL=%%v" 2>nul
if defined REL (
    if !REL! GEQ 528040 (
        echo   [ok]   .NET Framework 4.8 present ^(Release=!REL!^).
    ) else (
        echo   [WARN] .NET 4.8 not detected ^(Release=!REL!, need ^>=528040^).
        echo          SimpleWall targets .NET 4.8 and may not start. Do not
        echo          improvise a runtime install without checking the runbook.
    )
) else (
    echo   [WARN] Could not read the .NET version. SimpleWall needs .NET 4.8.
)

echo(

rem --- The firewall rule --------------------------------------------------------
rem Delete first so a re-run (e.g. after changing the port) does not leave a stale
rem rule for the old port sitting alongside the new one.
echo   Removing any existing "SimpleWall OSC" rule...
netsh advfirewall firewall delete rule name="SimpleWall OSC" >nul 2>&1

echo   Adding inbound UDP allow on port %PORT%...
netsh advfirewall firewall add rule name="SimpleWall OSC" dir=in action=allow protocol=UDP localport=%PORT% profile=any
if %errorlevel% neq 0 (
    echo(
    echo   [FAIL] The firewall rule could not be added. Until it is, the Stream
    echo          Deck cannot reach SimpleWall -- though the app itself will run
    echo          and loopback will still work, so it will LOOK fine.
    goto :done
)

echo(
echo   [ok] Firewall rule added. Stream Deck OSC on UDP %PORT% can now arrive.
echo(
echo   Verifying:
netsh advfirewall firewall show rule name="SimpleWall OSC" | findstr /i "Direction LocalPort Action Enabled Protocol"

:done
echo(
echo   Done. You can close this window.
echo(
pause
endlocal
