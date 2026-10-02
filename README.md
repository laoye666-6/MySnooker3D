# 双人斯诺克 3D（Unity + Blender + MuMu）

基于 Unity 2022.3.62f3c1 开发的安卓双人同屏斯诺克游戏。球桌由 Blender 脚本化建模（标准比赛尺寸 3569×1778mm），
物理采用 Unity PhysX + 自定义滚动摩擦，规则为**完整官方斯诺克规则**（WPBSA Section 3：红彩交替、罚分取值、
彩球回点、清彩、只剩黑球终局、指定彩球、犯规与未击到 Miss、自由球），辅助瞄准线可随时开关，
另带入场运镜、设置界面、单杆分 HUD 与 147 满分提示。

**已开源**：https://github.com/laoye666-6/MySnooker3D （public / MIT，Latest Release v0.49 附 APK 直链）

## 目录结构

```
E:\Snooker\
├─ blender\                     # Blender 脚本化建模（全部无头运行：blender.exe -b -P xxx.py）
│  ├─ make_table.py             # 建球桌+球杆 OBJ；桌体尺寸与袋口参数的唯一权威来源
│  ├─ bake_cloth.py             # 烘焙台呢布纹 + 木纹 1024² 贴图，并重导带 UV 的 table.obj
│  ├─ make_icon.py              # 渲染应用图标 icon.png（512²）
│  ├─ open_table_edit.py        # GUI 调试用：打开球桌+布纹材质、取景出图，存 table_edit.blend
│  ├─ render_pockets.py         # 袋口特写离线渲染（3 张），改袋口外观后校验用
│  ├─ shrink_pockets.py         # 【已废弃】早期按 scale 缩袋口黑柱；v0.29 起改由 make_table.py 参数控制
│  ├─ pool_hall.blend           # 台球厅环境场景（贴图已打包自包含；见 pool_hall_README.md）
│  └─ pool_hall_README.md       # 场景构成 / 资源授权（Poly Haven CC0）/ Unity 集成注意
├─ shaders\                     # LiquidGlass.shader + GlassBlur.shader 母本
│                               #（sync.bat 只同步 *.cs，需手工拷入工程）
├─ android\                     # AndroidManifest.xml 母本（含 VIBRATE 权限，同上手工拷入）
├─ assets\                      # 产物：table.obj/cue.obj(+.mtl)、cloth_texture.png、wood_texture.png、
│                               #       icon.png、preview_persp.png、preview_top.png、
│                               #       audio\sfx_{cue,ball,cush}_{1..3}.wav（音效，v0.46）
├─ unity_src\                   # 【源码母本 · 权威】改动先写这里，再跑 tools\sync.bat 同步
│  ├─ Scripts\                  # 游戏运行时代码（16 个，同步到 Unity 工程）
│  │  ├─ G.cs                  # 标准尺寸常量 / 球种 / 分值 / 颜色
│  │  ├─ Bootstrapper.cs       # 开局自建场景：灯光/相机/球桌物理碰撞体/22颗球/UI
│  │  ├─ GameManager.cs        # 状态机 + 规则落地（加分/回点/换手/终局）+ IsSnookered 几何判定
│  │  ├─ SnookerRules.cs       # 【纯函数规则引擎】TableState + ShotFacts → ShotOutcome
│  │  ├─ BallController.cs     # 球体：落袋下沉、重置、滚动摩擦
│  │  ├─ CueController.cs      # 触控瞄准 / 力度 / 出杆
│  │  ├─ AimLine.cs            # 辅助瞄准线（幽灵球/目标球走向/白球分离/库边反弹）
│  │  ├─ GameCamera.cs         # 相机：入场贝塞尔运镜 + 菜单漂移 + 跟球/全景机位
│  │  ├─ UIManager.cs          # HUD/菜单/设置面板/147横幅/结算（uGUI 控件 + IMGUI 文字层）
│  │  ├─ SpinPad.cs            # 加塞圆盘控件（拖动小圆点选高杆/低杆/左右塞）
│  │  ├─ UIGlass.cs            # 液态玻璃图形（圆角/胶囊/圆形/描边环零贴图顶点网格）
│  │  ├─ UIJelly.cs            # 按钮 Q 弹动画（按下压扁/松手欠阻尼弹簧过冲回弹）
│  │  ├─ Haptics.cs            # 按钮触感反馈（安卓 VibrationEffect，轻/重两档）
│  │  ├─ Sfx.cs                # 碰撞/击球音效（响度音调随速度映射；同类节流；变体随机）
│  │  ├─ GlassSceneCamera.cs   # 副相机：每帧渲染半分辨率场景 RT 供玻璃折射/光晕采样
│  │  └─ GameSettings.cs       # 帧率 60/90/120/144、分辨率 50/75/100%、阴影、音效，PlayerPrefs 持久化
│  └─ Editor\
│     ├─ BuildGame.cs          # 命令行构建（-x86only 可选，仅模拟器调试用；版本号在此改）
│     ├─ PhysTest.cs           # 编辑器内离线物理回归四入口（开球/库边/加塞/袋口；免打包快速验证）
│     ├─ RuleTest.cs           # 规则回归断言（58 条，不走物理、不需模拟器）
│     └─ ShotTest.cs           # 编辑器内自动截图驱动（一条时间线出 11 张关键帧，含袋口特写）
├─ tools\                       # m.bat: adb 快捷命令 | sync.bat: unity_src → 工程 同步
│                               # make_audio.py: 程序化合成碰撞音效 WAV（v0.46）
├─ shots\                       # 验收截图：MuMu 实测 117 张（s01~s45 + v0xx_* 逐版本 + pocketrender_* 袋口特写）
│                               #   无头编辑器截图（ShotTest 出图）另存于 shots\editor\（11 张）
├─ logs\                        # 构建/测试日志
├─ README.md                    # 本文件（目录结构 / 构建 / 踩坑 / 版本记录）
├─ CONTEXT_SUMMARY.md           # 完整压缩上下文（供导入其它 AI 助手继续开发）
├─ DSH_IMPORT_PROMPT.txt        # 同上的导入提示词
└─ MySnooker3D\                 # 【开源仓库工作副本】origin = laoye666-6/MySnooker3D
   ├─ README.md                 # 面向 GitHub 的仓库文档（含下载安装区）
   ├─ UnityProject\             # 完整 Unity 工程（供他人 clone 自行编译）
   ├─ blender\  tools\  docs\   # 建模脚本 / 同步脚本 / 上下文文档与验收截图
   └─ PUBLISH_TO_GITHUB.bat     # 发布脚本（纯 ASCII）

E:\Snooker3D\                   # Unity 工程根
├─ Assets\Scripts, Assets\Editor          # 由 sync.bat 同步（16 个运行时 + 4 个 Editor）
├─ Assets\Resources\Models\table.obj,cue.obj(+.mtl)
├─ Assets\Resources\Textures\cloth_texture.png, wood_texture.png
├─ Assets\Resources\Audio\sfx_*.wav        # 碰撞音效（从 assets\audio\ 手工拷入，v0.46）
├─ Assets\Resources\Shaders\LiquidGlass.shader   # 液态玻璃（从 shaders\ 母本手工拷入）
├─ Assets\Plugins\Android\AndroidManifest.xml     # 含 VIBRATE 权限（从 android\ 母本手工拷入）
├─ Assets\Resources\Fonts\DroidSansFallback.ttf   # 【实际在用】中文 UI 字体（Apache-2.0）
├─ Assets\Resources\Fonts\NotoSansSC.ttf          # 备用字体（OFL，17.8MB）
├─ Assets\Resources\Fonts\NotoSansCJK-Regular.ttc # 备用字体（32.4MB，CJK 全字库）
├─ Assets\link.xml              # 防裁剪
├─ ProjectSettings\DynamicsManager.asset  # m_SimulationMode 必须为 0(FixedUpdate)!
└─ Builds\Snooker3D.apk         # 构建产物（ARM64 + x86_64，IL2CPP）
```

**注意（源码有三份副本，改动先写母本）**：`unity_src\`（权威母本）→ `tools\sync.bat` 镜像到
`E:\Snooker3D\Assets\`（构建用）→ 发版时同步到 `MySnooker3D\UnityProject\Assets\`（开源仓库）。
三份内容目前逐字节一致，**只改母本**，其余两处由脚本/发布流程覆盖。

**注意（Resources 打包规则）**：`Assets\Resources\` 下的资源会被**无条件打进 APK**，与是否被引用无关。
上面两个备用字体（NotoSansSC 17.8MB + NotoSansCJK-Regular.ttc 32.4MB）合计约 48MiB 常驻包内
（当前 APK 约 70MB）。已确认代码中无引用（文字层实际使用 DroidSansFallback）；如需瘦身，把这两个
文件整体移出 `Resources\` 即可 —— 纯移动文件，无需改代码。

## 构建命令

```bat
:: 1. 同步源码到工程
E:\Snooker\tools\sync.bat

:: 2. 无头构建 APK（双架构正式版）
"E:\Program files\2022.3.62f3c1\Editor\Unity.exe" -batchmode -quit -nographics ^
  -projectPath "E:\Snooker3D" -executeMethod BuildGame.BuildAndroid ^
  -logFile "E:\Snooker\logs\build.log"
:: 调试迭代可加 -x86only（只编 x86_64，IL2CPP 时间减半）
:: 版本号在 unity_src\Editor\BuildGame.cs 改：versionName 0.NN = 第 NN 次迭代，versionCode 同步递增

:: 3. 规则回归（改规则后必跑，期望 PASS=58 FAIL=0 + ALL RULES OK）
"E:\Program files\2022.3.62f3c1\Editor\Unity.exe" -batchmode -quit -nographics ^
  -projectPath "E:\Snooker3D" -executeMethod RuleTest.Run -logFile "E:\Snooker\logs\ruletest.log"

:: 4. 物理回归（注意：不能加 -nographics，需要图形设备）
"E:\Program files\2022.3.62f3c1\Editor\Unity.exe" -batchmode -quit ^
  -projectPath "E:\Snooker3D" -executeMethod PhysTest.Run -logFile "E:\Snooker\logs\pt.log"

:: 5. Blender 重新生成球桌（改尺寸/颜色后）
"D:\Program Files\STEAM\steamapps\common\Blender\blender.exe" -b -P E:\Snooker\blender\make_table.py
:: 然后把 E:\Snooker\assets\*.obj 拷到 Assets\Resources\Models\

