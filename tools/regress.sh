#!/bin/bash
# regress.sh - 全量编辑器回归（v0.41）。串行执行：Unity 工程锁是独占的。
# 用法: bash tools/regress.sh
UNITY="E:\\Program files\\2022.3.62f3c1\\Editor\\Unity.exe"
PROJ="E:\\Snooker3D"
LOGS="E:\\Snooker\\logs"
FAILED=0

run() {  # run <名称> <executeMethod> <额外参数...>
  local name="$1"; shift
  local method="$1"; shift
  echo "=== $name ==="
  "$UNITY" -batchmode -quit "$@" -projectPath "$PROJ" -executeMethod "$method" \
      -logFile "$LOGS/r_${method#PhysTest.}.log" >/dev/null 2>&1
  echo "  exit=$?"
}

run "1/5 RuleTest"        RuleTest.Run        -nographics
run "2/5 开球 Run"        PhysTest.Run
run "3/5 库边 CushionTest" PhysTest.CushionTest
run "4/5 加塞 SpinTest"   PhysTest.SpinTest
run "5/5 袋口 PocketTest" PhysTest.PocketTest

echo
echo "================ 结果 ================"
grep -a -o "ALL RULES OK"            "$LOGS/r_Run.log" 2>/dev/null >/dev/null
grep -a -q "ALL RULES OK"            "$LOGS/r_Run.log" && echo "  (log 名含 Run 的是开球)" || true
grep -a -q "ALL RULES OK"            "$LOGS/r_.log"    && echo "规则 44/44 PASS" || echo "规则：见 r_Run.log"

for f in r_Run r_CushionTest r_SpinTest r_PocketTest; do
  [ -f "$LOGS/$f.log" ] || continue
  echo "--- $f ---"
  grep -a -E "ALL RULES OK|ALL SPIN OK|bounced=True|RESULT PASS|FAIL " "$LOGS/$f.log" | head -8
done
