# 双人斯诺克 3D —— 项目上下文总结（截至 v0.43，2026-09-24）

> 本文件为完整压缩上下文，供导入 AI 助手继续开发使用。
> 项目根目录：`E:\Snooker`（工作区）；Unity 工程：`E:\Snooker3D`
> **注意**：本机 Bash 工具是 **Git Bash**，路径写作 `/e/Snooker`；执行 .bat 用 `cmd //c tools/sync.bat`
> Git Bash 会把 `/MIR`、`/c` 这类参数当路径改写，跑 robocopy/adb 前先 `export MSYS_NO_PATHCONV=1`

---

## 一、项目概述

基于 **Unity 2022.3.62f3c1** 的安卓**双人同屏斯诺克游戏**。球桌由 **Blender 5.2.1 LTS（Steam 版）**
脚本化建模（标准比赛尺寸 3569×1778mm），物理用 Unity PhysX + 自定义滚动摩擦 + 自建白球自旋模型，
规则为 **完整官方斯诺克规则**（WPBSA 2024-25，仅剩 2 项细则未实现），带可开关辅助瞄准线、
贝塞尔入场运镜、设置界面（帧率/分辨率/阴影/物理步长）、单杆分 HUD、147 满分提示、加塞杆法、
**真实袋口物理（弧形颚部 / 台呢真洞下坠 / 撞颚晃袋）**。

**已开源**：https://github.com/laoye666-6/MySnooker3D （public / MIT，Release 已到 v0.43，含 APK）
**交付物**：`E:\Snooker3D\Builds\Snooker3D.apk`（v0.43，双架构 ARM64+X86_64，IL2CPP，约 70MB）

## 二、环境与路径（全部实测有效）

