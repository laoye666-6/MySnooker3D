# 双人斯诺克 3D —— 项目上下文总结（截至 v0.53，2026-10-04）

> 本文件为完整压缩上下文，供导入 AI 助手继续开发使用。
> 项目根目录：`E:\Snooker`（工作区）；Unity 工程：`E:\Snooker3D`
> **注意**：本机 Bash 工具是 **Git Bash**，路径写作 `/e/Snooker`；执行 .bat 用 `cmd //c "E:\Snooker\tools\sync.bat"`（绝对路径）
> Git Bash 会把 `/MIR`、`/c` 这类参数当路径改写，跑 robocopy/adb 前先 `export MSYS_NO_PATHCONV=1`
> **但跑 .bat 时不要带该变量**（会把 `cmd //c` 的转换也禁掉 → cmd 进交互模式、bat 根本不执行）

---

## 一、项目概述

基于 **Unity 2022.3.62f3c1** 的安卓**双人同屏斯诺克游戏**。球桌由 **Blender 5.2.2 LTS（Steam 版）**
脚本化建模（标准比赛尺寸 3569×1778mm），物理用 Unity PhysX + 自定义滚动摩擦 + 自建白球自旋模型，
规则为**完整官方斯诺克规则**（WPBSA 2024-25，仅剩 2 项细则未实现），带可开关辅助瞄准线、
贝塞尔入场运镜、设置界面、单杆分 HUD、147 满分提示、加塞杆法、真实袋口物理、
**碰撞音效（v0.46）**、**iOS 液态玻璃 UI（v0.44~v0.47）**、**台球厅环境场景（v0.49~v0.50）**、
**胜利结算动画（v0.51：卡片 Q 弹/比分跳数/彩纸/胜利号角）**。

**已开源**：https://github.com/laoye666-6/MySnooker3D （public / MIT，Release 已到 v0.50，含 APK）
**交付物**：`E:\Snooker3D\Builds\Snooker3D.apk`（v0.50，双架构 ARM64+X86_64，IL2CPP，约 75MB）

## 二、环境与路径（全部实测有效）

