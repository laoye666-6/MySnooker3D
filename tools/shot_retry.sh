#!/bin/bash
# ShotTest 自动重试 v3：注意不能 export MSYS_NO_PATHCONV——它会让循环里的
# taskkill //IM 不被转换（//IM 原样传给 taskkill → 静默失败 → 僵尸堆积）。
# Unity.exe 的 -projectPath 用的是反斜杠 Windows 路径，本来就不需要路径转换。
for i in 1 2 3 4 5; do
  for k in 1 2 3; do
    taskkill //IM Unity.exe //F >/dev/null 2>&1
    sleep 4
    if ! tasklist //FI "IMAGENAME eq Unity.exe" 2>/dev/null | grep -q "Unity.exe"; then break; fi
  done
  rm -rf /e/Snooker3D/Temp 2>/dev/null
  ("/e/Program files/2022.3.62f3c1/Editor/Unity.exe" -screen-width 1600 -screen-height 900 \
    -projectPath "E:\Snooker3D" -executeMethod ShotTest.Run -logFile "E:\Snooker\logs\shot.log" &)
  sleep 60
  python "E:/Snooker/tools/pin_unity.py" 2>/dev/null | tail -1
  for t in $(seq 1 12); do
    sleep 15
    if grep -qE "\[SHOT\] DONE" /e/Snooker/logs/shot.log; then
      echo "OK attempt $i"; exit 0
    fi
  done
  echo "attempt $i frozen, retrying"
done
echo "ALL ATTEMPTS FAILED"; exit 1
