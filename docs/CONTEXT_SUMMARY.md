# 双人斯诺克 3D —— 项目上下文总结（截至 v0.41，2026-09-23）

> 本文件为完整压缩上下文，供导入 DSH 等 AI 助手继续开发使用。
> 项目根目录：`E:\Snooker`（工作区）；Unity 工程：`E:\Snooker3D`
> **注意**：本机 Bash 工具是 **Git Bash**，路径写作 `/e/Snooker`；执行 .bat 用 `cmd //c tools/sync.bat`

---

## 一、项目概述

基于 **Unity 2022.3.62f3c1** 的安卓**双人同屏斯诺克游戏**。球桌由 **Blender 5.2.1 LTS（Steam 版）**
脚本化建模（标准比赛尺寸 3569×1778mm），物理用 Unity PhysX + 自定义滚动摩擦，规则为
**完整官方斯诺克规则**（WPBSA Section 3，仅剩 2 项细则未实现），带可开关辅助瞄准线、
贝塞尔入场运镜、设置界面（帧率/分辨率/阴影）、单杆分 HUD、147 满分提示。

**已开源**：https://github.com/laoye666-6/MySnooker3D （public / MIT，Release v0.35 含 APK（v0.36 待发布））
**交付物**：`E:\Snooker3D\Builds\Snooker3D.apk`（v0.40，双架构 ARM64+X86_64，IL2CPP，约 70MB，已装 MuMu）

## 二、环境与路径（全部实测有效）

| 项 | 路径 |
|---|---|
| Unity 编辑器 | `E:\Program files\2022.3.62f3c1\Editor\Unity.exe`（Android 模块齐全） |
| Blender | `D:\Program Files\STEAM\steamapps\common\Blender\blender.exe`（5.2.1 LTS） |
| MuMu 模拟器 | `E:\Program files\Netease\MuMu\nx_main\`（adb.exe / MuMuManager.exe） |
| git | `D:\Program Files\Git\cmd\git.exe`（已加入用户 PATH） |
| gh CLI | `C:\Program Files\GitHub CLI\gh.exe`（2.101.0，已登录 laoye666-6） |
| MuMu adb | `127.0.0.1:16384`（1600×900 横屏，Android 15） |
| Unity 工程 | `E:\Snooker3D`（`Assets\Scripts` / `Assets\Editor` 由 sync.bat 同步） |
| **源码母本（权威）** | `E:\Snooker\unity_src\Scripts`（10 个 cs）+ `unity_src\Editor`（3 个） |
| Blender 脚本 | `E:\Snooker\blender\`：make_table / bake_cloth / make_icon / open_table_edit / shrink_pockets |
| 资产产物 | `E:\Snooker\assets\`（table.obj、cue.obj、cloth/wood 贴图、icon.png） |
| 开源仓库 | `E:\Snooker\MySnooker3D`（origin 已配，master） |
| 截图/日志 | `E:\Snooker\shots\`（s01~s45）、`E:\Snooker\logs\` |
| 文档 | `E:\Snooker\README.md`（踩坑 + 版本记录 v0.28~v0.40）、本文件、`DSH_IMPORT_PROMPT.txt` |

包名 `com.snookerlab.snooker3d`；Activity `com.unity3d.player.UnityPlayerActivity`

## 三、代码架构（全部含详细中文注释）

**运行时 `Scripts\`（11 个）：**
- `G.cs` — 全局常量：球桌尺寸(米)、袋口捕获半径、滚动摩擦 0.11、出杆初速 0.55~6.0、
  停判阈值 0.09、置球点、分值/颜色/中文名、ColorOrder；v0.36 加塞物理参数
  (SpinTopK 1.5 / SpinLowK 1.6 / SlipDecel 1.96 / SpinSideK 0.7 等) 与 `ClampToD()/InD()`
- `SnookerRules.cs` — **纯函数规则引擎**（不依赖 MonoBehaviour/物理，可离线单测）：
  输入 `TableState`(出杆前状态) + `ShotFacts`(本杆事实) → 输出 `ShotOutcome`(结算+新状态)。
  文件头按 WPBSA 条款逐条列出依据。含 `BallOn()`/`NextColorOn()`/`OnlyBlackLeft()`/`Evaluate()`
- `GameManager.cs` — 状态机(Menu/Aiming/Rolling/GameOver) + 把规则结果**落地**（加分/回点/
  换手/终局）+ 几何判定 `IsSnookered()`（自由球与 Miss 共用）+ 单杆分 + 147 连击 + 球位快照保护
  + v0.36 `cueInHand`/`DragCueBall()`（球在手 D 区摆球）/`Shoot(dir,power,spinV,spinH)`
- `BallController.cs` — 落袋/下沉/重置/滚动摩擦/碰撞上报；v0.36 **白球自旋模型**
  (`ApplySpin`/`CueRollStep` 滑移摩擦耦合/`CushionKick` 侧塞撞库/`MoveTo`/`ClearSpin`)
- `SpinPad.cs` — v0.36 加塞圆盘控件（拖动小圆点选高杆/低杆/左右塞，uGUI 事件）
- `CueController.cs` — 瞄准角(灵敏度 0.0018rad/px)/力度/出杆动画/暴露 `AimedBall`；
  v0.36 加塞状态 `spinV/spinH` + "球在手"拖球输入（抓白球拖动 vs 拖动瞄准）
- `AimLine.cs` — 辅助线：幽灵球解析几何 + 目标球走向 + 分离线 + 库边反弹 + 暴露 `AimedBall`；
  v0.36 `visible` 字段修幽灵球残留
- `GameCamera.cs` — 贝塞尔弧线入场运镜 + smootherstep + 注视点平滑 + 菜单漂移 + 跟球/全景
- `UIManager.cs` — uGUI 图形 + **IMGUI 文字层**；HUD 单杆分/菜单/设置面板/147 横幅/
  让对手重打提示/自由球与指定球状态/加塞圆盘与"球在手"提示；CRect 中心映射
- `GameSettings.cs` — 帧率(60/90/120/144)/分辨率(50/75/100%)/阴影，PlayerPrefs 持久化

**Editor `Editor\`（4 个）：**
- `BuildGame.cs` — 命令行构建（`-x86only` 调试；**版本号在此改**；写入应用图标；构建后自校验版本）
- `PhysTest.cs` — 离线物理回归（`Run` 开球；`CushionTest` 库边三用例；`SpinTest` 加塞，
  步长 2ms 与运行时一致）
- `RuleTest.cs` — **44 条规则断言**（不走物理、不需模拟器）
- `ShotTest.cs` — v0.38 新增：编辑器内自动截图驱动（时间线出 9 张关键帧，替代每轮装模拟器；
  运行方式与四个坑见 README 踩坑 23）

**规则实现覆盖（v0.35）**：球 on 顺序、Rule 10.3 换手回红球、罚分 max(4,on,涉及球)、
连续两杆打红 7 分、同杆多犯规取最高、犯规杆不计分、彩球回点(高分优先)、红球不回点、
清彩阶段、只剩黑球终局/平分重置、**指定彩球**、**犯规与未击到(Miss)+让对手重打**、
**自由球(Free Ball)**。
**未实现（已就地标注）**：Miss 累计三次判负、自由球判定的裁判裁量。

**v0.36 玩法新增**：**球在手**（开球前/白球落袋后按住白球在 D 区内自由拖动摆放，位置自动
夹取在 D 内并与其它球分离）+ **加塞杆法**（右下角"击球点"圆盘选高杆/低杆/左右塞；
白球自建滑移摩擦-自旋耦合模型，中杆与旧版零回归）；物理步长 4ms→2ms(500Hz)。

## 四、常用命令

```bat
:: 同步源码母本 → 工程（robocopy /MIR，会删除工程内多余 .cs，防重复定义编译失败）
cmd //c E:\Snooker\tools\sync.bat