:: 6. 重新生成碰撞音效（改音色后；v0.46）
python E:\Snooker\tools\make_audio.py
:: 然后把 E:\Snooker\assets\audio\*.wav 拷到 Assets\Resources\Audio\
```

## 安装与运行（MuMu）

```bat
"E:\Program files\Netease\MuMu\nx_main\adb.exe" connect 127.0.0.1:16384
"E:\Program files\Netease\MuMu\nx_main\adb.exe" -s 127.0.0.1:16384 install -r -t E:\Snooker3D\Builds\Snooker3D.apk
"E:\Program files\Netease\MuMu\nx_main\adb.exe" -s 127.0.0.1:16384 shell am start -n com.snookerlab.snooker3d/com.unity3d.player.UnityPlayerActivity
```

## 玩法 / 操作

- **瞄准**：桌面空白处左右拖动旋转球杆；也可用左下角 ◀ ▶ 微调（每次 0.02°）
- **力度**：右下角滑条（点击或拖动）
- **击球**：右下角"击球"按钮
- **加塞 / 杆法（v0.36，v0.37 按真实台呢定标）**：右下角"击球点"圆盘，拖动盘内小圆点选择
  杆头打在白球上的位置——圆心中杆、向上=高杆（跟杆）、向下=低杆（缩杆）、
  偏下约一半=**定杆**（撞球即停）、左右=左右塞（撞库改角）。白球速度任何时刻不超过出杆初速。
- **辅助瞄准线**：左下角"辅助线：开/关"切换（主菜单亦可设置），含幽灵球落点、
  目标球走向（按目标球颜色着色）、白球分离方向、库边反弹预测
  （**关掉辅助线也仍有"指定彩球"能力**——准线命中哪颗彩球即指定哪颗，属规则要求）
- **球在手（v0.36）**：开球前与白球落袋后，白球可在开球区 **D 内自由拖动摆放**——
  直接按住白球拖到想要的位置即可（HUD 会提示"球在手"）；在台面别处按住拖动仍是转球杆
- **指定彩球（v0.35）**：球 on 是彩球时，用准线指向想打的那颗即完成指定，HUD 显示「已指定 X」
- **Miss 提示（v0.35）**：对方犯规且未击中球时弹出提示，可选【让对手重打】或【我自己打】
- **自由球（v0.35）**：被判自由球时 HUD 出现「自由球：可指定任意一颗球作为球 on」提示
- **双人**：同屏轮流击球，顶部记分板实时显示比分/目标球/剩余红球
- **设置**（主菜单或 HUD 左下"设置"）：帧率上限、渲染分辨率、画面阴影、**物理步长**
  （0.5ms / 1ms / 2ms，默认 2ms；改动立即生效并持久化，重启后沿用）、**音效开/关**（v0.46）

## 已实现规则（v0.34 起为完整斯诺克规则，判定集中在纯函数 SnookerRules.cs）

- 红球 1 分；彩球 黄2 绿3 咖啡4 蓝5 粉6 黑7
- **球 on 判定（Section 3 Rule 3(g)/(h)；"ball on" 定义见 Section 2 Rule 11）**：只要台面还有红球，
  **换手后接台方永远重新从红球打起**；打进红球后下一杆打"任意彩球"（3(h)(i)）；最后一颗红球之后
  同样有一颗任意彩球，该球**只要被击打过**（不论进 / 未进 / 犯规，即 3(h)(ii) 的
  "a colour has been played at"，v0.42 修正）之后，彩球才按 黄→绿→咖啡→蓝→粉→黑 升序清彩
  （清彩目标由台面剩余彩球实时推导）
- **罚分（Section 3 Rule 11 Penalty Values；犯规判定见 Rule 10）**：最低 4 分，取"球 on 分值 /
  涉及球分值"较高者；空杆与白球落袋按球 on 分值计（Rule 11(a)；只剩黑球时空杆罚 7 分）；
  连续两杆打红罚 7 分（Rule 11(d)(iii)）；同杆多犯规取最高（Rule 10(g)）
- **犯规杆**打进的所有球一律不计分（Rule 10(e)）；彩球回点且多颗同时回点时高分优先
  （Rule 7(e)/(f)）；红球永不回点（Rule 3(g)）；白球落袋回开球区
- **只剩黑球（Section 3 Rule 4）**：第一次得分**或犯规**即终局；仅当比分因此打平时重置黑球继续
- **指定彩球（Section 2 Rule 12 Nominated Ball；Section 3 Rule 3(h)(i)，v0.35 / v0.43）**：球 on 为
  彩球时必须指定打哪一颗，且**计分的必须是【指定】的那颗**（3(h)(i) "which, if potted, is scored"）；
  本作用"准线指向的球"自动作为指定对象，HUD 显示「已指定 X」；指定球参与罚分（指定黑球后白球
  落袋罚 **7 分**，旧版固定 4 分）；指定蓝球却进绿球 → 罚 5 分、绿球回点、本杆不计分（v0.43 修正）
- **犯规与未击到 Miss（Section 3 Rule 14 Foul and a Miss；Miss 定义见 Section 2 Rule 21，v0.35/v0.45）**：
  未先击中球 on 且当时未被斯诺克即判 Miss，接台方可选【让对手重打】（击球权交回、球位不动）
  或【我自己打】；**选框未做出选择前禁止击球**（v0.45 门禁：Rule 14(b) 的选择权必须先于
  下一杆行使，Rule 13 选择一经提出不可撤回）
- **自由球 Free Ball（Section 3 Rule 12 Snookered After a Foul；定义见 Section 2 Rule 13，v0.35）**：
  犯规后接台方对所有球 on 均被斯诺克时可指定任意球为球 on，打进按**真实球 on 分值**计分并回点
  （红球也回点），之后按真实球 on 继续
- 双人交替：犯规/未进球换人（换手后回到红球），合法进球连续击球

## 踩坑记录（重要）
1. **MuMu 只认 arm64-v8a/x86_64**：Unity 的 Mono 后端在 Android 上只能产出 ARMv7 → 装到 MuMu 直接
   `INSTALL_FAILED_NO_MATCHING_ABIS` 拒装 → 必须 IL2CPP + ARM64/X86_64（唯一能同时产出这两个 ABI 的路子）。
2. **IL2CPP 引擎裁剪**会剥掉 SphereCollider/MeshCollider（CreatePrimitive 内部 AddComponent 报
   "class doesn't exist"）→ `PlayerSettings.stripEngineCode = false` + link.xml。
3. **白球与棕球同点重生**会让 PhysX 解算器爆炸（球飞出 ±38m、瞬间反弹）→ 开球白球放 D 区内偏离棕球点。
4. **PhysX 刚体休眠后直接 `rb.velocity=` 不会唤醒**（球不动）→ 出杆前必须 `WakeUp()`。
5. **编辑器里跑过 `Physics.autoSimulation=false` 的测试代码会把 `SimulationMode: 2` 持久化进
   DynamicsManager.asset 并打进包**，设备上物理完全不步进 → 资产保持 `m_SimulationMode: 0`，
   启动时运行时再强制一次；PhysTest 跑完必须恢复（手动步进枚举是 `SimulationMode.Script`）。
6. **uGUI 动态字体在部分模拟器上渲染空白**（真机同理风险）→ 文字层改用 IMGUI 绘制（已验证可靠），
   中文用内嵌 DroidSansFallback.ttf（Apache-2.0 可再分发）。此后不要新增 uGUI 文字。
7. **OBJ 导入后整桌合并为单个 "default" 网格、5 个材质槽** → 按材质名（Cloth/Cushion/Wood/Pocket/Mark）
   匹配赋材质，且要给所有槽赋值。
8. **Blender 导出的 OBJ 物体原点在世界原点** → 按物体 scale 缩放会把各部件拉向桌心（袋口位移事故根因）
   → 要改尺寸只能改 `make_table.py` 重新生成，禁止用 scale 微调。
9. **低速撞库会「粘」在库边**：PhysX 对法向速度低于 `bounceThreshold` 的接触不施加弹性 →
   阈值 0.5 → **0.1**；编辑器 `PhysTest.CushionTest` 三用例（快碰/慢碰/掠射）回归通过。
10. **uGUI 中心锚定 y 轴向上、设计坐标 y 轴向下**：`pos.y = 540 - 设计y`（设置面板曾整体上下镜像）；
    IMGUI 的 `CRect.cx` 是矩形**中心**而非左边缘（HUD 文字曾因此出屏）。
11. **换手等待久的隐藏元凶**：判停还等角速度衰减，而 `angularDrag` 仅 0.08，原地强旋球能拖几十秒 →
    停判只看线速度；出杆 4 秒后阈值放宽到 0.22，但慢爬球若 `reach = v²/2a` 仍够得着袋口则继续等。
12. **MuMu 窗口失焦 / 宿主休眠**会让应用暂停（输入延迟、画面冻结）甚至模拟器被关掉；`adb connect`
    被拒时先 `MuMuManager.exe control -v 0 launch` 重启（约 55 秒）——不是包的问题。
13. **PowerShell `Set-Content -Encoding ASCII` 会毁掉含中文的 UTF-8 脚本/文档**（勿用于改文件）。
14. **规则判定必须写成纯函数**：v0.33 那个"清彩阶段目标球与白球同杆落袋 → 该球不回点、目标又无法
    推进 → 一局永远打不完"的致命死局，根因就是规则判断散在 `GameManager` 的物理结算里，
    只能在模拟器上真打才可能发现。v0.34 起规则集中在 `SnookerRules.cs`（纯函数），
    由 `Editor/RuleTest.cs` 离线断言（v0.34 为 30 条，v0.35 扩至 44 条，v0.43 为 **58 条**）。**改规则必须跑它。**
15. **UI 不要重复乘缩放系数**：`CanvasScaler(match 0.5)` 的 scaleFactor 已经等于 `K()`，
    再给 `RectTransform.anchoredPosition` 乘 `K()` 就是二次缩放（147 横幅与设置面板曾因此错位）。
    IMGUI 文字同理：`CRect` 已含 `k`，别再乘。
16. **HUD 别硬贴 1920×1080 边缘**：`match=0.5` 只保证中心区，20:9 机型上下会被裁、4:3 机型左右会被裁；
    必须用 `Screen.safeArea` 算出的"可见设计安全区"夹住（见 `UIManager.Fit` / `FittedRect`）。
17. **批处理脚本必须纯 ASCII**：含中文的 UTF-8 `.bat` 在 GBK 控制台上会崩（仓库提交历史里有这条教训）；
    另外 `if cond A & B` 里的 `B` 是**无条件执行**的 —— 要用 `if cond ( A & B )`。
18. **推送到 GitHub 前必须先 `git fetch`**：本机曾在 GitHub 网页端直接改仓库，若在远端有新提交时
    盲目 `--force-with-lease` 推送，会**覆盖网页端的提交**（已发生一次，靠 `git cherry-pick` 找回）。
    → 已推送的历史尽量不改写；推送前先 fetch 比对。另：PowerShell 调 `gh` 时 `--jq` 表达式含引号会被
    拆参数报 "accepts 1 arg(s), received N"，改用 `--template` 或写成 `.ps1` 文件执行。
19. **本机 Bash 是 Git Bash**：路径写 `/e/Snooker`；执行 `.bat` 要 `cmd //c tools/sync.bat`；
    `cmd` 的 `timeout /t` 在重定向环境下会立即返回，等待改用 `powershell -Command "Start-Sleep -Seconds N"`。
20. **PhysX 会把自旋"抹平"**：刚体 + 库仑摩擦无法表达台球自旋（球的接触是滚+滑，不是纯滑动），
    所以白球的加塞必须自己建模（`BallController.CueRollStep`）。两个必须同时做的配套：
    ① 白球物理材质摩擦要取 0（否则 PhysX 的台呢摩擦与模型双重计算，低杆效果被吃掉）；
    ② 白球 `angularDrag` 要设 0（否则角阻尼额外衰减自旋）。
    **且模型必须保证"中杆手感零回归"**——`spinV=spinH=0` 时初速即为纯滚动、滑移为零，
    与旧版逐帧一致，这是判断加塞实现是否正确的第一道验收。
