@echo off
rem helper: mumu.bat <install|launch|stop|shot|log|tap|swipe|clear> [args]
set ADB="E:\Program files\Netease\MuMu\nx_main\adb.exe" -s 127.0.0.1:16384
if /i "%1"=="install" %ADB% install -r -t "E:\Snooker3D\Builds\Snooker3D.apk" & goto :eof
if /i "%1"=="launch" %ADB% shell am start -n com.snookerlab.snooker3d/com.unity3d.player.UnityPlayerActivity & goto :eof
if /i "%1"=="stop" %ADB% shell am force-stop com.snookerlab.snooker3d & goto :eof
if /i "%1"=="shot" mkdir "E:\Snooker\shots" 2>nul & %ADB% exec-out screencap -p > "E:\Snooker\shots\%~2.png" & echo saved E:\Snooker\shots\%~2.png & goto :eof
if /i "%1"=="log" %ADB% logcat -d -s Unity:V | findstr "SNOOKER" & goto :eof
if /i "%1"=="logall" %ADB% logcat -d -s Unity:V & goto :eof
if /i "%1"=="clear" %ADB% logcat -c & goto :eof
if /i "%1"=="tap" %ADB% shell input tap %2 %3 & goto :eof
if /i "%1"=="swipe" %ADB% shell input swipe %2 %3 %4 %5 %6 & goto :eof
echo usage: mumu install/launch/stop/shot NAME/log/logall/clear/tap X Y/swipe X1 Y1 X2 Y2 MS