| 项 | 路径 |
|---|---|
| Unity 编辑器 | `E:\Program files\2022.3.62f3c1\Editor\Unity.exe`（Android 模块齐全） |
| Blender | `D:\Program Files\STEAM\steamapps\common\Blender\blender.exe`（5.2） |
| MuMu 模拟器 | `E:\Program files\Netease\MuMu\nx_main\`（adb.exe / MuMuManager.exe） |
| git / gh | `D:\Program Files\Git\cmd\git.exe`；`C:\Program Files\GitHub CLI\gh.exe`（已登录 laoye666-6） |
| MuMu adb | `127.0.0.1:16384`（1600×900 横屏，Android 15） |
| Unity 工程 | `E:\Snooker3D`（`Assets\Scripts` / `Assets\Editor` 由 sync.bat 同步） |
| **源码母本（权威）** | `E:\Snooker\unity_src\Scripts`（11 个 cs）+ `unity_src\Editor`（4 个） |
| Blender 脚本 | `E:\Snooker\blender\`：make_table / bake_cloth / make_icon / open_table_edit / render_pockets / shrink_pockets（废弃） |
| 资产产物 | `E:\Snooker\assets\`（table.obj、cue.obj、cloth/wood 贴图、icon.png、preview_*.png） |
| 开源仓库 | `E:\Snooker\MySnooker3D`（origin 已配，master；`docs\DEVELOPMENT.md` = 详细开发文档） |
| 辅助脚本 | `E:\Snooker\tools\`：sync.bat / m.bat / regress.sh + 4 个编辑器 GUI 自动化 ps1 |
| 截图/日志 | `E:\Snooker\shots\`（编辑器截图在 `shots\editor\`）、`E:\Snooker\logs\` |
| 文档 | `E:\Snooker\README.md`（踩坑 38 条 + 版本史）、本文件、`DSH_IMPORT_PROMPT.txt` |

包名 `com.snookerlab.snooker3d`；Activity `com.unity3d.player.UnityPlayerActivity`

## 三、代码架构（全部含详细中文注释）

**运行时 `Scripts\`（11 个）：**
- `G.cs` — 全局常量：球桌尺寸(米)、**袋口几何（v0.41 新增）**、滚动摩擦 0.11、出杆初速 0.55~6.0、
  停判阈值 0.09、置球点、分值/颜色/中文名、ColorOrder、加塞物理参数、
  工具函数 `ClampToD()/InD()/InPocket()/PastCornerPocketCenter()`
- `SnookerRules.cs` — **纯函数规则引擎**（不依赖 MonoBehaviour/物理，可离线单测）：
  输入 `TableState`(出杆前状态) + `ShotFacts`(本杆事实) → 输出 `ShotOutcome`(结算+新状态)。
  文件头按 WPBSA **2024-25 版**条款逐条列出依据（3(g)/3(h)/10/11/12/4/7）
- `Bootstrapper.cs` — 用纯代码搭整个场景：物理参数 → 灯光 → 相机 → 球桌模型与碰撞体
  （**布料板挖真洞 / 库边 / 斜颚楔块 / 袋内衬**）→ 22 颗球 → 球杆与瞄准线 → 中文 UI
- `GameManager.cs` — 状态机(Menu/Aiming/Rolling/GameOver) + 把规则结果**落地**（加分/回点/
  换手/终局）+ 几何判定 `IsSnookered()`/`BallsOn()` + 单杆分 + 147 连击 + 球在手 `DragCueBall()`
- `BallController.cs` — 落袋/下沉/重置/滚动摩擦/碰撞上报；**白球自旋模型**（`CueRollStep` 滑移摩擦
  耦合 / `CushionKick` 侧塞撞库）+ v0.41 `PocketLaunchGuard()`（袋口防弹射）
- `CueController.cs` — 瞄准角(灵敏度 0.0018rad/px)/力度/出杆动画/加塞状态/拖球；
  **v0.42 新增 `NudgeStep`（微调步长常量）**
- `AimLine.cs` — 辅助线：幽灵球解析几何 + 目标球走向 + 分离线 + 库边反弹；暴露 `AimedBall`
- `SpinPad.cs` — 加塞圆盘控件（拖动小圆点选高杆/低杆/左右塞，uGUI 事件）
- `GameCamera.cs` — 贝塞尔弧线入场运镜 + smootherstep + 注视点平滑 + 菜单漂移 + 跟球/全景
- `UIManager.cs` — uGUI 图形 + **IMGUI 文字层**；HUD/菜单/设置面板/147 横幅/让对手重打提示/
  自由球与指定球状态/加塞圆盘/微调按钮
- `GameSettings.cs` — 帧率(60/90/120/144)/分辨率(50/75/100%)/阴影/物理步长，PlayerPrefs 持久化

**Editor `Editor\`（4 个）：**
- `BuildGame.cs` — 命令行构建（`-x86only` 调试；**版本号在此改**；写入图标；构建后自校验版本）
- `RuleTest.cs` — **58 条规则断言**（不走物理、不需模拟器）
- `PhysTest.cs` — 离线物理回归四入口：`Run` 开球；`CushionTest` 库边三用例；`SpinTest` 加塞；
  **`PocketTest` 袋口六用例**（v0.41 新增，含晃袋接触统计）；另有 `PocketTrace` 轨迹追踪（调试用）
- `ShotTest.cs` — 编辑器内自动截图驱动（时间线出 11 张关键帧，含袋口特写；坑见 README 踩坑 23/31）

## 四、常用命令

```bat
:: 同步源码母本 → 工程（robocopy /MIR，只镜像 *.cs，会删工程内多余 .cs）
cmd //c E:\Snooker\tools\sync.bat

:: 全量回归（串行跑 5 道；Unity 工程锁是独占的，不能并行；期望全绿）
bash E:\Snooker\tools\regress.sh

:: 单独跑（RuleTest 可加 -nographics；PhysTest 系不能加，需要图形设备）
::   RuleTest.Run         期望 PASS=58 FAIL=0 + ALL RULES OK
::   PhysTest.CushionTest 期望三个 bounced=True
::   PhysTest.SpinTest    期望 ALL SPIN OK
::   PhysTest.PocketTest  期望 PASS=6 FAIL=0
::   PhysTest.Run         开球回归
"E:\Program files\2022.3.62f3c1\Editor\Unity.exe" -batchmode -quit -nographics ^
  -projectPath "E:\Snooker3D" -executeMethod RuleTest.Run -logFile "E:\Snooker\logs\ruletest.log"