21. **改物理步长要连带改测试的步数**：`PhysTest.RunCase` 原写死"模拟 800 步"（配 4ms=3.2 秒），
    v0.36 把步长改成 2ms 后时长被砍半，慢速撞库用例"刚碰到库边就结束"，误报 `bounced=false`
    （看起来像粘库 bug 复发）。已改成 `steps = 4f / dt` 按秒数换算。
22. **`Camera.main` 依赖 MainCamera 标签**：Bootstrapper 自建相机时若忘记打标签，
    `Camera.main` 返回 null。拖动白球需要"屏幕坐标 → 台面平面"反投影，必须用相机，
    因此 v0.36 补上 `camGo.tag = "MainCamera"`（并保留 `FindObjectOfType<Camera>()` 兜底）。
23. **编辑器内自动截图（ShotTest）的四个坑**（v0.38 起用它替代每轮装模拟器）：
    ① **不能加 `-quit`**：它会在 `isPlaying=true` 之后立刻退出，播放模式的协程根本没机会跑；
    ② **不能加 `-batchmode`**：批处理下没有渲染帧循环，`WaitForEndOfFrame` 永不返回、
       `ScreenCapture.CaptureScreenshot` 不落盘 → 改用 `Texture2D.ReadPixels` 手动读帧缓冲
       并同步 `File.WriteAllBytes`；
    ③ **绝不要在编辑器里调 `Screen.SetResolution`**：它会试图改变 Game 视图分辨率，
       直接把编辑器播放模式卡死（症状：日志停在 "entering play mode"、一张图不产出）。
       想看大图请在 Game 视图下拉里手动选 1600×900；
    ④ **时间线用 `WaitForSecondsRealtime`** + `Application.runInBackground = true`：
       编辑器窗口失焦时播放模式可能暂停，普通 `WaitForSeconds` 会被拖慢。
    正确调用（带窗口、不加 -quit/-batchmode）：
    `Unity.exe -screen-width 1600 -screen-height 900 -projectPath E:\Snooker3D -executeMethod ShotTest.Run -logFile E:\Snooker\logs\shot.log`
24. **工程被占用会导致编辑器崩溃**：后台构建还没结束时再起一个 Unity 实例，会报
    `HandleProjectAlreadyOpenInAnotherInstance` 并崩溃；残留的 `Temp/UnityLockfile`
    要手工删掉，同时确认没有残留 Unity 进程（`Stop-Process -Name Unity -Force`）。
    → 编辑器测试与 APK 构建**不要并行**。
25. **运行时创建的 UI 图形在真机可能整块不渲染**（v0.40 未解之谜，留作后续）：
    给加塞圆盘画"圆形白球面"时三次翻车——① `Sprite.Create(tex,...)`；
    ② `new Texture2D` + RawImage；③ 导入 PNG + Android 强制未压缩。三次在**编辑器里都
    正常**，装到 MuMu 上圆盘子树（含 12 颗纯色块金菱刻度）**整棵不渲染**，只剩 IMGUI 文字。
    排查到的线索：设备支持 ETC1/ETC2 但**不支持 ASTC**（Unity 2022 Android 默认格式），
    所以先怀疑贴图格式，但改成未压缩后仍不显示，且**没有贴图的纯色块也一起消失** ——
    指向"这棵 UI 子树的渲染本身有问题"而非资源格式。
    **当前决策**：回退为方形纯色块（v0.38 真机验收过、显示正常），保证功能可用。
    **给后续的自己**：若再试，优先怀疑 (a) `Img()` 的 `SetParent` 后重排层级导致
    CanvasRenderer 状态异常；(b) 该矩形是否落在屏幕外（当时的诊断只拿到
    `padWorldPos`，没能确认最终布局位置）。
26. **给台面挖洞时，洞与台面边缘之间的布料会变成"托球唇"**（v0.41）：角袋的洞口圆
    有一部分伸出库边鼻线之外（圆心在台面角点外侧 8mm）。若只按洞口圆挖，洞口圆与台面
    矩形边界之间会剩一条月牙形布料，正好托住沿库滚进角袋的球 —— 实测球心降到 -0.02
    就被托住，永远到不了落袋深度 -0.10。**修法**：角袋的挖空要朝台面外侧**一直敞开**
    （把该带的挖空区间延到台面外），使洞口在那一侧是开放边界。中袋不需要（洞口圆完全
    在台面内）。
27. **台面碰撞体不要"比实际台面多留一圈"**（v0.41）：旧版台面是一个每边多 6cm 的大盒
    （本意"垫在库边下方"），挖洞后那圈多出来的布料在四个角袋处是**裸露在袋口区域的台呢**。
    球滚到袋口时会同时被库边鼻面和这圈布料的端面夹住，解算结果是把球斜着挤飞 ——
    实测球以 4.1 m/s 的恒定 X 速度飞出台面 3 米（x 一路涨到 3.4，全程无减速）。
    **修法**：布料板就是库边鼻线围出的矩形（±HalfL / ±HalfW），库边自带碰撞盒，不需要垫底。
28. **袋井内壁若比台呢开孔大，俯视会看到洞内一圈绿色洞壁**（v0.41）：台呢床身被布尔
    挖出的洞壁用的是布料材质（绿）。暗井外壳半径若大于开孔，圆洞内圈就露出这层绿。
    **修法**：井的内腔半径取「开孔 −2mm」，由黑色把绿壁整个挡住；外壳仍比开孔大 8mm
    以垫住孔与木框让位之间的缝。
29. **`Physics.Simulate` 手动步进不派发 MonoBehaviour 碰撞回调**（v0.41）：
    `OnCollisionEnter` 在手动步进模式下**不会被调用**，用它做的测试探针会一直记 0，
    而球明明已经撞过颚被弹回来了（据此误判成"没发生晃动"）。
    **修法**：测试里改用每步 `Physics.OverlapSphere(pos, BallR + eps)` 自己检测接触，
    并按"进入/离开"配对成接触回合数。
30. **手动步进测试不能直接给白球赋 `rb.velocity`**（v0.41）：白球的 `CueRollStep` 有
    "速度任何时刻不得超过出杆初速"的硬限速，基准 `shotSpeed0` **只在 `ApplySpin()` 里赋值**。
    测试里直接写 `rb.velocity` 会让限速把它钳到 0（球纹丝不动），
    症状是"慢球滚不进角袋"这类假失败。**修法**：测试里也用
    `ApplySpin(dir, speed, 0, 0)`（中杆 = 纯滚动）来出杆。
31. **编辑器跑 ShotTest 时窗口必须保持前台**（v0.41 复现）：编辑器窗口被别的窗口盖住/失焦时，
    GUI 会卡成白屏、`[SHOT]` 日志停在 "entering play mode" 不再推进（协程没机会跑）。
    虽然 ShotDriver 里设了 `Application.runInBackground = true`，仍不稳。**做法**：用
    `tools/focus_and_shot.ps1` 把 Unity 窗口提到前台；跑完 `ShotTest` 后编辑器会退出，
    但 `Library/LastSceneManagerSetup.txt` 可能残留"无标题空场景"记录 —— 下次启动若按 Play
    只有天空盒，**双击 `Assets/Scenes/Main.unity` 打开正确场景即可**。
32. **颚面碰撞体必须真的按圆弧轮廓走，不能用一条直斜线逼近**（v0.41）：
    颚尖圆弧与"直斜线"的最大偏差约 6.4mm（占球直径 12%）。这条偏差恰好落在**球贴库滚进
    袋口时最先接触的那一段**，用直斜线会让颚部手感偏"直"、球被弹开的时机偏晚。
    **实现要点**：按离颚尖的进深 d 把颚面切成 N 片，每片轮廓取
    `min(斜颚面 d·CushD/JawDx, 颚尖圆弧 r−√(r²−d²))`，各做一个 convex MeshCollider。
    改成圆弧后晃袋用例的接触次数从 1 次变成 2 次（撞一侧颚 → 弹到另一侧颚），
    更接近真实球桌的晃袋过程。
33. **袋内衬的半径有硬约束，且必须是"有厚度的实体"**（v0.41，两个真机 bug 换来的）：
    ① **半径下限**：内径必须 ≥ 洞口半径 + 球半径 + 余量。球在台呢上滚过袋口时，
    球心离袋口轴心最近就是洞口半径（55/62mm），球面最远伸到 洞口+26mm。
    内衬若比这近，球一进袋口就**嵌进内衬壁**，PhysX 去穿透把它崩飞 ——
    真机实测球心升到 124mm、以 4.7m/s 飞出台外 3 米（`POTTED Cue at(-2.138,0.124,…)`）。
    ② **必须给厚度**：零厚度曲面做内衬时，5m/s 的球（每步走 10mm）会直接**穿透**进去，
    再被 `defaultMaxDepenetrationVelocity` 顶出来 —— 实测球心弹到 **115mm，正好等于
    1.5²/(2g)**（这个数值特征直接指出了元凶）。现取 30mm 壁厚 + 把去穿透速度上限
    降到 0.6 m/s。
34. **落袋判据必须在"球一进洞口"就成立，不能等球掉到某个深度**（v0.41）：
    最初写成"球心低于 -0.10m 才判落袋"，结果高速球在掉够这个深度之前已经横移了 0.29m
    （3.3m/s × 下坠 38mm 所需时间），先撞上袋内衬或**布料拼缝的边缘**被顶飞 ——
    真机实测球心弹到 277mm、飞出台面。
    现改为 `G.InPocket()` 三层判据（见版本记录 v0.41 ②），核心是"球心已在洞口圆内
    且已下沉到台呢面之下"就立刻判，角袋直接算进、中袋要求速度朝袋外（否则是真实"过袋"）。
    **教训**：脚本判定与物理下坠的时机必须对齐 —— 判定晚一步，物理就已经跑到别处去了。
35. **布料板拼缝在高速下会改变接触法线**（v0.41，已知残留）：台面由 127 块 BoxCollider
    拼成（挖洞的代价）。球高速跨越两块间的拼缝时，接触法线会被解算成倾斜，
    给球一个向上的分量 —— 实测 5m/s 正打角袋时球心弹起约 90mm（1.2~3m/s 无此现象）。
    球仍正常落袋，真实球袋本来也会"跳球"，故按现状接受；
    回归里用"最高球心 ≤ 0.12m"作为防回归红线。若将来要彻底消除，
    方向是把布料板换成**单个 MeshCollider**（用带洞的三角网格），但会失去凸体求交的稳定性，
    需重新回归撞库与滚动。