| 项 | 路径 |
|---|---|
| Unity 编辑器 | `E:\Program files\2022.3.62f3c1\Editor\Unity.exe`（Android 模块齐全） |
| Blender | `D:\Program Files\STEAM\steamapps\common\Blender\blender.exe`（5.2.2，已配 MCP 插件协议 13） |
| MuMu 模拟器 | `E:\Program files\Netease\MuMu\nx_main\`（adb.exe / MuMuManager.exe） |
| git / gh | `D:\Program Files\Git\cmd\git.exe`；`C:\Program Files\GitHub CLI\gh.exe`（已登录 laoye666-6） |
| MuMu adb | `127.0.0.1:16384`（1600×900 横屏，Android 15） |
| Unity 工程 | `E:\Snooker3D`（`Assets\Scripts` / `Assets\Editor` 由 sync.bat 同步） |
| **源码母本（权威）** | `E:\Snooker\unity_src\Scripts`（17 个 cs）+ `unity_src\Editor`（4 个）；另有 `shaders\LiquidGlass.shader`、`shaders\GlassBlur.shader` 与 `android\AndroidManifest.xml` 母本（sync.bat 只同步 *.cs，需手工拷入工程） |
| Blender 脚本 | `E:\Snooker\blender\`：make_table / bake_cloth / make_icon / open_table_edit / render_pockets / shrink_pockets（废弃）+ **pool_hall.blend / pool_hall_README.md（台球厅场景）+ hall_lift_export.py / hall_export_fbx.py / hall_verify_fbx.py（v0.52 抬灯与 FBX 导出/校验）** |
| 资产产物 | `E:\Snooker\assets\`（table.obj、cue.obj、cloth/wood 贴图、icon.png、preview_*.png、`audio\sfx_*.wav` 音效 10 个（含 sfx_win_1 胜利号角）、`hall\`（台球厅 fbx/glb + CC0 资源 + tex_/model_ 子目录）） |
| 开源仓库 | `E:\Snooker\MySnooker3D`（origin 已配，master；`docs\DEVELOPMENT.md` = 详细开发文档） |
| 辅助脚本 | `E:\Snooker\tools\`：sync.bat / m.bat / regress.sh / make_audio.py（音效合成）/ fetch_hall_assets.py（台球厅 CC0 资源下载）+ 4 个编辑器 GUI 自动化 ps1 |
| 截图/日志 | `E:\Snooker\shots\`（编辑器截图 `shots\editor\`、台球厅 `hall_*.png`）、`E:\Snooker\logs\` |
| 文档 | `E:\Snooker\README.md`（踩坑 48 条 + 版本史）、本文件、`DSH_IMPORT_PROMPT.txt` |

包名 `com.snookerlab.snooker3d`；Activity `com.unity3d.player.UnityPlayerActivity`

## 三、代码架构（全部含详细中文注释）

**运行时 `Scripts\`（17 个）：**
- `G.cs` — 全局常量：球桌尺寸(米)、袋口几何（v0.41）、滚动摩擦 0.11、出杆初速 0.55~6.0、
  停判阈值 0.09、置球点、分值/颜色/中文名、ColorOrder、加塞物理参数、
  工具函数 `ClampToD()/InD()/InPocket()/PastCornerPocketCenter()`
- `SnookerRules.cs` — **纯函数规则引擎**（不依赖 MonoBehaviour/物理，可离线单测）：
  输入 `TableState`(出杆前状态) + `ShotFacts`(本杆事实) → 输出 `ShotOutcome`(结算+新状态)。
  文件头按 WPBSA **2024-25 版**条款逐条列出依据（3(g)/3(h)/10/11/12/4/7）
- `Bootstrapper.cs` — 纯代码搭整个场景：物理参数 → 灯光 → 相机 → **台球厅加载（v0.50：
  Resources/Models/pool_hall.fbx 实例化 + 23 材质按名重建 + 室内光照；v0.52 已删灯罩运行时上移，改由 Blender 模型自带高度）** →
  球桌模型与碰撞体（布料板挖真洞/库边/斜颚楔块/袋内衬）→ 22 颗球 → 球杆与瞄准线 → 中文 UI → Sfx.Init
- `GameManager.cs` — 状态机(Menu/Aiming/Rolling/GameOver) + 把规则结果落地（加分/回点/
  换手/终局）+ 几何判定 `IsSnookered()`/`BallsOn()` + 单杆分 + 147 连击 + 球在手 `DragCueBall()`
  + ChoicePending 门禁（Miss 选框未选择前禁止击球，v0.45）
  + **v0.53：`RecordShotStart()` 每杆起点记录（球位/落袋/比分/阶段/击球权）、Rule 14(b) 三选一
  （当前位置/原始位置重打/自己打）、双方同意的"复位上一杆"（两人都同意才回退整盘）**
- `BallController.cs` — 落袋/下沉/重置/滚动摩擦/碰撞上报；白球自旋模型（`CueRollStep` 滑移摩擦
  耦合 / `CushionKick` 侧塞撞库）+ `PocketLaunchGuard()`（袋口防弹射）+ **v0.46 碰撞音效上报
  `CollisionSfx`（撞球→Sfx.Ball、撞库/袋衬→Sfx.Cush，法向相对速度，实例 ID 防双响）**
  + **v0.53 `RestoreTo(pos, wasPotted)` 精确复位（含落袋态还原）**
- `CueController.cs` — 瞄准角(灵敏度 0.0018rad/px)/力度/出杆动画/加塞状态/拖球；`NudgeStep`=0.00035
- `AimLine.cs` — 辅助线：幽灵球解析几何 + 目标球走向 + 分离线 + 库边反弹；暴露 `AimedBall`
- `SpinPad.cs` — 加塞圆盘控件
- `GameCamera.cs` — 贝塞尔弧线入场运镜 + smootherstep + 注视点平滑 + 菜单漂移 + 跟球/全景；
  **v0.50 入场弧线压到 2.45~2.55m（台球厅天花板 y=2.68，旧 4.7m 会穿顶）**
- `UIManager.cs` — uGUI 图形 + IMGUI 文字层；HUD/菜单/设置面板（五行：帧率/分辨率/阴影/步长/音效）/
  147 横幅/让对手重打提示/加塞圆盘/微调按钮；字号 FontXxx 常量阶梯；
  **胜利结算动画时间线（v0.51：卡片 Q 弹/比分跳数/延迟按钮，ShowGameOver 传入 maxBreak）**
- `UIGlass.cs` — 液态玻璃零贴图顶点网格（圆角/胶囊/圆形/描边环）；OnPopulateMesh 推
  `_RectHW/_CornerR` 给 shader SDF；`UseLiquid()` 全参入口 + `UseRefraction/UseBlur` 预设 +
  `UseShadow`/`AttachShadow()` 软投影（v0.47）
- `UIJelly.cs` — 按钮 Q 弹（按下压扁/松手欠阻尼弹簧过冲回弹）
- `UIConfetti.cs` — **胜利彩纸（v0.51）**：零贴图顶点网格（72 片一个 draw call），
  Launch(burst)/ResetState；iOS 系统色，终端下落+正弦摆动+自旋，出屏消亡后零开销
- `Haptics.cs` — 按钮触感（安卓 VibrationEffect，按下轻点/主按钮确认重点）
- `Sfx.cs` — **碰撞/击球音效（v0.46）**：击球/球碰球/球碰库三类各 3 个合成变体
  （Resources/Audio 预导入，`tools\make_audio.py` 生成）；响度随法向撞击速度幂函数映射、
  音调随速度升高+微抖；同类最小间隔节流 + 实例 ID 防双响 + 10 路音源池；2D 播放；
  受 GameSettings.SfxOn 开关控制；**Win() 胜利号角（v0.51，sfx_win_1）**
- `GlassSceneCamera.cs` — 副相机：隔帧绘半分辨率 RT（_GlassScene 清晰层）+ 四分之一分辨率
  两轮分离高斯（_GlassBlur 磨砂层，GlassBlur.shader，RT→RT Blit 确定性）
- `GameSettings.cs` — 帧率(60/90/120/144)/分辨率(50/75/100%)/阴影/物理步长/音效开关(v0.46)，
  PlayerPrefs 持久化

**Editor `Editor\`（4 个）：**
- `BuildGame.cs` — 命令行构建（`-x86only` 调试；**版本号在此改**，当前 0.50/50）
- `RuleTest.cs` — **65 条规则断言**（不走物理、不需模拟器）
- `PhysTest.cs` — 离线物理回归四入口：`Run` 开球 / `CushionTest` 库边 / `SpinTest` 加塞 / `PocketTest` 袋口
- `ShotTest.cs` — 编辑器内自动截图驱动（11 张关键帧；不能加 -quit/-batchmode，窗口须前台）

## 四、常用命令

```bat
:: 同步源码母本 → 工程（robocopy /MIR，只镜像 *.cs）
cmd //c "E:\Snooker\tools\sync.bat"
:: 同步到开源仓库工程：先 set SNOOKER_PROJECT=E:\Snooker\MySnooker3D\UnityProject 再跑 sync.bat

