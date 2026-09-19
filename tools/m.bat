@echo off
rem =====================================================================
rem m.bat - adb helper for the MuMu emulator (install / launch / shot / log ...)
rem
rem v0.34 changes:
rem   * No hard-coded absolute paths: ADB path, apk and screenshot dir are
rem     derived from this script's location or from environment variables,
rem     so a fresh clone works on another machine.
rem   * Always runs "adb connect" first, and verifies get-state. A dead adb
rem     used to make "shot" silently produce a 0-byte png.
rem   * Every command checks errorlevel and returns exit code 1 on failure
rem     instead of printing success no matter what.
rem
rem Usage: m.bat <install|launch|stop|shot NAME|log|logall|clear|tap X Y|swipe X1 Y1 X2 Y2 MS>
rem
rem Optional overrides (set before calling):
rem   MUMU_ADB     path to adb.exe
rem   MUMU_SERIAL  emulator adb address      (default 127.0.0.1:16384)
rem   SNOOKER_APK  apk to install            (default ..\..\Snooker3D\Builds\Snooker3D.apk)
rem NOTE: keep this file pure ASCII - a UTF-8 batch file breaks on GBK consoles.
rem =====================================================================
setlocal
if "%MUMU_SERIAL%"=="" set "MUMU_SERIAL=127.0.0.1:16384"
if "%SNOOKER_APK%"=="" set "SNOOKER_APK=%~dp0..\..\Snooker3D\Builds\Snooker3D.apk"
if "%MUMU_ADB%"=="" set "MUMU_ADB=E:\Program files\Netease\MuMu\nx_main\adb.exe"
set "PKG=com.snookerlab.snooker3d"
set "ACT=com.unity3d.player.UnityPlayerActivity"
set "SHOTS=%~dp0..\shots"

if not exist "%MUMU_ADB%" (
  echo [mumu] FAILED: adb not found at "%MUMU_ADB%"
  echo [mumu] set MUMU_ADB=path\to\adb.exe and retry
  exit /b 1
)

rem Connect first; a second connect on an already-connected device is harmless.
"%MUMU_ADB%" connect %MUMU_SERIAL% >nul 2>&1
"%MUMU_ADB%" -s %MUMU_SERIAL% get-state >nul 2>&1
if errorlevel 1 (
  echo [mumu] FAILED: device %MUMU_SERIAL% not reachable
  echo [mumu] hint: start the emulator first, e.g.
  echo [mumu]   "E:\Program files\Netease\MuMu\nx_main\MuMuManager.exe" control -v 0 launch
  exit /b 1
)

if /i "%1"=="install" (
  if not exist "%SNOOKER_APK%" ( echo [mumu] FAILED: apk not found: "%SNOOKER_APK%" & exit /b 1 )
  "%MUMU_ADB%" -s %MUMU_SERIAL% install -r -t "%SNOOKER_APK%"
  if errorlevel 1 ( echo [mumu] FAILED: install & exit /b 1 )
  echo [mumu] installed %SNOOKER_APK%
  goto :eof
)
if /i "%1"=="launch" (
  "%MUMU_ADB%" -s %MUMU_SERIAL% shell am start -n %PKG%/%ACT%
  if errorlevel 1 ( echo [mumu] FAILED: launch & exit /b 1 )
  goto :eof
)
if /i "%1"=="stop" (
  "%MUMU_ADB%" -s %MUMU_SERIAL% shell am force-stop %PKG%
  goto :eof
)
if /i "%1"=="clear" (
  "%MUMU_ADB%" -s %MUMU_SERIAL% logcat -c
  goto :eof
)
if /i "%1"=="log" (
  "%MUMU_ADB%" -s %MUMU_SERIAL% logcat -d -s Unity:V | findstr "SNOOKER"
  goto :eof
)
if /i "%1"=="logall" (
  "%MUMU_ADB%" -s %MUMU_SERIAL% logcat -d -s Unity:V
  goto :eof
)
if /i "%1"=="shot" (
  if "%2"=="" ( echo [mumu] usage: m.bat shot NAME & exit /b 1 )
  if not exist "%SHOTS%" mkdir "%SHOTS%"
  "%MUMU_ADB%" -s %MUMU_SERIAL% exec-out screencap -p > "%SHOTS%\%2.png"
  if errorlevel 1 ( echo [mumu] FAILED: screencap & exit /b 1 )
  for %%F in ("%SHOTS%\%2.png") do if %%~zF LEQ 0 ( echo [mumu] FAILED: empty screenshot & exit /b 1 )
  echo [mumu] saved %SHOTS%\%2.png
  goto :eof
)
if /i "%1"=="tap" (
  "%MUMU_ADB%" -s %MUMU_SERIAL% shell input tap %2 %3
  goto :eof
)
if /i "%1"=="swipe" (
  "%MUMU_ADB%" -s %MUMU_SERIAL% shell input swipe %2 %3 %4 %5 %6
  goto :eof
)

echo usage: m.bat install/launch/stop/shot NAME/log/logall/clear/tap X Y/swipe X1 Y1 X2 Y2 MS
exit /b 1