36. **规则里的"击打过"不等于"打进"**（v0.42，清彩阶段混乱的根因）：
    WPBSA Section 3 Rule 3(h)(ii) 的措辞是 **"a colour has been played at following the
    potting of the last Red"** —— 触发升序清彩的条件只是那颗任选彩球**被击打过**，
    **不要求打进**。旧版把阶段切换写成 `if (postReds == 0 && scored)`，即只在**合法打进**
    时才切，于是未进/犯规换手后仍停留在"任意彩球"，接台方还能随便挑彩球打。
    **教训**：规则措辞里的每个动词都要逐字读（played / potted / struck 是三种不同条件），
    凭"意思差不多"去实现就会埋下这种只在特定路径才暴露的 bug。
    另：本条也为"用纯函数 + 构造式断言"背书 —— 这个 bug 靠真机试打极难稳定复现
    （要正好走到"最后一红后打彩球但没进"），但用构造的状态表一测就现形。
37. **规则条款号要按当前规则书的版本核对**（v0.42）：本项目注释里长期写的
    `Rule 3(e)(f)` / `Rule 10.3` 是**旧版编号**；2024-25 版里"球 on 顺序"是
    Section 3 Rule 3(g)/(h)，"球 on 的定义"在 Section 2 Rule 11，
    而"换手后回红球"根本没有独立条款号（是 3(g) 的直接推论）。
    照旧号读规则会找错条款、进而误判语义。**改规则前先确认规则书年份与编号**。
38. **"任选彩球"必须按【指定】的那颗计分**（v0.43）：Section 3 Rule 3(h)(i) 说
    "the next ball on is a colour of the striker's choice **which, if potted, is scored**" ——
    计分的必须是**被指定为球 on 的那颗**。旧版对"任选彩球"分支无条件 `legalPts += G.Value(k)`，
    于是"指定蓝球、打进绿球"被算成合法 3 分（应为罚 5 分）。
    **教训**：`switch` 里"看起来总是合法"的分支，往往漏了"对象是否匹配"的校验；
    规则里凡出现"指定的球""球 on"这类限定词，都要在计分前比对一次。
39. **GLES 铁律：shader 里 pow() 底数必须 max(…,1e-4)**（v0.45）：液态玻璃穹顶中心
    n.z==1 → pow(0,1.7) 在部分 GLES 驱动返回 NaN，整片玻璃中心渲染成黑色——编辑器
    D3D11 一切正常，极具迷惑性。定位手段：做一个"直接输出顶点色"的诊断构建。
    同类坑：场景采样（GrabPass/相机命令缓冲/OnRenderImage）在 MuMu GLES3 上间歇性
    黑帧/清屏色，最终改用 GlassSceneCamera 副相机渲染半分辨率 RT（多相机渲染是唯一
    确定性方案），并 LateUpdate 隔帧 enable 折半成本（折射源 30Hz 肉眼不可辨）。
40. **ShotTest 会把物理步长 0.5ms 持久化**（v0.45）：时间线演示切 0.5ms 后若中断，下一轮
    编辑器以 2000Hz 物理启动，首帧慢到像卡死（ShotTest 连续"白屏无产出"的元凶）。
    修法：时间线开头强制回 2ms 并 Save；编辑器 PlayerPrefs 在注册表
    HKCU\Software\Unity\UnityEditor\SnookerLab\Snooker3D（键名是哈希形式如
    snk_stepIdx_h4150860907）。另内置 150s 看门狗自动退出僵死会话。
41. **编辑器播放模式仍会间歇白屏卡死**（ShotTest 顽疾，v0.45）：runInBackground 也压不住，
    表现=[SHOT] 停在 entering play mode / READY 后协程全灭、无产出。缓解：启动后用
    SetWindowPos HWND_TOPMOST 钉住窗口（实测大幅减少）；卡死后杀进程删 Temp/UnityLockfile
    重试。另：`export MSYS_NO_PATHCONV=1` 会把 `cmd //c` 的双斜杠转换也禁掉 → cmd 进
    交互模式、bat 根本没执行——跑 .bat 时不要带该变量（仅 robocopy/adb 参数需要）。
42. **Fallback 会静默吞掉 shader 编译错误**（v0.47 发现，已掩盖两版）：v0.44~v0.45 的
    LiquidGlass.shader 引用了未定义变量 `spec`，整个 Pass 编译失败，Unity 自动走
    `Fallback "UI/Default"` 平色渲染——界面"看起来正常"（平色玻璃凑合像），母本与工程
    副本还逐字节一致，毫无异常迹象。**教训**：改 shader 后必须在编辑器 Console 确认无
    "Shader error"，或对玻璃做一次"必须有折射内容"的针对性截图；"材质在渲染"≠"在用
    你写的那个 Pass"。
43. **MuMu GLES3 不渲染多数 LiquidGlass 元素，真机正常**（v0.47）：自带 CanvasGroup 的
    元素（中央提示胶囊/大面板）能渲染，按钮/顶条/加塞盘等整块消失；CanvasGroup/
    UIJelly/材质参数三组对照实验逐一排除，无日志无异常。**结论：MuMu 的 GL 翻译层
    不可作为玻璃 UI 的验收环境**——玻璃验收 = 编辑器截图（ShotTest）+ 真机；
    定位手段：shader 临时直接输出顶点色（踩坑 39 方法），验完立即还原。
44. **构建卡死在 "Detecting Android SDK" = 残留 adb.exe/java.exe 挡路**（v0.50）：
    连续三次构建在该行停止推进（CPU 零、日志冻结数十分钟）。根因：MuMu 的 adb server
    与构建期间残留的 java（Gradle daemon）让 Unity 的 SDK 探测子进程死等。
    **处置：杀光 `adb.exe`/`java.exe` + 删 `Temp/` 再构建**（仅删 UnityLockfile 不够）；
    恢复后 IL2CPP 全量约 40~60 分钟。

## 已验证（MuMu 实测）

- 进入游戏：菜单 → 开始游戏 → 记分板/中文 UI 正常
- 击球：开球冲散球堆、贴库球、切角进球、白球走位、库边反弹、袋口捕获均符合真实物理
- 计分：红球落袋 +1（记分板 6:4）、犯规 +4/+5（白球落袋/先触蓝球）、进球后目标切换
  为"任意彩球"、彩球/白球重置、轮换逻辑
- 辅助瞄准线：幽灵球 + 目标球走向 + 分离线 + 库边反弹，可随时开关
- 运镜 / 设置 / HUD：贝塞尔入场运镜、设置面板（帧率/分辨率/阴影，重启后保持）、
  单杆分 HUD（金色大字）、147 红黑连击提示
- v0.35 规则回归：`logs\ruletest3.log` 实测 `PASS=44  FAIL=0`（`ALL RULES OK`）