:: 构建 APK（加 -x86only 只编 x86_64，调试更快；正式版双架构）
"E:\Program files\2022.3.62f3c1\Editor\Unity.exe" -batchmode -quit -nographics ^
  -projectPath "E:\Snooker3D" -executeMethod BuildGame.BuildAndroid ^
  -logFile "E:\Snooker\logs\build.log"

:: 规则回归（改规则后必跑，期望 PASS=44 FAIL=0 + ALL RULES OK）
"E:\Program files\2022.3.62f3c1\Editor\Unity.exe" -batchmode -quit -nographics ^
  -projectPath "E:\Snooker3D" -executeMethod RuleTest.Run -logFile "E:\Snooker\logs\ruletest.log"

:: 物理回归（注意：不能加 -nographics，需要图形设备）
"E:\Program files\2022.3.62f3c1\Editor\Unity.exe" -batchmode -quit ^
  -projectPath "E:\Snooker3D" -executeMethod PhysTest.Run -logFile "E:\Snooker\logs\pt.log"

:: MuMu 安装/启动/截图
"E:\Program files\Netease\MuMu\nx_main\adb.exe" connect 127.0.0.1:16384
"E:\Program files\Netease\MuMu\nx_main\adb.exe" -s 127.0.0.1:16384 install -r -t E:\Snooker3D\Builds\Snooker3D.apk
"E:\Program files\Netease\MuMu\nx_main\adb.exe" -s 127.0.0.1:16384 shell am start -n com.snookerlab.snooker3d/com.unity3d.player.UnityPlayerActivity

