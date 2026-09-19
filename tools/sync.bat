@echo off
rem =====================================================================
rem sync.bat - copy master sources (unity_src) into the Unity project.
rem
rem v0.34 changes:
rem   * robocopy /MIR (mirror) instead of xcopy: a .cs deleted or renamed in
rem     unity_src is now also removed from the project. xcopy only ever added
rem     files, so a stale copy caused CS0101 "already defines a member" and
rem     broke the headless build (this really happened before).
rem   * Only *.cs is mirrored, so Unity-generated .meta files and every other
rem     asset are left untouched.
rem   * Non-zero robocopy error level (>=8) is reported and returns exit code 1,
rem     instead of silently printing "synced."
rem
rem Paths are derived from this script's own location, so a fresh clone works
rem without editing. Target resolution order:
rem   1) %SNOOKER_PROJECT%\Assets  (if the env var is set)
rem   2) <repo>\..\Snooker3D\Assets  (dev layout: E:\Snooker + E:\Snooker3D)
rem   3) <repo>\UnityProject\Assets  (in-repo layout of the public repository)
rem
rem NOTE: keep this file pure ASCII - a UTF-8 batch file breaks on GBK consoles.
rem =====================================================================
setlocal
set "SRC=%~dp0..\unity_src"

if not "%SNOOKER_PROJECT%"=="" goto use_env
if exist "%~dp0..\..\Snooker3D\Assets" goto use_sibling
set "DST=%~dp0..\UnityProject\Assets"
goto have_dst
:use_env
set "DST=%SNOOKER_PROJECT%\Assets"
goto have_dst
:use_sibling
set "DST=%~dp0..\..\Snooker3D\Assets"
:have_dst

echo [sync] src = %SRC%
echo [sync] dst = %DST%

if not exist "%SRC%\Scripts" ( echo [sync] FAILED: missing "%SRC%\Scripts" & exit /b 1 )
if not exist "%DST%\Scripts" ( echo [sync] FAILED: missing "%DST%\Scripts" & exit /b 1 )
if not exist "%SRC%\Editor"  ( echo [sync] FAILED: missing "%SRC%\Editor"  & exit /b 1 )
if not exist "%DST%\Editor"  ( echo [sync] FAILED: missing "%DST%\Editor"  & exit /b 1 )

robocopy "%SRC%\Scripts" "%DST%\Scripts" *.cs /MIR /NJH /NJS /NDL /NP >nul
if errorlevel 8 ( echo [sync] FAILED: robocopy Scripts & exit /b 1 )

robocopy "%SRC%\Editor" "%DST%\Editor" *.cs /MIR /NJH /NJS /NDL /NP >nul
if errorlevel 8 ( echo [sync] FAILED: robocopy Editor & exit /b 1 )

echo [sync] OK
exit /b 0