**验收截图**（MuMu 实测，存于 `shots\`）：

![v0.35 实测：开球瞄准](shots/s45_v035_aim.png)

## 遗留 / 可做

- 未实现：Miss 累计三次判负（Rule 11(c)(i)）、自由球判定的裁判裁量部分（已就地标注）
- 147 横幅已实现但未在真机自然触发（需连续 5 套红黑走位）；清彩/黑球决胜未逐步走完
- 无音效；球杆无贴图（纯色）；彩球无数字贴纸；图标高光在 512px 下有轻微像素感
- 加塞圆盘只有文字提示当前杆法，没有画杆头示意动画
- **已知未完成**：加塞圆盘的"白球面"目前是**方形**色块。曾三次尝试画成圆形
  （运行时 `Sprite.Create` / 运行时 `new Texture2D`+RawImage / 导入 PNG 资源 + Android
  强制未压缩），后两种在编辑器里都正常，但**真机（MuMu GLES3）上圆盘连同金菱刻度整棵
  子树都不渲染**，只剩文字；方形色块在真机显示正常，故保留方形。
  排查线索：设备只支持 ETC1/ETC2（不支持 ASTC），但把贴图设为未压缩仍未解决，
  且**纯色块刻度也一起消失**，说明不是贴图格式问题。详见踩坑记录第 25 条。
- 入场 CG（Blender 渲染开场动画）曾列入计划，**已按需求取消**（不做）
- APK 约 70MB，其中约 48MiB 是两个**未被引用**的备用中文字体常驻包内（移出 `Resources\` 即可瘦身）
- 仓库可做：GitHub Actions 自动构建、Topics 标签、英文 README
- GitHub Release：v0.45（Latest，含 APK）、v0.43、v0.42、v0.41、v0.40、v0.36、v0.35、v0.33

## 台球厅场景（Blender，待集成）

`blender\pool_hall.blend` + `assets\hall\pool_hall.glb`（29MB，186 节点）：围绕游戏球桌构建的
完整台球厅环境 —— 21×10m 房间（旧木地板/暗绿墙+墙裙）、三张球桌、每桌低垂长条吊灯
（真实球房 32~36″ 标准）、球杆架/记分牌/挂钟/海报/沿墙座椅/盆栽，**无人物**。
免费资源全部 Poly Haven CC0（下载脚本 `tools\fetch_hall_assets.py`），出处与 Unity 集成注意
见 `blender\pool_hall_README.md`。预览：`shots\hall_main_v4.png` / `shots\hall_wide_v5.png`。
**尚未集成进 Unity 工程**（下一轮：Bootstrapper 加载 GLB + 游戏内灯光布点 + 移动端贴图降级）。

## 版本记录

### v0.50（第 50 次迭代）—— 台球厅集成进游戏

修复 v0.49 的"安装后场景没变"：台球厅此前只存在于 Blender，未接入 Unity。

- **资产路线**：GLB 需要 Unity 插件才能读 → 改导 **FBX（内嵌贴图，Unity 原生支持）**
  （`blender -b pool_hall.blend --python-expr` 无头导出）。场景模型**剔除主桌**——它与
  游戏 table.obj 几何完全重合，同位置渲染会 z-fight；主桌仍由 table.obj 提供。
- **Bootstrapper 集成**：`Resources.Load("Models/pool_hall")` 实例化（缺失则告警回退空厅）；
  垫底地板下移至 -0.9（厅模型自带地板同为 -0.72，共面闪烁）；**室内光照**——主光
  1.05→0.45、补光→0.15、环境光压暗，每桌吊灯位（±7.2/0, y=0.80）加 300W 级暖色点光
  （range 6.5），与 Blender 预览同光位；液态玻璃折射源照常采样新场景。
- **GameCamera**：入场弧线从 4.7m 高空压到 2.45~2.55m——台球厅有天花板（y=2.68），
  旧弧线会穿越天花板（背面剔除闪烁）。
- **构建环境踩坑 44（本轮真凶）**：连续三次构建卡死在 "Detecting Android SDK"（CPU 零、
  日志不动）。根因是**残留的 adb.exe（MuMu 的）与 java.exe 挡住 Unity 的 SDK 探测子进程**
  ——杀光 adb/java 并清 Temp 后立刻恢复。处置：构建前 `taskkill adb.exe/java.exe`。
- 回归：规则 58/58。**MuMu 实测**：台球厅完整入景（背景桌/扶手椅/盆栽/暖色地板可见），
  灯罩不再挡视线，材质重映射 99 个渲染器生效（logcat `remapped renderers=99`），
  开球/音效/结算零回归（`shots\v050_menu2.png` / `v050_game2.png` / `v050_shot.png`）。
  FBX 内嵌贴图在真机未生效 → 材质按名重建 + diffuse 贴图入 `Resources/Textures/hall/`。

### v0.49（第 49 次迭代）—— 台球厅场景（Blender 资产轮）

- **blender\pool_hall.blend + assets\hall\pool_hall.glb**（29MB，186 节点）：围绕游戏球桌的
  完整台球厅环境 —— 21×10m 房间（旧木地板/暗绿墙+墙裙）、三张球桌、每桌低垂长条吊灯
  （真实球房 32~36″ 标准，300W 暖色面光）、球杆架/斯诺克记分牌/挂钟/海报/沿墙座椅/四角盆栽，
  **无人物**。
- **免费资源全部 Poly Haven CC0**：旧木地板/灰泥墙贴图 2k、游戏厅 HDRI、绿椅/扶手椅/灌木
  模型（下载脚本 `tools\fetch_hall_assets.py` 直连 API）；其余程序化建模（MIT）。
  出处与 Unity 集成注意见 `blender\pool_hall_README.md`。
- **制作过程（Blender MCP 全程驱动）**：升级 MCP 插件至协议 13；中文 UI 下材质脚本改用
  socket identifier；OBJ 导入器已自动转 Z-up（再转 90° 会立起球桌）；glTF 导出器会静默
  跳过导入模型网格 —— 用「JOIN 进程序化网格」解决，导出前解析 GLB JSON 验证节点齐全。
- **本轮为 Blender 资产轮**：游戏 C# 零改动，APK 仅版本号递增（0.49/versionCode 49）。
  预览：`shots\hall_main_v4.png` / `shots\hall_wide_v5.png`。
- **下一轮**：GLB 集成进 Unity（Bootstrapper 加载环境 + 游戏内灯光布点 + 移动端贴图降级）。

### v0.48（第 48 次迭代）—— HUD 顶条圆角 + 击球点文字改黑

- 顶部计分板顶条（TopPanel）由直角矩形改**圆角 28**（与加塞盘面板一致）；
- 加塞圆盘标题「击球点」文字由次级灰（Ink2）改为主墨色（Ink，黑），与下方"中杆"行同色；
- 纯 UI 微调，规则/物理零改动。回归：ShotTest 11 帧编辑器截图确认（玻璃验收按踩坑 43
  约定以编辑器+真机为准）。

### v0.47（第 47 次迭代）—— 液态玻璃重做：重磨砂 + 边缘拉丝（参考 DeepSeek 风格稿）

按用户提供的参考图重做玻璃质感——目标从"清澈折射+游动光斑"改为 **iOS 26 液态玻璃的
"厚磨砂玻璃板"**：背景重度高斯模糊、整体奶白提亮、**边缘把界外内容"拉进来"并沿边缘
方向"抹开"（透镜拉丝）**、顶部内侧受光高光、底部内侧厚度阴影、最边缘一圈细亮边、
元素下方软投影。

**① 修复一个被掩盖两版的 shader 编译错误（踩坑 42）**：v0.44~v0.45 的 LiquidGlass.shader
第 142 行引用了未定义变量 `spec`——整个 Pass 编译失败，Unity 静默走 `Fallback "UI/Default"`
平色渲染。母本与工程副本逐字节一致、编辑器截图"看起来正常"（因为平色也在渲染），极具
迷惑性；直到本次重写通读 shader 才发现。**教训：Fallback 会吞掉 shader 编译错误，
"没报错"≠"在用新 shader"，改 shader 后必须确认 Console 无 shader 编译错误。**

**② LiquidGlass.shader 重写**（GLES 安全：无循环、无动态分支采样、pow 底数全部 clamp）：
- 圆角矩形 **SDF**（解析有向距离场，UIGlass 每次重建网格时推送 `_RectHW/_CornerR`）：
  fragment 内求到边缘距离 d、外法线 nrm（数值梯度）、切向 tng——一切边缘光学由此驱动；
- **磨砂主体**：采样 `_GlassBlur`（新增的四分之一分辨率磨砂 RT）+ 9 点小核模糊；
- **边缘法向透镜**：`lens * band²` 沿法线向外采样，把玻璃外的背景"拉进"边缘（挤压感）；
- **边缘切向拉丝**：`streak * band²` 跨度上 5 点加权拖影——大面板 90px 长拉丝
  （参考图的大面板拖影约占高度 15~25%），控件 44px 短拉丝；
- **光层**：顶部内侧 sheen（band²·max(nrm.y,0)）、底部内侧厚度阴影（band·max(−nrm.y,0)）、
  最边缘 rim 细亮边（band⁶）、边缘轻收暗（亮背景可读性）、轻微去饱和 + 奶白 frost 提亮；
- **软边模式 `_SoftMode`**：同一 shader 分支输出纯色 + alpha 向网格边缘淡出——专用于投影；
- 三盏游动光源保留但大幅调低（0.18/0.10），只作点缀。

**③ GlassSceneCamera 加磨砂链**：新增四分之一分辨率 RT，相机隔帧渲染后对上一帧场景做
**两轮分离高斯**（`GlassBlur.shader`，RT→RT Blit 常规路径，非帧缓冲拷贝——不踩 GLES
黑帧坑）产出 `_GlassBlur`；失败时退化为采样原始半分辨率 RT。

**④ UIGlass / UIManager**：
- `UseLiquid()` 全参数入口；`UseRefraction/UseBlur` 变为参考质感预设（旧签名不变，
  SpinPad 等调用点零改动）——控件 frost 0.36 / 拉丝 44px，大面板 frost 随参数 / 拉丝 90px；
- `AttachShadow()`：稍大、下移的软边圆角矩形插到元素正后方（同父层级，随面板 CanvasGroup
  一起淡入淡出）——所有按钮、设置/重打/加塞面板、力度胶囊挂上软投影；
- 独立淡入淡出的中央提示胶囊/147 横幅不加投影（影子不会跟着消失）。

**回归：规则 58/58；编辑器截图 11 帧（ShotTest）确认磨砂/拉丝/投影成型；真机实测观感正常。**

**MuMu 环境差异（踩坑 43）**：本版玻璃在 MuMu GLES3 上除自带 CanvasGroup 的元素
（中央提示胶囊/各大面板）外**多数不渲染**（按钮/顶条/加塞盘整块消失，且与
CanvasGroup/UIJelly/参数无关——三组对照实验逐一排除），而**真机无异常**。沿用 v0.44
结论的加重版：**玻璃效果一律以编辑器截图 + 真机为准，MuMu 不作为玻璃验收环境**；
定位手段=shader 临时输出顶点色（踩坑 39 方法），诊断后已还原。

### v0.46（第 46 次迭代）—— 碰撞/击球音效（响度音调随速度）

新增三类音效，均按撞击速度实时变化：

| 音效 | 触发点 | 声学特征 |
|---|---|---|
| 击球"嗒" | `GameManager.Shoot` 出杆瞬间 | 皮革杆头敲酚醛球：低频闷响+中频敲击+噪声瞬态 |
| 球碰球"咔" | `BallController.OnCollisionEnter`（撞到球） | 清脆高频 click（3.2/5.6/8.4kHz 三段快速衰减） |
| 球碰库"噗" | 同上（撞到库边/颚部/袋衬/地板） | 包呢库边低频闷响 + 台呢摩擦噪声 |

- **力度/速度区分**：响度随法向撞击速度幂函数增长（球碰球 0.6 次幂、库边 0.7 次幂，
  中低速已明显可辨）；音调随速度升高（硬碰撞激发高频振型更多，听感更"亮"）；
  叠加 ±2% 音调抖动 + 每类 3 个合成变体随机挑选，避免同一波形连放的"机枪感"。
- **速度取接触法线方向相对速度**：掠射轻擦声音小、正撞响亮；不用 `contacts[].normal`
  的符号（各平台歧义，见 CushionSpin 注释），音效只取 Dot 绝对值。
- **节流与防双响**：同类音效最小间隔 25~50ms（开球一帧内十几颗球互撞不挤成噪声）；
  球-球双方都收到碰撞回调，按实例 ID 只让一方出声；10 路轮转音源池允许重叠余音。
- **轻触门槛**：法向速度 <0.12/0.15 m/s（球碰球/碰库）不出声，静置贴球与接触抖动无声。
- **资产路线**：音效由 `tools\make_audio.py` 程序化合成（阻尼正弦+低通噪声，可复现），
  以 WAV 预导入 `Assets\Resources\Audio\` —— **不做运行时 AudioClip.Create**
  （踩坑 25：运行时创建的资源在真机不可靠）；2D 播放（spatialBlend=0），响度只由
  撞击速度决定，不随机相机机位漂移。
- **设置面板第五行「音效」**：开/关即时生效，PlayerPrefs 持久化（键 `snk_sfx`）；
  行距 125→105 压缩排版，面板尺寸不变。
- **顺带修正**：`tools\sync.bat` 在 Git Bash 下执行需 `cmd //c "E:\Snooker\tools\sync.bat"`
  （绝对路径）；带 `MSYS_NO_PATHCONV=1` 跑它会让 cmd 进交互模式、bat 不执行（踩坑 41 实测复现）。

**回归：规则 58/58、开球、库边 3/3、加塞 ALL SPIN OK、袋口 6/6；日志确认 SFX 加载 3/3/3 并随出杆触发。**

### v0.45（第 45 次迭代）—— 犯规选择权门禁 + iOS 液态玻璃 UI 全面重做

**① 规则修复：Miss 选框未做出选择前禁止击球**

依据 WPBSA 2024-25 官方规则 Section 3 **Rule 14(b)**（判 FOUL AND A MISS 后，由非犯规方选择：
要求犯规方"从当前位置"或"从原始位置"重打，或自己击球）与 **Rule 13**（Play Again：选择一经提出
不可撤回）——接台方的选择权必须**先于下一杆**行使。

旧版漏洞：判 Miss 弹出选框后游戏立即进入瞄准状态，接台方可以不选择直接出杆——
①静默放弃了 Rule 14(b) 的选择权；②选框残留成无法关闭的僵尸弹窗（结算把 `canReplay`
重置、`replayPrompt` 永久为真，弹窗既挡视线又点不掉）。

修复（三道门禁）：
- `GameManager.ChoicePending`（= `canReplay`）门禁：`CueController.BeginStrike` 与
  `GameManager.Shoot` 在选框未处理时直接拒绝出杆，并提示"请先选择是否让对手重打"；
- 选框追加**全屏透明挡板**（选框子物体，继承其淡入与射线开关）——弹出期间挡住击球/力度/
  瞄准的一切点击，两个选项按钮仍可点；
- 副行文案明确"**请先选择再击球**"。

**自我审核（对照官方规则书 2024-25 版逐条）**：
- Rule 14(b) 三选项：要求从**当前位置**重打 ✓（让对手重打，球位不动）；从**原始位置**重打
  ✗＝简化未实现（需整盘复摆，沿用 v0.35 起的就地标注）；**自己击球** ✓（我自己打）；
- Rule 13 不可撤回 ✓：RequestReplay 后 `canReplay` 立即清零、击球权交回犯规方，无法反悔；
- Rule 10(h)(ii) 犯规方"被要求时必须打下一杆" ✓：重打由犯规方执行；
- Rule 12 自由球是权利非义务 ✓：选"我自己打"放弃资格为既有简化（放弃后按真实球 on 打，合法）；
- 仍未实现（沿用既有标注）：Rule 14(d)(ii) 二次 Miss 警告与三次 Miss 判负（Rule 11(c) 相关）、
  Rule 14 原始位置重摆。