:: 全量回归（串行 5 道；Unity 工程锁独占不能并行；期望全绿）
bash E:\Snooker\tools\regress.sh
::   RuleTest 65/65 | PhysTest.Run/CushionTest(3 bounced=True)/SpinTest(ALL SPIN OK)/PocketTest(PASS=6)

:: 构建 APK —— 【踩坑 44】构建前必须：taskkill adb.exe java.exe（MuMu 的 adb/残留 Gradle
::   daemon 会挡住 SDK 探测死等）+ 删 Temp/；且代理须在线（Unity 联网 fetch SDK 仓库索引，
::   断网时卡 "66% Fetch remote repository"）。IL2CPP 全量 40~60 分钟，增量 3~7 分钟。
"E:\Program files\2022.3.62f3c1\Editor\Unity.exe" -batchmode -quit -nographics ^
  -projectPath "E:\Snooker3D" -executeMethod BuildGame.BuildAndroid -logFile "E:\Snooker\logs\build.log"

:: 编辑器自动截图（带窗口，不加 -quit/-batchmode，跑时保持前台+TOPMOST 钉窗；偶发白屏卡死→杀进程删锁重试）
Unity.exe -screen-width 1600 -screen-height 900 -projectPath E:\Snooker3D -executeMethod ShotTest.Run -logFile E:\Snooker\logs\shot.log

:: 重新生成音效 WAV（改音色后；再拷 assets\audio\*.wav → Assets\Resources\Audio\）
python E:\Snooker\tools\make_audio.py

