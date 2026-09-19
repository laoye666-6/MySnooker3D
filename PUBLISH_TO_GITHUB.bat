@echo off
title MySnooker3D - GitHub Publish Wizard
set "GH=C:\Program Files\GitHub CLI\gh.exe"
set "GITDIR=D:\Program Files\Git\cmd"
set "GIT=%GITDIR%\git.exe"

rem Ensure git is discoverable by gh (gh requires git in PATH)
set "PATH=%GITDIR%;%PATH%"

echo ============================================================
echo   MySnooker3D - GitHub Publish Wizard
echo   Local repo is ready. Only auth + push left.
echo   (This window uses English to avoid codepage issues.)
echo ============================================================
echo.

rem Sanity check: both tools must exist
if not exist "%GIT%" (
    echo [ERROR] git not found at "%GIT%"
    echo         Please fix GITDIR at the top of this file.
    pause
    exit /b 1
)
if not exist "%GH%" (
    echo [ERROR] gh not found at "%GH%"
    echo         Please fix GH at the top of this file.
    pause
    exit /b 1
)
echo [OK] git: & "%GIT%" --version
echo [OK] gh : & "%GH%" --version
echo.

echo ---- Step 1: Login to GitHub ----
echo A one-time code will be shown. Your browser will open
echo github.com/login/device - enter the code there and authorize.
pause
"%GH%" auth login --hostname github.com --git-protocol https --web
if errorlevel 1 (
    echo [ERROR] Login failed. Please run this bat again.
    pause
    exit /b 1
)

echo.
echo ---- Step 2: Create public repo MySnooker3D and push ----
cd /d "%~dp0"
"%GH%" repo create MySnooker3D --public --description "2-player snooker 3D game for Android - Unity + procedural Blender pipeline, MIT" --source . --remote origin --push
if errorlevel 1 (
    echo [ERROR] repo create/push failed. Check network and retry.
    pause
    exit /b 1
)

echo.
echo ---- Step 3 (optional): Create v0.33 Release with APK ~70MB ----
set /p REL="Upload APK release? (Y/N): "
if /i "%REL%"=="Y" (
    "%GH%" release create v0.33 "E:\Snooker3D\Builds\Snooker3D.apk" --title "MySnooker3D v0.33" --notes "First public release. ~70MB, supports arm64-v8a devices and x86_64 emulators such as MuMu."
)

echo.
echo ============================================================
echo   DONE! Repo URL: https://github.com/^<your-username^>/MySnooker3D
echo   View it with: "%GH%" repo view MySnooker3D --web
echo ============================================================
pause