**② iOS 液态玻璃 UI 全面重做（v0.44~v0.45 累计）**

- **UIGlass**：零贴图顶点网格图形（圆角矩形/胶囊/圆形/描边环）——与纯色块 Image 同一条
  "白纹理×顶点色"渲染路径，规避真机贴图坑（踩坑 25）；
- **LiquidGlass.shader**：清澈折射（穹顶法线偏移采样场景，`_Crisp` 清晰占比）+ 菲涅尔边缘
  亮环 + 镜面反射 + **三盏游动光源的实时光晕**（暖白/冷蓝/淡金，程序化漂移）；
- **GlassSceneCamera**：折射源 = 挂主相机的子相机每帧渲染半分辨率 RT，且 LateUpdate 隔帧
  enable 折半成本——GrabPass/相机命令缓冲/OnRenderImage 三种帧缓冲拷贝方案在 MuMu GLES3
  上均间歇性黑帧（编辑器 D3D11 正常），多相机渲染是唯一确定性方案；
- **UIJelly**：按钮 Q 弹（按下压扁、松手欠阻尼弹簧过冲回弹，双轴刚度差=果冻扭动），
  滑块手柄/加塞圆点拖住放大；
- **Haptics**：按钮触感反馈（VibrationEffect 单次脉冲，按下轻点/主按钮确认重点），
  AndroidManifest 增加 VIBRATE 权限；
- ** GLES 铁律**：shader 里 `pow()` 底数必须 `max(…,1e-4)`——`pow(0,k)` 在部分 GLES 驱动
  返回 NaN，曾把整片玻璃中心染黑（编辑器 D3D11 正常，极具迷惑性；用"直接输出顶点色"的
  诊断构建一锤定音）；
- **设置弹窗**：背景随滑入动画逐渐高斯模糊（暗色模糊底 + 亮磨砂面板，iOS sheet 语言）；
- **字号规范**：全部 IMGUI 文字引用 `FontXxx` 常量阶梯（修复各处手写导致的中文字体大小不一）。

**回归：规则 58/58、开球、库边 3/3、加塞全过、袋口 6/6、编辑器截图 11 帧。**

### v0.43（第 43 次迭代）—— 修正"任选彩球"未按指定球计分

依据 WPBSA 2024-25 Section 3 Rule 3(h)(i)："the next ball on is a colour of the striker's
choice which, **if potted, is scored**" —— 计分的必须是**被指定为球 on 的那颗**彩球。
指定蓝球却打进绿球，属于"打进非球 on"（Rule 11(b)(iii)）→ 犯规、本杆不得分、该彩球回点。

旧版对"任选彩球"分支一律 `legalPts += G.Value(k)`（**不比对**指定球），于是
"指定蓝球、进了绿球"会被算成合法的 **3 分**（实际应为**罚 5 分**）。
这类错误在实战里很容易被当成"手气好"而蒙混过去 —— 是逐条审规则时用构造式断言查出来的。

修正后：指定球与实际进球不符 → 按 `max(4, 球 on 分值, 涉及球分值)` 罚分、
进球回点、本杆不计分；未指定时仍按宽松处理（进袋的那颗事后认定，与真实裁判一致）。

**回归：规则 58/58（较 v0.42 新增 4 条"指定球"断言）、开球、库边 3/3、加塞全过、袋口 6/6。**

### v0.42（第 42 次迭代）—— 修正"清彩阶段"阶段切换 / 微调步长降到十分之一

**① 清彩阶段混乱的根因：阶段切换的触发条件写错了**

依据 WPBSA 2024-25 官方规则 Section 3 Rule 3(h)（已核对原文，旧版编号不同）：

> (ii) The break is continued by potting Reds and colours alternately until all the Reds
> are off the table and, **where applicable, a colour has been played at** following the
> potting of the last Red.
> (iii) The colours **then** become on in the ascending order of their value …

关键在措辞 **"a colour has been played at"** —— 只要求那颗"最后一红之后的任选彩球"
**被击打过**，**不要求打进**。所以"进最后一红 → 打彩球"这一杆，无论**进球 / 未进 / 犯规**，
之后的目标球都是**黄球**，彩球自此升序成为球 on。

旧版只在【合法打进】那颗任选彩球时才切到清彩阶段（`if (postReds == 0 && scored)`），
于是：
- 打进 → 切升序（正确）；
- **未进或犯规换手 → 仍停留在"任意彩球"**，接台方还能随便挑一颗彩球打 ← 这就是混乱的根因。

修正后按"这一杆是不是在打那颗任选彩球"来判定阶段切换，与是否进球无关：

| 上一杆 | 修正前 | 修正后 |
|---|---|---|
| 进最后一红 | 打任选彩球 | 打任选彩球 |
| 打任选彩球**并打进**（该球回点） | 打黄球 | 打黄球 |
| 打任选彩球**未进/空杆** | ❌ 仍打任选彩球 | ✅ 打黄球 |
| 打任选彩球**犯规** | ❌ 仍打任选彩球 | ✅ 打黄球 |
| 台面**还有红球**时任选彩球未进 | 打红球 | 打红球（不变） |

顺带把 `SnookerRules.cs` 文件头的条款号按 2024-25 版全部核对更正
（例如"球 on 顺序"是 3(g)/3(h) 而非旧版的 3(e)/3(f)(ii)；"换手后回红球"没有独立条款号，
是 3(g) 的直接推论，旧注释里的 `Rule 10.3` 是过时编号）。

**② 瞄准微调步长降到原来的十分之一**

`◀ ▶` 微调按钮每次的方向增量：`0.0035` → `0.00035` rad（0.2° → 0.02°），
新常量 `CueController.NudgeStep`。原值在长台（约 3.5m）上每按一次偏 12mm，
而长台进球的角度容差只有零点几度 —— 一按就越过目标，根本没法对准；
现在每按一次只偏 1.2mm，可以逐步逼近。

**回归：规则 54/54（新增 10 条清彩断言）、开球、库边 3/3、加塞全过、袋口 6/6。**

### v0.41（第 41 次迭代）—— 袋口真实化：弧形颚部 / 真洞口下坠 / 晃袋

本版把袋口从"视觉装饰 + 脚本判定"改成**有真实物理的袋口**，是球桌物理的一次结构性重做。

**① 颚部改成圆弧（袋口与库边连接处的形状）**
- 现实依据：WPBSA 官方规则书明确把库边端头描述为**曲线** ——
  "The curved face of the cushion is considered to be the area inside the points where
  the cushion face is actually cut into a curve to form the pocket opening."
  （WPBSA Rulebook 2024-25, §2 English Billiards Rule 4 "Cushion Faces"）。
  维基百科独立佐证："On snooker and English billiards tables, the pocket entries are rounded,
  while pool tables have sharp [points]."
- 实现：`make_table.py` 的 `cushion()` 先按斜切生成库边实体，再用**竖直圆柱布尔差集**
  在两端切出圆弧（角袋 R=22mm / 中袋 R=16mm）。圆心取在"端头沿进深方向偏 r"处，
  因此**圆弧与鼻线在端头点相切 —— 袋口开口宽度不变**（手感不回归），端头由直棱变圆角。
- 碰撞体同步：`Bootstrapper.AddJaw()` 从"旋转的 BoxCollider 斜块"改为
  **三角棱柱 MeshCollider（convex）**。原因：旋转盒的直角会伸到鼻线前方形成幽灵墙，
  把贴库滚向袋口的球挡下来；三角棱柱是凸体，既无幽灵墙也无空隙。

**② 落袋判定从"进捕获圈即判定"改为"重力自然下坠"（晃袋的物理前提）**
- 旧版台面是**一整块实体盒**，球物理上掉不进洞，落袋只能靠"球心进半径 0.070 的捕获圈"
  的脚本判定 —— 于是球要么已被判落袋、要么被整块台面挡住，**撞颚弹回在物理上不可能发生**。
- 现在 `BuildClothBed()` 用轴对齐矩形拼出台面并在 6 个洞口处**挖出真的洞**
  （每洞 16 条水平带逼近圆孔，生成 127 块布料板）。
- 落袋判据集中在 `G.InPocket()`（三层，与视觉洞口几何严格对应）：
  ① 球心低于 `-PotDepth` → 必然在袋里（安全网）；
  ② 球心已在洞口圆内**且已下沉到台呢面之下**（y &lt; 0.022）：
     角袋直接判落袋（角袋在台面角上，洞口圆内没有对侧台面，不存在合法"过袋"）；
     中袋还要看**速度朝袋外**的分量够大才算进袋，沿台面横穿洞口是真实的"过袋"，不判；
  ③ 球心已越过角袋袋口中心且在往外走（几何补充）。
  **为什么判据要这么细**（真机实测换来的）：最初只等"掉到 -0.10m 才判"，
  结果高速球在"掉够深度"之前就横移了 0.29m，先撞上袋内衬/布料拼缝边缘被顶飞
  （实测球心弹到 277mm、飞出台外）。判据必须**在球一进洞口就成立**。
- 顺带修掉的两个不真实处：角袋处布料要**朝台面外侧一直敞开**（否则洞口圆与台面边缘之间
  残留的布料"唇"会把沿库滚进角袋的球托住，永远到不了落袋深度）；布料板**不再向四周多留 6cm**
  （旧版那圈"垫在库边下方"的布料在角袋处裸露成台面，球会被它和库边鼻面夹成的直角挤飞，
  实测飞出桌外 3 米）。

**③ 袋内衬（现实中皮革/绒布衬里）：有厚度、开口侧低、后侧高**
- 一圈**30mm 厚**的实体环墙（不是零厚度曲面）。壁厚是必须的：零厚度壁面对 5m/s 的
  球（每步走 10mm）会被穿透，然后被去穿透逻辑顶出来（实测球心弹到 115mm = 1.5²/2g）。
  同时把 `defaultMaxDepenetrationVelocity` 从 1.5 降到 0.6 m/s 作为第二道措施。
- 顶端高度按段分两种：后侧（在库边鼻线之外）高墙 y=+0.045 兜住快球；
  开口侧低墙 y=-0.015 藏在台面下。**两个约束是冲突的**：低墙不挡滚动球但兜不住快球
  （快球要下落 41mm 才够得着，期间已横飞 180mm）；高墙能兜但绕整圈会挡住贴库球。
  只有"分高低"的形状能同时满足。判据用"该段两端点是否都在鼻线之外"——**用"两端都在"而非
  "任一端"**，保证高墙绝不伸进台面。
- 内径硬约束：必须 ≥ 洞口半径 + 球半径 + 余量（取 洞口+36mm）。球在台呢上时球面最远
  伸到洞口+26mm，内衬若比这近，球一进袋口就嵌进衬壁被解算崩飞（实测飞出台外 3 米）。

**④ 袋井从"平齐黑盘"改为真正有深度的杯状空腔（深 400mm）；静置判定提前**
- 井的内壁半径取**比台呢开孔小 2mm**：台呢床身被布尔挖出的洞壁是绿色的（布料材质），
  井壁若在它外面，俯视会看到洞内一圈绿墙；内收后由黑色整个挡住（编辑器截图实测确认）。