:: 构建 APK（-x86only 只编 x86_64，调试更快；正式版双架构）
"E:\Program files\2022.3.62f3c1\Editor\Unity.exe" -batchmode -quit -nographics ^
  -projectPath "E:\Snooker3D" -executeMethod BuildGame.BuildAndroid ^
  -logFile "E:\Snooker\logs\build.log"

:: 编辑器自动截图（带窗口，不加 -quit/-batchmode，跑时让 Unity 窗口保持前台）
Unity.exe -executeMethod ShotTest.Run -logFile E:\Snooker\logs\shot.log  → shots\editor\*.png

:: 袋口外观改动的离线校验（Blender 出三张特写）
blender.exe -b -P E:\Snooker\blender\render_pockets.py  → shots\pocketrender_*.png

:: 重新生成美术资产（改尺寸/袋口形状时）
blender.exe -b -P E:\Snooker\blender\make_table.py   （再 bake_cloth.py 补 UV，然后拷 assets → 工程）

:: MuMu 安装/启动/截图（Git Bash 下 export MSYS_NO_PATHCONV=1）
"E:\Program files\Netease\MuMu\nx_main\adb.exe" connect 127.0.0.1:16384
"E:\Program files\Netease\MuMu\nx_main\adb.exe" -s 127.0.0.1:16384 install -r -t E:/Snooker3D/Builds/Snooker3D.apk
"E:\Program files\Netease\MuMu\nx_main\adb.exe" -s 127.0.0.1:16384 shell am start -n com.snookerlab.snooker3d/com.unity3d.player.UnityPlayerActivity
"E:\Program files\Netease\MuMu\nx_main\adb.exe" -s 127.0.0.1:16384 shell screencap -p /sdcard/a.png
```

**操作注意**：cmd 的 `timeout /t` 在重定向环境下立即返回，等待须用
`powershell -Command "Start-Sleep -Seconds N"`。MuMu 会因宿主睡眠被关闭，
连接被拒时先 `MuMuManager.exe control -v 0 launch` 重启（约 55 秒）。
IL2CPP 全量构建 40~60 分钟；**纯 C# / 纯资产改动走增量约 3~7 分钟**。

## 五、本轮（v0.41~v0.43）三块改动

### v0.41 —— 袋口真实化（结构性重做）

**旧版的三个不真实处**：① 台面是**一整块实体盒**，球物理上掉不进洞，落袋只能靠"球心进半径
0.070 的捕获圈"的脚本判定 → **撞颚弹回根本不可能发生**；② 颚部只是一个 45° 斜块的直棱；
③ 袋内无侧墙，判落袋即关碰撞直接下坠。

| 改动 | 做法 | 依据 |
|---|---|---|
| 颚部改圆弧 | `make_table.py` 的 `cushion()` 先按斜切生成库边，再用**竖直圆柱布尔差集**切出圆弧（角袋 R22mm / 中袋 R16mm）。圆心取在"端头沿进深偏 r"处 → **与鼻线相切 → 开口宽度不变（手感不回归）** | WPBSA §2 Rule 4："the cushion face is actually cut into a curve to form the pocket opening" |
| 颚面碰撞体同源 | 按进深 d 切 6 片凸棱柱，轮廓 `min(斜颚面 d·CushD/JawDx, 颚尖圆弧 r−√(r²−d²))`。先前用一条直斜线逼近，颚尖处偏差 6.4mm（球直径的 12%） | 同上 |
| 台面挖真洞 | `BuildClothBed()`：每洞 16 条水平带逼近圆孔，轴对齐矩形拼装，**共 127 块 BoxCollider** | 落袋 = 失去支撑 + 重力 |
| 落袋判据 | `G.InPocket()` 三层：① `y<−PotDepth` 安全网；② 球心在洞口圆内**且已下沉**（角袋直接算进；中袋要求速度朝袋外，否则是真实"过袋"）；③ 角袋越过袋口中心且在往外走 | —— |
| 袋内衬 | 30mm 厚环墙，**开口侧低(−0.015) / 后侧高(+0.045)**，内径 = 洞口 + 36mm | 两个冲突约束逼出的形状（见下） |
| 袋井 | 平齐黑盘 → 深 400mm 杯状空腔；内腔半径取**开孔 − 2mm**（否则俯视会看到洞内一圈绿墙） | —— |

**袋内衬为什么必须是"开口侧低、后侧高"**（两个约束冲突，只有形状能解）：
- 低墙（藏在台面下）不挡任何滚动球，但**兜不住快球** —— 球要下落 41mm 才够得着，
  而这段时间它已横向飞出 ~180mm；
- 高墙（后壁）能兜住快球，但**绕整圈做会挡住贴库滚过中袋的球**。
- 判据用"该段**两端点都在**库边鼻线之外"（不是"任一端"），保证高墙绝不伸进台面
  （台面内的点必有 |x|≤HalfL 且 |z|≤HalfW）。

### v0.42 —— 清彩阶段切换修正 + 微调步长降到十分之一

**① 清彩阶段混乱的根因：阶段切换的触发条件写错了。**
WPBSA 2024-25 **Section 3 Rule 3(h)** 原文：

> (ii) The break is continued by potting Reds and colours alternately until all the Reds
> are off the table and, **where applicable, a colour has been played at** following the
> potting of the last Red.
> (iii) The colours **then** become on in the ascending order of their value …

关键在 **"a colour has been played at"** —— 只要求那颗"最后一红之后的任选彩球"**被击打过**，
**不要求打进**。所以这一杆无论**进球 / 未进 / 犯规**，之后目标球都是**黄球**。
旧版写成 `if (postReds == 0 && scored)`（只在合法打进时才切），于是未进/犯规换手后
接台方仍停留在"任意彩球"、还能随便挑彩球打。

| 上一杆 | 修正前 | 修正后 |
|---|---|---|
| 进最后一红 | 打任选彩球 | 打任选彩球 |
| 打任选彩球**并打进**（该球回点） | 打黄球 | 打黄球 |
| 打任选彩球**未进/空杆** | ❌ 仍打任选彩球 | ✅ 打黄球 |
| 打任选彩球**犯规** | ❌ 仍打任选彩球 | ✅ 打黄球 |
| 台面**还有红球**时任选彩球未进 | 打红球 | 打红球（不变） |

顺带把源码文件头过时的条款号按 2024-25 版全部更正（"球 on 顺序"是 3(g)/(h) 而非
旧版的 3(e)/3(f)(ii)；"ball on 定义"在 Section 2 Rule 11；"换手后回红球"没有独立条款号，
是 3(g) 的直接推论，旧注释里的 `Rule 10.3` 不存在）。

**② 微调步长**：`◀ ▶` 每次增量 `0.0035` → `0.00035` rad（0.2° → 0.02°），新常量
`CueController.NudgeStep`。原值在长台（约 3.5m）上每按一次偏 **12mm**，而长台进球角度容差
只有零点几度；现在每按一次偏 **1.2mm**。**真机实测：连按 10 次 == 原来按 1 次。**

### v0.43 —— "任选彩球"必须按【指定】的那颗计分

WPBSA **Section 3 Rule 3(h)(i)**："the next ball on is a colour of the striker's choice
**which, if potted, is scored**" —— 计分的必须是**被指定为球 on 的那颗**。
旧版对 `FreeColor` 分支无条件 `legalPts += G.Value(k)`（**从不比对**指定球）：

| 情形 | 修正前 | 修正后 |
|---|---|---|
| 指定蓝球、进蓝球 | 合法 5 分 | 合法 5 分（不变） |
| 指定蓝球、**进绿球** | ❌ 合法 3 分 | ✅ 罚 5 分、绿球回点、本杆不计分 |
| 指定蓝球、进蓝球同时误带绿球 | ❌ 合法 5 分 | ✅ 罚 5 分、蓝绿都回点 |
| 未指定（准线未指向彩球） | 进袋那颗视为认定 | 同上（保持宽松，与真实裁判一致） |

## 六、物理要点（容易改错，改动前必读）

1. **白球自旋（加塞）不能靠 PhysX**，是 `BallController.CueRollStep` 里自建的滑移摩擦模型。
   三条硬约束缺一个效果就被吃掉：① 白球物理材质摩擦取 0；② 白球 `angularDrag=0`；
   ③ 高低杆用非对称系数（`G.SpinTopK=1.0` / `SpinLowK=2.0`），否则中低杆是"无旋滑行"死区。
2. **定标依据**（实测文献）：球-台呢滑动摩擦 μ≈0.2（滚动阻力 0.005~0.015）；球重对 `a=μg`
   无影响（质量约掉），故加速度只取 μg；杆头击点极限 h≤0.5r → 高杆≤1.25×(v/r)、低杆≥−1.0×(v/r)。
   **白球速度任何时刻不得超过出杆初速**（用户明确要求，代码里有钳制；只钳速度不动自旋）。
3. **spinV=spinH=0（中杆）必须与旧版逐帧一致** —— 判断加塞实现对不对的第一道关。
4. 低速撞库"粘库" = `Physics.bounceThreshold` 太大 → 设 0.1。
5. 白球不可与棕球同点重生（PhysX 炸膛）；赋速度前必须 `WakeUp()`。
6. **袋口四条硬约束**（v0.41，改动前必读）：
   ① **颚部圆弧与库边鼻线相切** → 开口宽度不变 → 手感不回归。视觉（Blender 圆柱布尔）与
      碰撞体（凸棱柱切片）必须同源，都从 `JawProfileV(d)` 推。
   ② 台面挖真洞的代价是**洞口边缘成了阶梯**，高速球（500Hz 下 3.5m/s 每步走 7mm）滚过时
      会嵌进台阶尖角、被沿"尖角→球心"的斜向上法线顶出来（实测球心弹到 277mm、飞出台面）。
      用 `BallController.PocketLaunchGuard()` 在袋口 15cm 内钳掉向上的速度分量，表示"真实的
      袋口边沿是包着台呢的圆角"。**只在 y≤8cm 时生效，不影响正常跳球**；水平分量完全不动，
      所以撞颚弹回（晃袋）不受影响。
   ③ **袋内衬内径下限** ≥ 洞口半径 + 球半径 + 余量（取 洞口+36mm）。球在台呢上滚过袋口时
      球面最远伸到 洞口+26mm；内衬若比这近，球一进袋口就**嵌进衬壁**被去穿透逻辑崩飞
      （实测球心升到 124mm、以 4.7m/s 飞出台外 3 米）。**且必须有壁厚**：零厚度曲面会被
      5m/s 的球穿透，再被顶出来（峰值 115mm 正好 = `1.5²/2g`，这个数值特征直接指出了元凶
      `defaultMaxDepenetrationVelocity`）。现取 30mm 壁厚 + 把该上限从 1.5 降到 0.6。
   ④ **落袋判据必须"球一进洞口"就成立**，不能等掉到某个深度：高速球在下坠期间带着水平速度
      横移（实测 0.29m），会先撞上袋内衬或布料拼缝边缘被顶飞。这就是 `G.InPocket` 要分层的原因。
7. 布料板**不能**比台面多留一圈（旧版每边多 6cm，那圈在角袋处裸露成台面，球被它和库边鼻面
   夹成的直角挤飞出台外 3 米）；角袋处布料要**朝台面外侧一直敞开**（否则残留的布料"唇"会托住
   沿库滚进角袋的球，永远到不了落袋深度）。

## 七、规则要点（v0.42/v0.43 两条真 bug 的教训）

1. 规则改动**必须**走 `SnookerRules.cs` + RuleTest 断言，不许把判定写进物理/UI 代码。
2. **读规则要逐字读动词**：`played` / `potted` / `struck` 是三种不同条件。
   "a colour has been played at" 只要求击打过、不要求打进 —— 凭"意思差不多"实现就会埋雷。
3. **凡出现"指定的球""球 on"这类限定词，都要在计分前比对一次**：
   `FreeColor` 分支"看起来总是合法"，漏了"对象是否匹配"的校验。
4. **条款号要按当前规则书年份核对**。本项目注释里长期写的 `Rule 3(e)(f)` / `Rule 10.3`
   是**旧版编号**；2024-25 版：Section 3 Rule 3(g)/(h) 是球 on 顺序，Section 2 Rule 11 是
   ball on 定义。照旧号读规则会找错条款、误判语义。
5. **为什么这套结构有效**：v0.42/v0.43 两个 bug 都只在特定路径才走到，真机试打极难稳定复现
   （要正好"最后一红后打彩球但没进"或"指定蓝球进绿球"），但**构造式断言一测就现形**。

## 八、踩坑记录（共 38 条，完整版见 README）

只列**仍会影响新改动**的（完整版在 `E:\Snooker\README.md` 的「踩坑记录」）：

1. MuMu 只认 arm64-v8a/x86_64 → 必须 IL2CPP + ARM64/X86_64
2. IL2CPP 引擎裁剪会剥掉 SphereCollider/MeshCollider → `stripEngineCode=false` + `link.xml`
3. PhysX 刚体休眠后直接赋 velocity 不动 → 出杆/放球前必须 `WakeUp()`
4. 工程物理资产 `m_SimulationMode` 必须为 0；测试代码必须 try/finally 恢复（曾导致真机物理全冻）
5. uGUI 动态字体部分机渲染空白 → 文字一律 IMGUI + 内嵌 DroidSansFallback
6. **运行时创建的 UI 图形在真机不可靠**：`Sprite.Create`/`new Texture2D` 编辑器正常、真机不渲染
   → UI 图形优先用纯色块或内建资源
7. uGUI 中心锚定 y 向上、设计坐标 y 向下：`pos.y = 540 - 设计y`；IMGUI `CRect` 的 cx 是**中心**不是边缘
8. **不要在编辑器里调 `Screen.SetResolution`**（会卡死播放模式）；编辑器窗口失焦时播放模式可能暂停
9. **编辑器测试与 APK 构建不可并行**（`HandleProjectAlreadyOpenInAnotherInstance` 崩溃，
   需手工删 `Temp/UnityLockfile`）；ShotTest 不能加 `-quit`/`-batchmode`
10. 跑 ShotTest 时**窗口必须保持前台**（否则 GUI 白屏、`[SHOT]` 日志停在 entering play mode）；
    跑完会残留"无标题空场景" → 下次启动按 Play 只有天空盒，**双击 `Assets/Scenes/Main.unity` 即可**
11. **推送前必须先 `git fetch`**（曾 force push 覆盖用户网页编辑的提交）。已推送历史尽量不改写 ——
    v0.42 发完又发现 bug，做法是**直接发 v0.43**，不重写 v0.42
12. `gh release create` 偶发把 release 建成 **Draft 且不附 APK** → 必须 `release view` 核对 assets，
    必要时 `release upload` + `release edit --draft=false`
13. 本机 github.com 偶发不通（`git tag push` 会失败），但 **gh CLI 常可用** →
    用 `gh api repos/.../git/refs -f ref=refs/tags/vX -f sha=<sha>` 建 tag 兜底
14. 含中文的 .bat 在 GBK 控制台闪退 → 批处理保持纯 ASCII；PowerShell 调 gh 的 `--jq` 含引号会报错，用 `--template`
15. **Git Bash 会把 `/MIR`、`/c` 当路径改写** → robocopy/adb 前 `export MSYS_NO_PATHCONV=1`
16. **`Physics.Simulate` 手动步进不派发 `OnCollisionEnter`** → 离线测试统计接触要用
    `Physics.OverlapSphere` 自己检测（初版探针因此一直记 0）
17. **手动步进测试不能直接给白球赋 `rb.velocity`**：`shotSpeed0` 只在 `ApplySpin()` 里赋值，
    直接赋速度会被限速钳到 0（球纹丝不动）。测试里也用 `ApplySpin(dir, speed, 0, 0)` 出杆
18. **布料板拼缝在高速下会改变接触法线**（已知残留）：5m/s 正打角袋时球心弹起约 90mm
    （1.2~3m/s 无此现象），球仍正常落袋。回归红线 0.12m。彻底解决要把布料板换成单个
    MeshCollider（会失去凸体求交稳定性，需重新回归撞库与滚动）

## 九、版本迭代史（均 MuMu 实测验收）

- **v1.0.28** — 首个全链路验收；布纹台呢；双架构 IL2CPP 打通
- **v0.29** — 粘库修复(bounceThreshold 0.5→0.1)；球哑光材质；帧率 120；步长 5→4ms
- **v0.30** — 袋口黑柱下沉与台面平齐；换手等待 18s→3.3s
- **v0.31** — UI 美化；入场动画；设置面板；木纹桌沿
- **v0.32** — HUD 改单杆分；147 横幅；满力 4.6→6.0
- **v0.33** — 入场运镜重做（贝塞尔 + 五次 smootherstep + 注视点平滑）
- **v0.34** — **规则抽成纯函数** `SnookerRules.cs` + RuleTest(30 断言)；修换手回红球、清彩死局等
- **v0.35** — 补齐官方细则：指定彩球、Miss 让对手重打、自由球；回归扩至 44 断言
- **v0.36** — 球在手 D 区摆球 + 加塞杆法（高/低/定杆/左右塞）+ 步长 4→2ms
- **v0.37~v0.40** — 加塞按真台呢定标（`SpinTopK=1.0`/`SpinLowK=2.0` + 硬限速）；步长三档；
  原神风 UI；新增 `Editor/ShotTest.cs` 编辑器截图驱动
- **v0.41** — **袋口真实化**：弧形颚部 + 真洞下坠 + 晃袋（三处结构重做，见第五节）；
  新增 `PhysTest.PocketTest`
- **v0.42** — **清彩阶段切换修正**（"played at" ≠ "potted"）+ 微调步长 0.0035→0.00035 rad
- **v0.43** — **任选彩球按指定球计分**修正；RuleTest 44→58 断言

## 十、当前功能全清单（均已验收）

菜单(贝塞尔入场运镜+淡入) → 设置(帧率/分辨率/阴影/物理步长，滑入动画，持久化) → 开始游戏
→ 瞄准(拖动/微调 ±0.02°/辅助线开关/准线自动指定彩球) → 力度滑条 → 击球(出杆动画)
→ 真实物理(滚动摩擦/库边反弹/**真洞落袋/撞颚晃袋**/粘库修复) → 完整规则(首触/罚分/白球重置/
彩球回点/红球回点/清彩/**指定彩球按指定球计分**/Miss 让对手重打/自由球) →
计分(HUD 单杆分+总分+阵营色块) → 147 提示 → 结算面板

## 十一、遗留 / 可做

- **未实现**：Miss 累计三次判负（Rule 11(c)(i)）、自由球判定的裁判裁量部分（已就地标注）
- **已知遗留**：
  - 加塞圆盘的"白球面"是**方形**（圆形方案在真机始终异常，见踩坑 6）
  - 5m/s 满力正打角袋时球在袋口布料拼缝处弹起约 90mm 后仍进袋（无害，回归红线 0.12m）
  - 球质量 `rb.mass=0.17kg` 与文献不符（同行评审论文用 **140.6g**，142g/170g 都查不到一手来源）。
    因 `a=μg` 与质量无关，只影响撞球动量分配，改动需单独回归，故未动
- 147 横幅已实现但未在真机自然触发（需连续 5 套红黑走位）；清彩/黑球决胜未逐步走完
- 无音效；球杆无贴图（纯色）；彩球无数字贴纸
- APK 约 70MB，其中约 48MiB 是两个**未被引用**的备用中文字体常驻包内（移出 `Resources\` 即可瘦身）
- 仓库可做：GitHub Actions 自动构建、Topics 标签、英文 README