:: Blender 重新生成美术资产
blender.exe -b -P E:\Snooker\blender\make_table.py   （再 bake_cloth.py，然后拷 assets → 工程）
```

**操作注意**：cmd 的 `timeout /t` 在重定向环境下立即返回，等待须用
`powershell -Command "Start-Sleep -Seconds N"`。MuMu 会因宿主睡眠被关闭，
连接被拒时先 `MuMuManager.exe control -v 0 launch` 重启（约 55 秒）。
IL2CPP 全量构建 40~60 分钟；纯资产改动走增量很快。

## 五、版本迭代史（均 MuMu 实测验收）

- **v1.0.28** — 首个全链路验收（进游戏/击球/计分 6:4）；布纹台呢；双架构 IL2CPP 打通
- **v0.29** — 粘库修复(bounceThreshold 0.5→0.1，编辑器三用例复现+回归)；袋口归位+缩小；
  球哑光材质(粗 0.65)；帧率 120；步长 5→4ms；中文 UI 偏移修复(中心锚定+k 映射)
- **v0.30** — 袋口黑柱下沉与台面平齐；换手等待 18s→3.3s
- **v0.31** — UI 美化(投影/描边按钮/金色装饰条/阵营色块)；入场动画；设置面板；木纹桌沿
- **v0.32** — HUD 改单杆分(金色大字)；红黑连击 5 套弹 147 横幅；满力 4.6→6.0；Blender 图标
- **v0.33** — 入场运镜重做（贝塞尔弧线 + 五次 smootherstep + 注视点时间平滑）
- **v0.34** — **规则修正为完整斯诺克**：①换手后回红球(Rule 10.3) ②清彩期犯规落袋的回点
  死局 ③罚分按 Rule 10（含只剩黑球空杆罚 7）④只剩黑球终局按 Rule 4。
  规则抽成纯函数 `SnookerRules.cs` + `RuleTest.cs`(30 断言)。另修：PhysTest 未 try/finally
  导致物理设置写坏、sync.bat 只增不删、147/设置面板 UI 双重缩放、HUD 非 16:9 被裁、
  力度滑条被击球键盖住、HUD 画在菜单之上、VSync 使帧率档失效、运镜硬切、触屏瞄准中断
- **v0.35** — **补齐官方细则**：指定彩球(Rule 3(f)(i)(b)，准线指向自动指定，参与罚分)、
  犯规与未击到 Miss(Rule 11(b)，可选让对手重打)、自由球 Free Ball(Rule 12，按真实球 on 计分且回点)、
  新增 `IsSnookered()` 几何判定与 `RespotRed()`；回归扩至 **44 断言全通过**
- **v0.36** — **球在手 D 区摆球**（修 bug：开球/白球摔袋后白球原先无法移动；现在按住白球可在
  D 区内自由拖动，位置夹取在 D 内并自动分离重叠）+ **加塞杆法**（右下角"击球点"圆盘选高杆/低杆/
  左右塞；白球自建滑移摩擦-自旋耦合模型，高低杆用非对称系数避免"略低杆=无旋"死区；中杆零回归）
  + **物理步长 4ms→2ms(500Hz)** + 修 v0.35 幽灵球残留（关辅助线后仍显示，似"多一颗白球"）
  + 新增 `PhysTest.SpinTest`（四档杆法排序 + 侧塞数学 + 直线轨迹断言全通过）
  + 主相机补 `MainCamera` 标签（`Camera.main` 原为 null，拖球需要它做屏幕→台面反投影）
- **v0.37~v0.40**（当前，同批发布）— **① 加塞按真实台呢定标**：依据球-台呢滑动摩擦
  μ≈0.2（滚动阻力 0.005~0.015，本工程 RollDecel=0.11≈0.011），球重 0.17kg 对 a=μg 无影响
  （质量约掉）→ 加速度只取 μg、严禁自旋凭空造加速度；高杆上限 ω≤1.25×(v/r)（杆头击点极限
  0.4~0.5r，`ω·r/v=(5/2)(h/r)`），低杆极限 -1.0×(v/r)。旧版 SpinTopK=1.5 给到 2.5 倍（"大力
  高杆加速过快"根因）、SpinLowK=1.6 只有 0.6 倍（"低杆不明显"）。现取 SpinTopK=1.0/SpinLowK=2.0，
  并新增**硬限速**：白球速度任何时刻 ≤ 出杆初速（只钳速度不动自旋，否则会清零跟进效果）。
  副产物 `spinV≈-0.5` → ω≈0 = 真实**定杆 stun**。**② 物理步长可调** 0.5/1/2ms 三档
  （Time.fixedDeltaTime 立即生效 + PlayerPrefs 持久化），设置面板扩为四行。
  **③ 原神风 UI**：金描边+深藏青底+四角菱饰钉（主按钮带宝石菱）、金色滑条+菱形手柄、
  面板行间金线、菜单标题双翼金线、HUD 顶栏金线、加塞盘 12 点金刻度。
  **④ 新增编辑器截图驱动** `Editor/ShotTest.cs`（一条时间线出 9 张关键帧，替代每轮装模拟器；
  四个坑见 README 踩坑 23）。回归：规则 44/44、加塞 9 项、库边 3/3、开球物理全过。
  **入场 CG 已取消不做**。

## 六、踩坑记录（重要教训，勿重蹈）

1. **MuMu 只认 arm64-v8a/x86_64**；Unity Mono 只有 ARMv7 → 必须 IL2CPP + ARM64/X86_64
2. **IL2CPP 引擎裁剪**会剥掉 SphereCollider → `stripEngineCode=false` + `Assets/link.xml`
3. **白球与棕球同点重生** → PhysX 炸膛(球飞 38 米) → 白球放 D 区内偏移 (BaulkX-0.09, 0.09)
4. **休眠刚体直接赋 velocity 不动** → 出杆/放球前必须 `WakeUp()`
5. **编辑器跑过 `autoSimulation=false` 会把 SimulationMode 持久化进工程打进包**，
   设备上物理完全不步进 → 资产保持 `m_SimulationMode: 0`，运行时再强制一次；
   测试代码必须 try/finally 恢复；手动步进枚举是 `SimulationMode.Script`
6. **uGUI 动态字体部分机渲染空白** → 文字一律 IMGUI；中文用内嵌 DroidSansFallback(Apache-2.0)
7. **OBJ 导入后整桌合并为单个 "default" 网格、5 材质槽** → 按【材质名】匹配且**全槽**赋值
8. **Blender OBJ 导入的物体原点在世界原点** → 按物体 scale 缩放会把部件拉向桌心
   （袋口位移事故根因）；改尺寸应改 make_table.py 重新生成
9. **低速撞库粘库** = PhysX bounceThreshold 吞掉低速法向弹性 → 阈值设 0.1
10. **uGUI 中心锚定 y 向上、设计坐标 y 向下**：`pos.y = 540 - 设计y`（设置面板曾整体上下镜像）；
    IMGUI `CRect` 的 cx 是矩形**中心**不是边缘（曾致 HUD 文字出屏）
11. **换手等待久**的隐藏元凶：角速度判停 + angularDrag 0.08（原地旋球拖几十秒）
    → 只看线速度 + 4 秒后放宽阈值 0.22（但慢爬球够得着袋口则继续等）
12. **规则判定绝不能写在物理/UI 代码里** ← v0.33 两大规则 bug 长期潜伏的根因。
    已抽成纯函数 + RuleTest 离线断言（44 条），改规则必须跑它
13. **推送前必须 `git fetch`**：曾用 `--force-with-lease` 改署名，**覆盖了用户在 GitHub
    网页编辑 README 的提交**（已 cherry-pick 找回）。已推送历史尽量不改写
14. **PowerShell 调 gh**：`--jq` 表达式含引号会被 PS 拆参数报 "accepts 1 arg(s), received N"
    → 改用 `--template '{{...}}'`，或写成 .ps1 文件执行而非 -Command 内联
15. **含中文 + `chcp 65001` 的 .bat 在 GBK 系统会闪退** → 批处理保持纯 ASCII
16. **PowerShell `Set-Content -Encoding ASCII`** 会毁掉含中文的 UTF-8 文件（勿用于改源码）
17. MuMu 窗口失焦/宿主睡眠 → 应用暂停（输入延迟、画面冻结）或模拟器被关闭
18. 本机 Bash 是 Git Bash：`sed -i`/`grep -a` 可用，但 cmd 内建命令需 `cmd //c`

## 七、当前功能全清单（均已验收）

菜单(贝塞尔入场运镜+淡入) → 设置(帧率/分辨率/阴影，滑入动画，持久化) → 开始游戏(相机俯冲转场)
→ 瞄准(拖动/微调/辅助线开关/准线自动指定彩球) → 力度滑条 → 击球(出杆动画)
→ 真实物理(滚动摩擦/库边反弹/袋口捕获/粘库修复) → 完整规则(首触/罚分/白球重置/彩球回点/
红球回点/清彩/黑球终局/指定彩球/Miss 让对手重打/自由球) → 计分(HUD 单杆分金色大字+总分+阵营色块)
→ 147 提示(5 套红黑底部弹出) → 结算面板

## 八、遗留 / 可做

- 未实现：Miss 累计三次判负（Rule 11(c)(i)）、自由球判定的裁判裁量部分
- 147 横幅已实现但未在真机自然触发（需连续 5 套红黑走位）；清彩/黑球决胜未逐步走完
- 无音效；球杆无贴图（纯色）；彩球无数字贴纸；图标高光在 512px 下有轻微像素感
- GitHub 仓库可做：Actions 自动构建、Topics 标签、英文 README