- `BallController.Pot()` 不再强加下坠速度（球本来就是自然掉下去的），水平速度压到 5%
  后沉到 `G.PotHideY(-0.25m)` 隐藏。

**新增回归 `PhysTest.PocketTest`（6 用例）**：慢球滚入角袋落袋 / 快球横穿中袋不落袋 /
撞颚弹回台面 / 洞口圈外静止不下坠 / 晃袋（三个速度各测一次，统计与颚面的接触回合数，
v=3.0 出现**连续撞两侧颚** jaw,jaw = 真实晃袋过程）/ 沿角袋轴线正打四档力度
（1.2/2/3/5 m/s）都必须落袋且不飞出桌外、不被严重弹飞。
**回归：规则 44/44、开球、库边 3/3、加塞全过、袋口 6/6。**

**残留现象（已确认、暂不修，无害）**：5 m/s 满力正打角袋时，球在袋口内的**布料拼缝**处
会获得约 1.2 m/s 的向上分量、弹起约 90mm 后仍落入袋中（1.2~3 m/s 无此现象，最高 42mm）。
起因是布料板由 127 块 BoxCollider 拼成，球高速跨越拼缝时接触法线被解算成倾斜。
真实球袋本来也会"跳球"，且球仍正常落袋。回归里以 0.12m 作为防回归红线。

**颚面碰撞体与视觉严格同源**：`JawProfileV(d) = min(斜颚面 d·CushD/JawDx, 颚尖圆弧 r−√(r²−d²))`，
按 d 切成 6 片凸棱柱。先前用"一条直斜线"逼近时，颚尖处与圆弧偏差达 6.4mm（球直径的 12%），
而这段恰是贴库球进袋最先接触的部位（详见踩坑 32）。

**附带发现（未改，留给后续）**：球质量 `rb.mass = 0.17f` 与文献不符 ——
同行评审论文（Loughborough，覆盖 snooker）用 **140.6 g**，而 142g/170g 都查不到一手来源。
WPBSA 规则只规定同套球内质量差 ≤3g，不规定绝对值。因 `a = μg` 与质量无关，
当前只影响撞球动量分配，改动需单独回归，故本版未动。

### v0.40（第 40 次迭代）—— 加塞物理定标 / 物理步长可调 / 原神风界面

本版是 v0.37~v0.40 四轮迭代的收官，三块内容：

- **加塞（杆法）按真实台呢参数重定标**（v0.37）。旧版两个参数都不符合现实：
  - **大力高杆加速过快**：`SpinTopK=1.5` 给出 2.5 倍滚动自旋，远超真实球杆的杆头
    击点极限（最多偏到 0.4~0.5r，再远就滑杆）。物理上限是 `ω·r/v = (5/2)·(h/r)`
    → 高杆 ≤ 1.25 倍。旧值让打滑期长达近 1 秒、白球被滑移摩擦硬推着加速约 2 m/s。
  - **低杆效果不明显**：`SpinLowK=1.6` 只有 0.6 倍倒旋，而真实低杆极限是
    1.0 倍（纯倒旋）。
  - 定标依据取自台球物理实测文献：球-台呢**滑动摩擦系数 μ ≈ 0.2**（范围 0.15~0.4），
    滚动阻力系数 0.005~0.015（本工程 `RollDecel=0.11` ≈ μ_roll 0.011，在范围内）；
    **球重 0.17kg** 对滑动减速度 `a = μ·g` 无影响（质量约掉）——所以正确做法是
    加速度只取 `μ·g`，**严禁由自旋凭空造加速度**，质量只影响动量与对库冲量。
  - **新增硬限速**（按需求）：白球任何时刻速度 **不得超过出杆初速**。高杆打滑加速时
    在 `BallController.CueRollStep` 里钳死（只钳速度、不动自旋，让多余自旋按正常速率
    被台呢消耗——若顺手把自旋写回纯滚动，会把跟进效果瞬间清零，首测踩过这个坑）。
  - 结果：`SpinTopK=1.0`/`SpinLowK=2.0`，满力高杆最大速度恰好钳在 6.00 m/s（不再加速），
    低杆明显加强，并得到一个物理正确的副产物——`spinV≈-0.5` 时 ω≈0，即真实的
    **定杆 stun**（圆盘该档位 HUD 文案就叫「定杆」）。
- **物理步长可在设置里调**（v0.37）：新增三档 **0.5ms / 1ms / 2ms**（默认 2ms），
  改 `Time.fixedDeltaTime` 立即生效，并经 PlayerPrefs 持久化（重启后沿用）。
  设置面板相应扩为四行（帧率/分辨率/阴影/物理步长），行间加金色分隔线。
- **原神风界面**（v0.38/v0.39）：所有按钮重做为「金色细描边 + 深藏青底 + 四角菱形饰钉」，
  主操作按钮（开始游戏/击球/完成）额外带左侧宝石菱；力度滑条改金色轨道 + 菱形手柄；
  设置面板加行间金线；菜单标题加双翼金线与中央宝石；HUD 顶栏加金线；
  加塞圆盘外圈加 12 点金刻度。整体配色统一为「深藏青 + 金」。
- **顺带修复**：设置面板打开时 HUD 文字穿透面板标题（IMGUI 永远画在 uGUI 之上，
  现在设置面板展开时隐藏这些提示文字）；菜单「开始游戏/设置」两按钮边缘贴合。

**新增编辑器截图驱动** `Editor/ShotTest.cs`（v0.38 起）：一条时间线自动跑完
（菜单 → HUD → 设置 → 切步长 → 球在手 → 加塞 → 出杆三帧），一轮出 9 张关键帧，
让 UI 迭代不必每次都装模拟器。踩坑记录见 README 末尾第 23 条。

**回归**（全部通过）：规则 44/44、加塞 9 项断言、库边三用例、开球物理。

### v0.37（第 37 次迭代）—— 见 v0.40 说明（本版为物理修正，与 v0.38~v0.40 同批发布）

### v0.36（第 36 次迭代）—— 球在手 D 区摆球 / 加塞杆法 / 物理步长 2ms
- **修复："球在手"时无法摆球**（本次主要 bug）。旧版开球与白球落袋后，白球只能由
  `RespotCue()` 放在开球线上的固定候选点，玩家**完全无法移动它**——既不符合规则
  （规则要求从 D 区内任意位置开球/击打），实战中也可能被棕球、绿球、黄球挤住导致
  没有出杆线路。现在：开球前与白球落袋后进入 **"球在手"** 状态（`GameManager.cueInHand`），
  直接按住白球就能拖动摆放，位置实时夹取在 **D 区内**（`G.ClampToD`：球心不得越过开球线、
  不得超出 D 圆），并与台面上其它球做重叠分离（`SeparateFromBalls`，两球间距 ≥ 2.05r），
  摆好后正常瞄准出杆。拖动期间会刷新球位快照，避免应用暂停恢复时把白球弹回原位。
- **新增：加塞 / 杆法系统**（本次主要功能）。右下角新增"击球点"圆盘（`SpinPad.cs`），
  拖动盘内小圆点选择杆头位置：圆心中杆、向上高杆（跟杆）、向下低杆（缩杆）、左右左右塞。
  物理实现见 `BallController` 的白球专用模型：
  - **滑动摩擦 + 自旋耦合**（`CueRollStep`）：接触点滑移速度 `u = v + ω × r_c`，
    打滑时摩擦力同时修正线速度与角速度（`Δω = 2.5·a/r·(ŷ × û)`，由 `τ = r_c × F` 推出），
    自旋与滚动因此自然收敛 —— "跟杆推、低杆拉"是模型涌现的结果，不是写死的动画。
  - **中杆零回归**：`spinV=spinH=0` 时初速即纯滚动、滑移为零，与 v0.35 手感逐帧一致。
  - **侧塞撞库改角**（`CushionSpin`）：`Δv = (n × ŷ)·ω_y·r·Grab`，只有撞非球碰撞体才触发。
  - **白球改用零摩擦物理材质**（`cuePM`，`frictionCombine = Minimum`）与 **角阻尼 0**：
    否则 PhysX 会把台呢摩擦与角阻尼再算一遍，与自旋模型双重计算、把低杆效果抹平。
  - 换手/开新局自动把加塞复位到中杆（`UIManager.ResetSpin`）。
- **物理步长 4ms → 2ms（500Hz）**：加塞的滑移模型每步都要修正线速度与角速度，步长越长
  自旋修正越粗糙；同时高速薄球碰撞更精确。用户反馈手机 CPU 性能富余，故直接减半。
  `PhysTest` 的步长与循环次数同步改为 2ms（见踩坑 21）。
- **新增离线物理回归 `PhysTest.SpinTest`**（第三个测试入口），把加塞行为变成可复现断言。
  实测结果（`logs/spintest_v036d.log`，白球正碰单颗红球后相对靶球的位置偏移）：
  | 用例 | offset | 期望 | 结果 |
  |---|---|---|---|
  | A 中杆对照 | **+1.009** | 基线（= v0.35 行为） | ✓ |
  | B 低杆 spinV=-1 | **-0.052** | 显著小于中杆，甚至倒退 | ✓ 被拉回 |
  | B2 中低杆 spinV=-0.5 | **+0.637** | 也要有可感知回缩（无"死区"） | ✓ |
  | C 高杆 spinV=+1 | **+1.131** | 显著大于中杆 | ✓ 继续前冲 |
  | D 侧塞踢出 | 垂直于入射、左右相反、无塞为 0 | 数学断言 | ✓ |
  | E 台面轨迹 | drift = 0.0000 | 侧塞不应让球走弧线 | ✓ |
  全部通过（`ALL SPIN OK`）。D 用例验证的是纯函数 `BallController.CushionKick`
  ——撞库回调 `OnCollisionEnter` 在 `-batchmode` 下不会触发（没有游戏循环调度消息），
  撞库后的实际走位以 MuMu 真机实测为准。
- **高低杆用非对称系数**（`SpinTopK=1.5` / `SpinLowK=1.6`）：中杆(spinV=0)被钉在
  "自然滚动"上，若两侧同系数，低杆要拖到 spinV<-0.67 才出现反旋，
  中间一大段"略低杆"其实是**无旋滑行** —— 真机 A/B 实测发现（同力度 4.46m/s、
  同方向，spinV=-0.58 时白球撞堆后的落点与中杆几乎无差别）。
  改非对称后反旋从 spinV≈-0.4 就出现。真机最终手感（同样 4.46m/s 直球开球，撞堆后白球停在）：
  中杆 x=-1.17m / spinV=-0.36 → x=-0.51m / spinV=-0.85 → 白球被拉回并追进顶袋。
  满低杆一度用 2.5，回缩过猛（白球常自己追进袋），收到 1.6。
- **顺带修复**：主相机补打 `MainCamera` 标签（`Camera.main` 原本返回 null，拖动白球需要
  把屏幕坐标换算成台面坐标）；`BallController.MoveTo()` 新增（拖动摆球用，与 `Place` 的
  "复活/停协程"语义区分开）；`BallController.ClearSpin()`（结算刹停时若只清
  `rb.angularVelocity` 而不清自旋字段，下一物理步会把自旋写回刚体）。