:: 台球厅场景（Blender MCP 驱动或无头）：
::   重新导 FBX（剔主桌防 z-fight）：blender -b pool_hall.blend --python-expr "..."
::   CC0 资源重下：python E:\Snooker\tools\fetch_hall_assets.py
::   导出后拷 assets\hall\pool_hall.fbx → Assets\Resources\Models\（命名 pool_hall.fbx）

:: MuMu 安装/启动/截图（Git Bash 下 export MSYS_NO_PATHCONV=1；路径写 E:/ 形式）
"E:\Program files\Netease\MuMu\nx_main\adb.exe" connect 127.0.0.1:16384
adb -s 127.0.0.1:16384 install -r -t E:/Snooker3D/Builds/Snooker3D.apk
adb shell am start -n com.snookerlab.snooker3d/com.unity3d.player.UnityPlayerActivity
adb shell screencap -p /sdcard/a.png && adb pull /sdcard/a.png E:/Snooker/shots/x.png
:: MuMu 被关（宿主睡眠/杀 adb 后）→ MuMuManager.exe control -v 0 launch 重启（约 55 秒）
```

## 五、近五版改动（v0.49~v0.53）

### v0.53 —— 复选项 + 玻璃更透 + 球在手通知 + 袋口库边（2026-10-04）
- **① 球在手提示移到左上**：原居中胶囊（1100×52）横跨台面挡视线 → 左侧紧凑胶囊（660×52，
  设计 x 36~696、左对齐），与顶栏/中央提示互不重叠。
- **② 液态玻璃去白边、更透**：LiquidGlass.shader 去饱和 0.22 → `_Desat` 可调、`_Lift` 补上
  真正可调（原被 UseLiquid 写死 0.10）；`UseRefraction` 的 `_Frost` 0.36→0.08、`_Crisp` 提高、
  `_Sheen/_Rim/_EdgeAlpha/_SpecInt` 逐项下调（按钮/顶栏/胶囊去白晕）。**踩坑 48**：去白边≠把
  面板填充调透——台球厅很暗，面板一透就变暗、墨字读不清；边缘光学与面板填充要分开调
  （大面板 UseBlur 保留中度奶白 0.20 + 浅色板填充）。另修 `pow(band,6)` 未钳底数（踩坑 39）。
- **③ 每杆记录 + Rule 14(b) 原始位置重打 + 双方同意复位**：`GameManager.RecordShotStart()` 在
  Shoot 前抓完整起点（球位/落袋/比分/单杆分/红球/阶段/击球权）；Miss 选框扩为**三选一**
  （当前位置/原始位置=整盘复位保留罚分/我自己打）；新增 HUD「复位上一杆」按钮 → 双方确认框
  （玩家1/2 各自同意·不同意，两人都同意才回退含比分）。纯函数 `ReplayChooser/ReplacementApproved`。
- **④ 袋口库边下部"未渲染"修复**：**踩坑 47**——台呢在袋口被布尔挖洞、库边底恰在 z=0，低位
  视角能看穿库边端头下方露出木框暗井（资产本身闭合无缺面）。`make_table.py` 给库边加**下摆**
  （底边 z=-0.055）+ 袋井 0.40→0.44，重导 table.obj。排查法：低机位渲染（render_pocket_diag.py）。
- 回归：规则 **65/65**（+7 断言）、库边 3×bounced、ALL SPIN OK、袋口 PASS=6；ShotTest 16 帧。

### v0.52 —— 抬高台球厅吊灯（相机不再被灯挡，2026-10-03）
- **问题**：跟球机位高 1.18m，灯罩下沿（v0.50 Unity 补丁 +0.30 后）约 1.15m → 视线被灯罩侵入。
- **处置（改 Blender 权威源，模型即最终位置）**：罩体 z 0.79~1.05 → **1.54~1.80**（下沿 1.60），
  吊杆以天花端 2.68 为锚缩短（1.80~2.68），灯下方面光同步上移 0.88→1.63；
  脚本 `blender\hall_lift_export.py`（抬灯+导出一体）。
- **Unity 侧**：删 v0.50 的 `Shade_* +0.30` 运行时补丁；点光抬到 y=1.45、强度 1.4→2.8
  （距离平方补偿），`remapped=99` 不变。
- **踩坑 46（Blender FBX 导出）**：`use_selection` 只选根对象会漏件——`object_types={'MESH'}`
  下导出器不遍历空物体父级，绿椅（PROP_GreenChair_* 父级）+ Poly Haven 叶片共 10 件被静默
  跳过（99→89）。**改逐对象选中**（仅排除主桌子树）；导出后跑 `blender\hall_verify_fbx.py`
  反查。另：改 `location` 后 `matrix_world` 需 `view_layer.update()` 才刷新。
- ShotTest 验证：跟球/Rolling 机位均不再见灯罩遮挡。

### v0.51 —— 胜利结算动画（2026-10-03）
- 终局演出时间线（UIManager `overT`）：磨砂淡入 → 0.10s 胜者玻璃卡片 Q 弹入场
  （弹簧 0.72→1 过冲 ~4%）→ 0.40s 比分跳数 1.1s（胜者阵营色）→ 0.45s 彩纸 1.6s →
  0.55s "再来一局"延迟淡入（淡入完成才开射线）。
- **UIConfetti.cs（新）**：零贴图顶点网格彩纸（一个 MaskableGraphic、72 片一个 draw call，
  iOS 系统色，终端下落+摆动+自旋，静止期零重建）。
- GameManager 新增 `maxBreak[2]`，结算卡加"最高单杆 X : Y"行。
- Sfx.Win()：make_audio.py 合成 C 大调三音上行号角 `sfx_win_1.wav`（受 SfxOn 控制）。
- **踩坑 45**：按钮软投影（AttachShadow）是按钮兄弟节点，按钮延迟淡入时投影先暴露成
  灰斑 → 按钮+投影装进同一个容器（AgainGroup），CanvasGroup 挂容器。
- 编辑器偶发播放模式时间线冻结（Bootstrap 后 ShotDriver 无心跳）→ 杀进程删 Temp 重试
  （复验第 4 次过）；编辑器极低帧率下弹簧/彩纸 dt 钳制会放慢真实时间观感，真机无此问题。

### v0.50 —— 台球厅集成进游戏（2026-10-02）
- 修复 v0.49"安装后场景没变"（台球厅只存在于 Blender，未接入 Unity）：
  GLB 需 Unity 插件 → 改导 **FBX 内嵌贴图**；场景模型**剔除主桌**（与 table.obj 重合会
  z-fight），只含房间+两侧背景桌 → `Resources/Models/pool_hall.fbx`。
- **FBX 内嵌贴图真机不生效**（踩坑 7 的 FBX 版）→ Bootstrapper 按材质名重建 23 个 Standard
  材质（Blender 19 + GLTF 4 个名）+ diffuse 贴图入 `Resources/Textures/hall/`，
  实测 `remapped renderers=99`。灯罩整体上移 0.30（跟球机位不再被挡）。
- 室内光照：主光 1.05→0.45 / 补光→0.15 / 环境压暗 + 每桌吊灯位（±7.2/0, y=0.80）暖色点光
  1.4/range6.5。垫底地板 -0.72→-0.9 防共面闪烁。
  （v0.52 更新：点光改 y=1.45、强度 2.8；罩体抬高见下。）
- MuMu 实测：厅完整入景（背景桌/扶手椅/盆栽/暖色地板），开球/音效/计分零回归。

### v0.49 —— 台球厅场景（Blender 资产轮，2026-10-02）
- `blender\pool_hall.blend`（贴图打包自包含 31MB）+ `assets\hall\pool_hall.glb`（29MB/186 节点）：
  三桌台球厅（21×10m 房间/暗绿墙+墙裙/旧木地板），每桌低垂长条吊灯（0.85m/300W 暖光）、
  球杆架/记分牌/挂钟/海报/绿椅×4/扶手椅/盆栽×4，**无人物**；台呢 z=0 与游戏物理一致。
- 免费资源 Poly Haven CC0（`tools\fetch_hall_assets.py` 直连 API，UA 必设否则 403）。
- Blender MCP 全程驱动（插件已升级协议 13：`uvx mcp-for-blender install-addon` + 重启 +
  `--python-expr` 自启服务器）。**Blender 坑**：中文 UI 节点名本地化（材质脚本用 socket
  identifier 访问）；OBJ 导入器已自动转 Z-up（再转 90° 球桌立起）；glTF 导出器静默跳过
  导入模型网格 → JOIN 进程序化网格解决，导出后解析 GLB JSON 验证节点齐全。

### v0.48 —— HUD 顶条圆角 + 击球点文字改黑（2026-10-02）
- TopPanel 圆角 0→28；加塞盘标题「击球点」Ink2→Ink 黑。

### v0.47 —— 液态玻璃重做（2026-10-01，参考 DeepSeek 风格稿）
- **踩坑 42（修）**：v0.44~45 的 shader 引用未定义变量 `spec` → 整 Pass 编译失败 →
  `Fallback "UI/Default"` 静默平色掩盖两版。**改 shader 后必须确认 Console 无 "Shader error"。**
- **LiquidGlass.shader 重写**：圆角矩形 SDF（`_RectHW/_CornerR`）→ 边缘距离/法线/切向；
  磨砂主体采 `_GlassBlur`；边缘**法向透镜**（lens·band² 把界外内容拉进来）+ **切向拉丝**
  （streak·band² 5 点拖影，大面板 90px/控件 44px）；顶部 sheen、底部厚度阴影、rim 细亮边、
  去饱和+奶白 frost；`_SoftMode` 软边分支（投影用）；游灯光源降为点缀。
- **GlassBlur.shader** 新增：9 点分离高斯；GlassSceneCamera 隔帧对上帧 RT 做两轮 → `_GlassBlur`。
- UIGlass：`UseLiquid()` 全参入口 / `AttachShadow()` 软投影（挂按钮/面板/力度胶囊）；
  独立淡入淡出的提示胶囊/147 横幅不加投影。
- **踩坑 43**：MuMu GLES3 不渲染多数玻璃元素（自带 CanvasGroup 的例外），真机正常 →
  **玻璃验收 = 编辑器截图 + 真机，MuMu 不作数**；定位 = shader 临时输出顶点色（踩坑 39 法）。

### v0.46 —— 碰撞/击球音效（2026-10-01）
- 三类音效（击球嗒/球碰球咔/球碰库噗）各 3 变体，`tools\make_audio.py` 程序化合成
  （阻尼正弦+低通噪声，确定性可复现）→ WAV 预导入 Resources/Audio（**不做运行时
  AudioClip.Create**，踩坑 25 教训）。
- 响度 = Lerp(Min,Max,(v/VRef)^幂)（Ball 0.6/Cush 0.7/Cue 1.0）；音调 = Min+Span·√(v01)±2%；
  速度取**接触法向相对速度**（|Dot(relativeVelocity, normal)|）；轻触门槛 0.12/0.15 m/s。
- 节流 25~50ms + 实例 ID 防双响 + 10 路音源池；设置面板第五行「音效」（键 `snk_sfx`）。

## 六、物理要点（容易改错，改动前必读）

1. 白球自旋（加塞）**不能靠 PhysX**，是 `BallController.CueRollStep` 自建滑移摩擦模型。
   三条硬约束：① 白球物理材质摩擦 0；② 白球 angularDrag=0；③ 非对称系数
   （`G.SpinTopK=1.0`/`SpinLowK=2.0`），否则中低杆是"无旋滑行"死区。
2. 定标：球-台呢 μ≈0.2；杆头击点极限 h≤0.5r → 高杆≤1.25×(v/r)、低杆≥-1.0×(v/r)。
   **白球速度任何时刻不得超过出杆初速**（用户明确要求，代码有钳制；只钳速度不动自旋）。
3. spinV=spinH=0（中杆）必须与旧版逐帧一致 —— 加塞实现正确性的第一道关。
4. 低速粘库 = `Physics.bounceThreshold` 太大 → 0.1。
5. 白球不可与棕球同点重生（PhysX 炸膛）；赋速度前必须 `WakeUp()`。
6. 袋口四条硬约束（v0.41）：① 颚弧与鼻线相切（手感不回归）；② 台面挖真洞 127 块板，
   PocketLaunchGuard 钳袋口弹射（只 y≤8cm 生效）；③ 袋内衬 30mm 厚、开口低(-0.015)/
   后侧高(+0.045)、内径≥洞口+36mm、defaultMaxDepenetrationVelocity=0.6；④ 落袋判据
   `G.InPocket` 三层，球一进洞口就成立。
7. 布料板不能比台面多留一圈；角袋布料朝台面外敞开。

## 七、规则要点（v0.42/v0.43 真 bug 教训）

1. 规则改动**必须**走 `SnookerRules.cs` + RuleTest 断言，不许写进物理/UI 代码。
2. **逐字读动词**：played / potted / struck 是三种不同条件（"a colour has been played at"
   只要求击打过、不要求打进）。
3. 凡出现"指定的球""球 on"限定词，计分前**比对一次**对象是否匹配。
4. 条款号按 2024-25 版核对：球 on 顺序 = 3(g)/(h)；ball on 定义 = S2 R11；罚分 = S3 R11；
   犯规判定 = R10；Miss = R14；自由球 = R12。旧编号 3(e)/3(f)(ii)/10.3 不存在。
5. Miss 选择权门禁（v0.45）：`ChoicePending` 在选框未处理时拒绝出杆（Rule 13/14(b)）。

## 八、液态玻璃要点（v0.47 现行，改动前必读）

1. UIGlass 零贴图顶点网格（**真机 UI 禁运行时贴图**，踩坑 25）；uv0=[-1,1] 局部坐标 +
   OnPopulateMesh 推 `_RectHW/_CornerR`（SDF 用；滑条等变尺寸元素随重建刷新）。
2. 双场景源：_GlassScene（半分辨率清晰）/ _GlassBlur（四分辨率磨砂）都由 GlassSceneCamera
   副相机产出。**GrabPass/命令缓冲/OnRenderImage 在 MuMu GLES3 均间歇黑帧——多相机 + 
   RT→RT Blit 是唯一确定性方案，别再试。**
3. GLES 铁律：pow() 底数 max(…,1e-4)（踩坑 39）；**Fallback 吞 shader 编译错误**（踩坑 42）；
   **MuMu 不作玻璃验收环境**（踩坑 43）。
4. 参数入口 `UIGlass.UseLiquid()`；UseRefraction（控件）/UseBlur（大面板）为预设映射。
   软投影 `AttachShadow()` 只挂随面板 CanvasGroup 淡入淡出的元素。
5. 设置弹窗背景渐模糊 = SettingsBlurBg 全屏磨砂层随 settingsCG 淡入。

## 九、踩坑精选（完整 48 条见 README「踩坑记录」）

只列仍会影响新改动的：

1. MuMu 只认 arm64-v8a/x86_64 → 必须 IL2CPP 双架构；stripEngineCode=false + link.xml
2. 工程物理资产 `m_SimulationMode` 必须为 0；测试代码 try/finally 恢复（曾致真机物理全冻）
3. uGUI 动态字体部分机渲染空白 → 文字一律 IMGUI + 内嵌 DroidSansFallback
4. **运行时创建的 UI 贴图真机不可靠**（Sprite.Create/Texture2D/PNG/FBX 内嵌均中招）→
   纯色块、顶点网格（UIGlass）、预导入贴图 + 按材质名重建（v0.50 台球厅 `remapped=99`）
5. uGUI 中心锚定 y 向上 vs 设计坐标 y 向下：pos.y = 540 - 设计y；IMGUI CRect cx 是中心
6. 编辑器里禁调 Screen.SetResolution；ShotTest 不加 -quit/-batchmode 且须前台（TOPMOST 钉窗）
7. 编辑器测试与 APK 构建**不可并行**（工程锁）；跑完残留场景 → 双击 Main.unity
8. 推送前必 `git fetch`；gh release 建完必须 `release view` 核对非 Draft 且 APK 在列
9. 含中文 .bat 在 GBK 控制台闪退 → 批处理纯 ASCII；PowerShell 调 gh 用 --template
10. **git push 网络抖动**（curl 55 / connection reset）→ 自动重试循环（90~120s 间隔，
    数分钟内多自愈）；github.com 与 api.github.com 可能一个通一个不通（按 IP 抖动）
11. `Physics.Simulate` 手动步进不派发 OnCollisionEnter → 测试用 OverlapSphere 自检；
    白球直接赋 rb.velocity 会被限速钳 0 → 测试用 `ApplySpin(dir,speed,0,0)`
12. **构建卡死 "Detecting Android SDK"（踩坑 44，v0.50 真凶）**：残留 adb.exe（MuMu 的）/
    java.exe 挡住 SDK 探测子进程 → `taskkill /IM adb.exe /IM java.exe /F` + 删 `Temp/`
    （仅删 UnityLockfile 不够）；**且 Unity 联网 fetch SDK 仓库索引，代理须在线**
    （断网卡 "66% Fetch remote repository"，杀了 adb 也不会自愈，须重跑构建）
13. MuMu 会被宿主睡眠/杀 adb 连带关掉 → `MuMuManager.exe control -v 0 launch`（约 55s）
14. 布料板拼缝高速弹起 ~90mm（无害残留，回归红线 0.12m）；球质量 0.17kg 文献 140.6g
    （a=μg 与质量无关，只影响动量分配，未改）
15. Blender：中文 UI 节点名本地化 → 材质脚本用 socket identifier；OBJ 导入器自动转 Z-up；
    glTF 导出器静默跳过导入网格 → JOIN 进程序化网格，导出后解析 GLB JSON 验证

## 十、版本迭代史（均验收）

- v1.0.28 首个全链路验收 → v0.29 粘库修复/哑光球 → v0.30 换手 18s→3s → v0.31 UI/设置/木纹
- v0.32 单杆分 HUD/147/满力 6.0 → v0.33 贝塞尔运镜 → v0.34 规则纯函数化+RuleTest(30)
- v0.35 指定彩球/Miss/自由球(44 断言) → v0.36 球在手+加塞+2ms → v0.37~40 加塞定标/步长三档/
  原神 UI/ShotTest → v0.41 袋口真实化(弧颚/真洞/晃袋)
- v0.42 清彩阶段修正("played at") → v0.43 任选彩球按指定计分(58 断言)
- v0.44~45 液态玻璃 UI + Miss 选择权门禁
- v0.46 碰撞音效（速度映射）→ v0.47 玻璃重做（磨砂+拉丝）→ v0.48 顶条圆角/文字改黑
- v0.49 台球厅场景（Blender 资产轮）→ v0.50 台球厅集成进游戏（FBX/材质重建/室内光）
- v0.51 胜利结算动画（卡片 Q 弹/比分跳数/彩纸 UIConfetti/胜利号角 maxBreak 统计）
- v0.52 抬高台球厅吊灯（罩 1.60 下沿，相机不再被挡；踩坑 46 FBX 逐对象选中）
- v0.53 复选项 + 玻璃去白边更透 + 球在手移左上 + 袋口库边下摆（规则 65 断言；踩坑 47/48）

## 十一、当前功能全清单（均已验收）

菜单(入场运镜+液态玻璃磨砂底) → 设置(帧率/分辨率/阴影/物理步长/音效五行，持久化) →
开始游戏 → 瞄准(拖动/微调±0.02°/辅助线开关/准线自动指定彩球) → 力度滑条 → 击球 →
真实物理(滚动摩擦/库边反弹/真洞落袋/晃袋/加塞杆法) → 完整规则(58 断言) → 计分(HUD 单杆分) →
147 提示 → 结算（**胜利结算动画**：磨砂淡入→胜者卡片 Q 弹→比分跳数（含最高单杆统计）→
彩纸→按钮延迟淡入+胜利号角）；液态玻璃 UI（磨砂+边缘拉丝+Q弹+触感+软投影）；碰撞音效（速度映射）；
**台球厅环境**（三桌/吊灯/墙裙/球杆架/记分牌/挂钟/海报/座椅/盆栽，暖色室内光）

## 十二、遗留 / 可做

- **未实现规则**：Miss 累计三次判负（Rule 11(c)(i)/14(d)(ii)）、自由球裁判裁量。（v0.53 已补齐"原始位置重摆"）
- 观感可调：台球厅整体偏暗（有意，真实球房暗环境）——调 `Bootstrapper` 的 HallLampLight
  intensity/color 即可；MuMu 上玻璃多数元素不渲染（踩坑 43，真机正常）
- APK 约 75MB：两个未引用备用字体 ~48MiB 可移出 Resources；台球厅贴图可 2k→1k 省一半
- 无背景环境音（音乐/人声嘈杂声）；球杆无贴图；彩球无数字贴纸
- 仓库可做：GitHub Actions 自动构建、Topics、英文 README
- 台球厅背景两桌是静态模型（无球、不可打）；可考虑摆装饰球