- **修复（v0.35 引入的回归）：关掉辅助线后幽灵球仍然显示**。v0.35 为了让"指定彩球"在
  辅助线关闭时也生效，把 `AimLine.Compute()` 从"仅辅助线开启时调用"改成每帧无条件调用，
  但 `Compute` 内部命中球时会无条件 `ghost.SetActive(true)` —— 于是关闭辅助线后那个
  半透明幽灵球仍留在台面上，**看起来像"多了一颗白球"**（MuMu 实测截图确认：
  台面上只有一颗真实白球，日志 `LogBalls` 也只打印一颗 Cue）。
  修法：新增 `AimLine.visible`，`Show(on)` 设定它，`Compute` 只负责几何与位置、
  幽灵球显隐一律服从 `visible`。

### v0.35（第 35 次迭代）—— 补齐剩余官方细则（指定彩球 / 犯规与未击到 / 自由球）
- **指定彩球 nomination（Rule 3(f)(i)(b)）**：球 on 为彩球时，击球方必须指定打哪一颗。本作用
  "辅助准线指向的彩球"自动作为指定对象，玩家无需额外点击，HUD 显示「已指定 黑球」等提示。
  指定球参与罚分计算——例如指定黑球后白球落袋罚 **7 分**（旧版固定 4 分）。
  注意：`AimLine.Compute()` 改为**无论辅助线是否显示都计算**，因为指定彩球是规则要求，
  不该因玩家关掉辅助线就失去指定能力（`CueController.AimedBall` 转发给 `GameManager`）。
- **犯规与未击到 Foul and a Miss（Rule 11(b)）**：未先击中球 on 且当时**未被斯诺克**时判 Miss。
  判 Miss 后在屏幕中下方弹出黄框提示，接台方二选一：
  【**让对手重打**】→ 击球权交回犯规方、球位保持不动（`GameManager.RequestReplay()`）；
  【**我自己打**】→ 按当前球位正常击球。
- **自由球 Free Ball（Rule 12）**：犯规后若接台方对**所有球 on 都被斯诺克**（无直线击打线路），
  获得自由球资格——可指定任意一颗球当作球 on 打完这一杆：打进按**真实球 on 的分值**计分
  （例如真实球 on 是红球时，指定黑球打进只算 1 分），该球**回点**（红球也回点），
  之后按真实球 on 继续。
- **新增斯诺克几何判定** `GameManager.IsSnookered()`：对每颗球 on 取"中心 + 左右各一个球宽"
  三条路径做球-球遮挡检测，任一路径通畅即未被斯诺克（自由球与 Miss 共用）。
- **新增红球回点** `RespotRed()`：自由球规则下被打进的红球需回点，按官方做法放到粉球点附近空位。
- **规则回归从 30 条扩到 44 条**（新增指定彩球 / 未指定罚分 / Miss 判定 / 自由球计分与回点等断言），
  `logs\ruletest3.log` 实测 `PASS=44  FAIL=0` + `ALL RULES OK`。
- **仓库历史说明**：v0.34 当时只在本机构建验收，**没有单独提交到 git**——它的代码是同本批
  v0.35 改动一起进库的（提交 `affd6d1`，diff 里带 `v0.34：` 注释的那些行即 v0.34 的修复，
  详见下一节）。因此 GitHub 上没有 v0.34 的 tag 与 Release，v0.34 的 APK 只存在于本机。
- 验收：MuMu 实测（`shots\s45_v035_aim.png`）；APK 双架构 IL2CPP，约 70MB。
  已发布 GitHub Release v0.35（Latest）并附 APK。
- 仍未实现（已就地标注）：Miss 累计三次判负（Rule 11(c)(i)）、自由球判定的裁判裁量部分。

### v0.34（第 34 次迭代）—— 规则修正为完整斯诺克规则
- **核心规则错误修正**：进攻中断、换手后接台方重新从红球打起（Rule 10.3 / 3(e)）。旧版换手后仍
  停留在"任意彩球"状态，对手可直接合法打进彩球得分。
- **清彩阶段致命死局修正**：旧版清彩时"目标彩球与白球同杆落袋"（犯规）→ 该彩球不回点、目标无法
  推进 → 一局永远打不完。现在犯规杆打进的彩球一律回点，清彩目标由台面剩余彩球实时推导。
- **罚分按 Rule 10 重写**：最低 4 分取"球 on / 涉及球"较高者；空杆与白球落袋按球 on 分值计；
  连续两杆打红 7 分；同杆多犯规取最高。
- **只剩黑球按 Rule 4 重写**：第一次得分或犯规即终局，打平则重置黑球继续。
- 最后一红之后的任意彩球：该杆犯规换手后，接台方同样以任意彩球为球 on（Rule 3(f)(ii)）。
- 规则判定抽成纯函数 `Scripts/SnookerRules.cs`；新增 `Editor/RuleTest.cs`（30 条断言，30/30 通过）。
- 审计修复：`PhysTest` 恢复物理设置加 try/finally（异常会污染工程设置并打出物理冻结的包）、
  `sync.bat` 改 robocopy 镜像同步（修 CS0101 残留）、147 横幅与设置面板 UI 二次缩放、
  HUD 安全区（非 16:9 不裁切）、力度滑条右端被击球按钮覆盖、HUD 菜单期不显示且不接收点击、
  VSync 导致帧率四档失效、入场运镜两处硬切、`Place()` 协程泄漏、触屏瞄准中途中断、
  `m.bat` 去绝对路径并加错误检查、`BuildGame` 增加播放器设置读回校验。

### v0.33（第 33 次迭代）
- **入场运镜重做（丝滑版）**：直线插值改为**三次贝塞尔弧线**（黑球端高空 → 绕 +X 端
  划弧 → 沿 -Z 库边滑行落位），缓动升级为**五次 smootherstep**（起收一二阶导数为零，
  加速度连续不顿挫），注视点增加指数时间平滑吸收帧率抖动，待机漂移相位从动画结束
  起算（衔接零跳变）。时长 3.4s → 3.6s 更从容。

### v0.32（第 32 次迭代）
- **HUD 单杆分**：顶部两侧改为"玩家名 + 单杆 X"（X 为金色大字，本次连续得分，
  换手/犯规/空杆即清零），总分挪到中央信息行显示。
- **147 满分提示**：同一击球权内连续完成 5 套"红→黑"交替落袋，屏幕底部弹出
  金色描边横幅"147 满分进行中 · 红黑连击 5/15"（0.45s 弹入、停留、0.35s 收回）。
  追踪逻辑：任何偏离红黑节奏的进球/犯规/空杆立即清零重来。
- **满力初速**：MaxShotSpeed 4.6 → 6.0 m/s，100% 力度开球更具爆发力。
- **游戏图标**：Blender 渲染（深绿呢底 + 高光红球主角 + 白球点缀，512²），
  经 BuildGame 写入 Android 应用图标（全 mipmap 密度）。
- 修复：HUD 新布局把文字左/右边缘坐标误当 CRect 中心坐标导致文字出屏。

### v0.31（第 31 次迭代）
- **入场动画**：GameCamera 从黑球端高空俯冲划过整桌落到演示机位（smoothstep 缓动
  + 正弦弧线），菜单在 3.0s 起淡入衔接；菜单落定后缓慢左右漂移；点开始游戏时相机
  平滑俯冲转场到跟球机位。
- **设置面板**（菜单与 HUD 均有入口，从右侧滑入 0.35s easeOutCubic）：
  帧率四档 60/90/120/144、渲染分辨率 50%/75%/100%、画面阴影开关，
  改动即生效并通过 PlayerPrefs 持久化（GameSettings.cs）。
- **桌沿木纹**：Blender 程序化木纹（长条纹噪声+扭曲+细孔）烘焙 1024² 贴图，
  桌框/桌腿 cube-project UV 按米平铺，Bootstrapper 按材质名赋给 Wood。
- **UI 美化**：所有文字带投影；按钮三层结构（深色描边底+主体+顶部高光条）；
  菜单金色装饰条；记分板双方阵营色块（蓝/红）；结算/菜单面板渐变过渡；
  设置打开时菜单文字自动让位淡出。

### v0.30（第 30 次迭代）
- **袋口黑柱下沉至台面平齐**：圆柱顶端从台面上方 44mm 改为台面下 2mm（防共面闪烁），
  观感为真实的下沉式袋口。仅改 `make_table.py` 生成参数，无任何 C# 改动。
- **换手等待 18 秒 → 约 3 秒**（MuMu 实测同力度开球）：
  1. 停判阈值 StopSpeed 0.035 → 0.09 m/s（0.09 以下爬行肉眼几乎不可见）
  2. 停判只看线速度：旧版还等角速度衰减，而 angularDrag 仅 0.08，原地强旋球会拖住
     结算几十秒（隐藏元凶）
  3. 快速收杆：出杆 4 秒后停判阈值放宽到 0.22 m/s，但慢爬球若按剩余滚动距离
     （reach = v²/2a）还够得着袋口捕获圈则继续等——保证不漏判慢滚进球
  4. 结算前统一刹停所有球（含残余角速度）
- `PhysTest.cs` 的 `Physics.autoSimulation` 过时 API 改为 `simulationMode = Script`。

### v0.29（第 29 次迭代）
- **低速粘库修复**：`Physics.bounceThreshold` 0.5 → 0.1。根因：PhysX 对法向速度低于
  bounceThreshold 的接触不施加弹性，轻推/掠射撞库的球会"粘"在库边只能滑行。
  编辑器内 `PhysTest.CushionTest` 三用例（快碰/慢碰/掠射）回归通过。
- **袋口黑柱归位并再缩小**：重写 `make_table.py` 生成参数（角袋黑环半径 0.0625、
  中袋 0.070，位置=捕获圆心）。此前用 Blender 物体缩放导致圆柱被拉向桌心（OBJ 导入
  后物体原点在世界原点的坑）。
- **球体哑光材质**：粗糙度 0.65、金属度 0（哑光酚醛树脂观感；曾历经 0.25+金属0.55
  金属感过重、0.12 高光过锐两版）。
- **帧率上限 120**；**物理步长 5ms → 4ms**（250Hz）。
- **编辑器中文 UI 偏移修复**：HUD 全部 uGUI 改中心锚定，OnGUI 文字用同一
  "屏幕中心 + (设计坐标-设计中心)×k" 映射（k=√(sx·sy)，与 CanvasScaler match 0.5
  一致），任意宽高比下文字与按钮逐像素对齐；菜单/结算遮罩改四角拉伸铺满。
- 台呢布纹贴图（Blender 烘焙 1024² 无缝平铺）+ 带 UV 的球桌 OBJ。

### v1.0.28（第 28 次迭代；首个全链路验收版 —— 当时命名为 v1.0.28，v0.29 起统一为「0.N = 迭代数」）
- 首个 MuMu 完整验收版：进游戏/击球/计分全链路通过（记分板 6:4 实测）。
- 台呢布纹初版、袋口黑柱第一次缩小、双架构（ARM64+X86_64）IL2CPP。
