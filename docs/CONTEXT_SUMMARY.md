# 双人斯诺克 3D —— 项目上下文总结（截至 v1.0，2026-10-18）

> 本文件为完整压缩上下文，供导入 AI 助手继续开发使用。
> 项目根目录：`E:\Snooker`（工作区）；Unity 工程：`E:\Snooker3D`
> **本机 Bash 工具是 Git Bash**，路径写作 `/e/Snooker`；执行 .bat 用 `cmd //c "E:\Snooker\tools\sync.bat"`（绝对路径）
> robocopy/adb 前先 `export MSYS_NO_PATHCONV=1`；**但跑 .bat 时不要带该变量**（会把 `cmd //c` 的转换也禁掉 → cmd 进交互模式、bat 不执行）
> **新增铁律（v0.57）**：`export MSYS_NO_PATHCONV=1` 在同一 shell 会话内会污染后续的 `taskkill //IM` 等命令（双斜杠不再转换 → 静默失败 → Unity 僵尸进程堆积）。脚本里如需该变量，务必放在子 shell `( export ...; cmd )` 或每步显式清除。

---

## 一、项目概述

基于 **Unity 2022.3.62f3c1** 的安卓**双人同屏斯诺克游戏**。球桌由 **Blender 5.2.2 LTS（Steam 版）**
脚本化建模（标准比赛尺寸 3569×1778mm），物理用 Unity PhysX + 自定义滚动摩擦 + 自建白球自旋模型，
规则为**完整官方斯诺克规则**（WPBSA 2024-25，65 条断言回归），带可开关辅助瞄准线、
贝塞尔入场运镜、设置界面、单杆分 HUD、147 满分提示、加塞杆法、真实袋口物理、
胜利结算号角（v1.0 起仅此一条音效——击球/碰撞/碰库音效已删除）、
iOS 液态玻璃 UI（v0.56 起改为**深色玻璃 + 全白字体**）、台球厅环境场景、胜利结算动画、
复位选项（Rule 14(b) 三选一）。

**v0.56~v0.57 大版本主题：向参考站 PCOL（http://www.heyzxz.me/pcol/）追平美术与光影。**
- **俯身击球视角**（照 PCOL 反编译参数换算）
- **暗厅 + 吊灯灯池布光**（PCOL 的 "暗厅亮桌" 语言）
- **ACES 全屏色调映射**（自建 RT 调色链，绕开真机会黑帧的路径）
- **台呢方块纹路根治**（法线从正弦网格改纯绒噪）
- **主桌吊灯按 PCOL 重建**（程序化梯形灯箱 + 过曝发光底板）
- **站立观察按钮**（瞄准时一键起立俯瞰全台）
- **UI 修黑色圆角阴影 / 绿色闪烁 / 全白字体**

**已开源**：https://github.com/laoye666-6/MySnooker3D （public / MIT）
**当前版本**：**v1.0（第 58 次迭代，versionCode 58）** —— 相对 v0.57 只做了两件事：
删除 v0.46 的击球/球碰球/碰库三类碰撞音效（资源 + 代码），版本号抬到 1.0。
**交付物（当前）**：`E:\Snooker3D\Builds\Snooker3D.apk`（双架构 IL2CPP）。

---

## 二、环境与路径（全部实测有效）

| 项 | 路径 |
|---|---|
| Unity 编辑器 | `E:\Program files\2022.3.62f3c1\Editor\Unity.exe`（Android 模块齐全） |
| Blender | `D:\Program Files\STEAM\steamapps\common\Blender\blender.exe`（5.2.2，已配 MCP 插件协议 13） |
| MuMu 模拟器 | `E:\Program files\Netease\MuMu\nx_main\`（adb.exe / MuMuManager.exe），adb 端口 127.0.0.1:16384 |
| git / gh | `D:\Program Files\Git\cmd\git.exe`；`C:\Program Files\GitHub CLI\gh.exe`（已登录 laoye666-6） |
| Unity 工程 | `E:\Snooker3D`（`Assets\Scripts` / `Assets\Editor` 由 sync.bat 同步） |
| **源码母本（权威）** | `E:\Snooker\unity_src\Scripts`（**18 个 cs**）+ `unity_src\Editor`（**5 个**） |
| 母本 shader | `E:\Snooker\shaders\`：`LiquidGlass.shader`、`GlassBlur.shader`、`ColorGrade.shader`（v0.57）、**`BallShadow.shader`（v1.0）** —— sync.bat 只同步 *.cs，**需手工拷入** `Assets\Resources\Shaders\` |
| 其它母本 | `android\AndroidManifest.xml`（需手工拷入 Assets\Plugins\Android\） |
| Blender 脚本 | `E:\Snooker\blender\`：make_table / bake_cloth / make_icon / open_table_edit / render_pockets / render_pocket_diag / pool_hall.blend + hall_*.py |
| 资产产物 | `E:\Snooker\assets\`：table.obj+mtl、cue.obj、cloth/wood 贴图、**cloth_normal/wood_normal/ball_shadow（v0.56）**、net_texture.png、audio\、hall\、icon.png、preview_* |
| 工具脚本 | `E:\Snooker\tools\`：sync.bat / m.bat / regress.sh / make_audio.py / **make_surface_maps.py（v0.56）** / **shot_retry.sh（v0.57）** / **pin_unity.py（v0.57）** / fetch_hall_assets.py / make_net_texture.py / 若干 ps1 |
| 截图/日志 | `E:\Snooker\shots\editor\`（ShotTest 17 帧）、`E:\Snooker\shots\`（MuMu 历史）、`E:\Snooker\logs\` |
| PCOL 逆向物 | `E:\Snooker\ref\pcol\`：index.html/index.js/0.chunk.js/1.chunk.js/game.js（美化版）/babylon.js、`assets\`（21 张原始贴图+HDR）、**`ref_menu.png` / `ref_stand.png` / `ref_aim.png`（PCOL 实拍参照图）** |
| 文档 | `E:\Snooker\README.md`、`CONTEXT_SUMMARY.md`（本文件）、`CONTEXT_SUMMARY_v055_backup.md`（旧版留档）、`DSH_IMPORT_PROMPT.txt` |

包名 `com.snookerlab.snooker3d`；Activity `com.unity3d.player.UnityPlayerActivity`

---

## 三、代码架构（全部含详细中文注释）

### 运行时 `unity_src\Scripts\`（18 个）

- **G.cs** — 全局常量：球桌尺寸(米)、袋口几何、滚动摩擦 0.11、出杆初速 0.55~6.0、停判阈值 0.09、
  置球点、分值/颜色/中文名、ColorOrder、加塞物理参数、JawRCorner=0.048 / JawRCenter=0.026。
  **v0.56**：`ClothCol/CushionCol/WoodCol` 改为 PCOL 校准的赛事深绿/深胡桃；
  **`BallColor()` 全部按 PCOL 线性色重校准**（白球 0.95/0.88/0.76、红 0.48/0.01/0.01、
  黄 0.74/0.48/0、绿 0/0.27/0.14、咖啡 0.34/0.14/0、蓝 0.08/0.24/0.52、粉 0.76/0.31/0.35、黑 0.02³）。
- **SnookerRules.cs** — **纯函数规则引擎**（不碰物理/UI）。TableState + ShotFacts → ShotOutcome。
- **Bootstrapper.cs** — 纯代码搭场景。**v0.56/57 大改**：
  - `Init()` 开头 **`Application.runInBackground = true`**（修 ShotTest 冻结，见踩坑 51）
  - `Lighting()`：平行光 1.05→**0.10**、补光→**0f**、**Trilight 三色环境光**
    （sky 0.26/0.26/0.28、equator 0.20/0.20/0.21、ground 0.10/0.105/0.095）
  - 台球厅材质：地板 0.40/0.33/0.25、绿墙 0.28/0.32/0.22；
    **灯罩内衬/灯板/筒灯 → 自发光 3.4**（`MAT_ShadeInner`/`MAT_LampPanel`/`MAT_Downlight`）
  - 布光：**MainTableSpot**（暖白 2.1 / range 6.5 / angle 100 / 软阴影 / y=1.50）+
    **MainTableFillSpot**（无影 / 1.5 / range 7.6 / angle 125 / y=2.45）+
    两侧背景桌点光 1.1 + **四壁洗墙点光 1.5/range11**
  - **`BuildReflectionProbe()`**：球台反射探针（512、boxProjection、ViaScripting、
    **AllFacesAtOnce**），**必须在 22 颗球创建前 RenderProbe**（否则球拍到自己的像）
  - **`BuildPcolLamp(0f)`**：v0.57 主桌吊灯按 PCOL 重建——隐藏旧 `Shade_Out_0/In_0/Panel_0`
    与旧 `LampGlowBottom_0/Skirt_*`；程序化搭：细吊杆（r=0.012，1.86→2.66）+
    梯形黑罩（顶 2.04×0.84 / 底 2.60×1.16，顶 y=1.86 / 底 y=1.60，4 片 12° 外倾侧板 + 顶盖，
    近黑哑光 0.022）+ **罩底发光板**（自发光 4.2，尺寸 1.7×罩底半宽）
  - 球：`NewMat(..., rough 0.20, metal 0)`（光面酚醛）；`ApplyNormalMap(cloth/cushion 0.22, wood 0.6)`；
    台呢 tint 0.92/0.60/0.58 × cloth_texture、木框 tint 0.34/0.30/0.28 × wood_texture
  - **贴地假阴影**：每球一个 Quad（0.135m≈2.6×球径，`ball_shadow.png`，alpha 混合），
    **`DestroyImmediate(sh.GetComponent<Collider>())`**（踩坑 50）
  - 相机：FOV 52、near 0.01、far 60，AddComponent **`ScreenGrade`** + **相机补面灯**
    （Point 0.45 / range 2.6 / 无影，挂在相机上，治低机位球背光面）
- **GameCamera.cs** — 入场贝塞尔运镜 + 菜单漂移 + 跟球/全景。
  **v0.56/57**：瞄准机位 = **白球后方 0.42m、台呢上方 0.11m、注视前方 0.55m(y=0.045)**；
  **`public bool standView`** 起立观察 = **白球后方 1.55m、上方 0.85m、注视前方 1.15m(y=0.02)**；
  跟瞄时间常数：贴近目标时 τ=0.08s（否则 0.2s）；
  菜单机位 `DemoPos(-2.75, 1.62, -2.7) / DemoLook(0.35, -0.12, 0.3)`（球台外对角、灯+台+厅同框）
- **ScreenGrade.cs（v0.56 新增，v0.57 重写）** — ACES 全屏调色链：
  主相机 `targetTexture = sceneRT`（全分辨率+24bit 深度+MSAA2），
  **PresentCam**（depth+10、orthographic、cullingMask 只含 layer 3）渲染一块
  **GradeQuad**（layer 3，材质 `ColorGrade.shader` 采样 sceneRT 做 ACES）；
  主相机 cullingMask 排除 layer 3。`Update` 轮询 Screen 尺寸重建 RT 与四边形宽高比。
  **禁止**改回 OnRenderImage/帧缓冲读法（踩坑 52）。
- **BallController.cs** — 落袋/下沉/重置/滚动摩擦/碰撞上报；白球自旋模型（`CueRollStep` /
  `CushionKick` / `PocketLaunchGuard`）；`RestoreTo` 精确复位。
  **v0.56**：`public Transform shadowQuad` + `LateUpdate()` 同步贴地假阴影
  （alpha=0.85×随离地高度衰减，落袋隐藏，MaterialPropertyBlock 写 alpha）。
- **CueController.cs** — 瞄准角(0.0018rad/px)/力度/出杆动画/加塞/拖球；NudgeStep=0.00035。
- **AimLine.cs** — 幽灵球解析几何 + 目标球走向 + 分离线 + 库边反弹；线宽 7mm。
- **UIManager.cs** — **v0.56 深色玻璃主题**：`GlassSheet/GlassPanel 0.10/0.12/0.16 α0.52~0.70`、
  `GlassGreen 0.16/0.60/0.34 α0.80`、`GlassBlue 0.20/0.46/0.86 α0.74`、
  **Ink/Ink2/AccentOrange/TextRed 全部白色**（Ink2 = 白 72%）。
  **移除全部 `AttachShadow` 调用**（黑色圆角阴影根因）。
  **v0.57**：新增 `standText` 字段 + `ToggleStand()`（翻转 `GameCamera.standView`）+
  `UpdateStandLabel()`；HUD 第二行新增 **StandBtn**（uGUI 220×62 @ x=-360）+ IMGUI 文字 @ x=600。
- **UIGlass.cs** — `UseBlur()` 参数真正生效 + **去饱和 0.45**、`glow=0`；
  `UseRefraction()` **去饱和 0.60**、`glow=0`、`lift 0.05`（绿色闪烁根治）。
- **GameSettings.cs** — 帧率/分辨率/阴影/物理步长/音效；`Apply()` 里 `Screen.SetResolution`
  会触发 ScreenGrade 重建 RT（已适配）。
- **Sfx.cs** — **v1.0 起只剩胜利结算号角**（`Win()`，sfx_win_1）；v0.46 的三类碰撞音
  （Cue/Ball/Cush、速度→响度/音调映射、节流、10 路音源池）已连同 9 个 WAV 一并删除。
- **GlassSceneCamera.cs** — 副相机半分辨率 RT + 四分之一分辨率两轮高斯磨砂链（_GlassScene/_GlassBlur）。
  `CopyFrom(main)` 会复制主相机 cullingMask（已排除 layer 3）与 targetTexture（随后覆写自己的 RT）。
- **SpinPad.cs / UIJelly.cs / UIConfetti.cs / Haptics.cs** — 同 v0.55 职责（加塞圆盘 / Q 弹 / 彩纸 / 触感）。

### Editor `unity_src\Editor\`（5 个）

- **BuildGame.cs** — 命令行构建；**版本号在此改，当前 `1.0` / versionCode `58`**。
- **RuleTest.cs** — **65 条规则断言**（纯函数，不走物理）。
- **PhysTest.cs** — 离线物理回归四入口：`Run` 开球 / `CushionTest` 库边 / `SpinTest` 加塞 / `PocketTest` 袋口。
- **ShotTest.cs** — 编辑器内自动截图驱动（**17 帧**：01 菜单 / 02 瞄准 / 03 设置 /
  04 步长 / 05 球在手 / 06 加塞 / **06b 站立观察** / 07~09 出杆 / 10~11 袋口 /
  12~14 结算 / 15 复位 / 16 Miss）。**不能加 -quit/-batchmode，窗口须前台**。
- **AssetImport.cs（v0.56 新增）** — `Fix`：把 cloth_normal/wood_normal 设为 NormalMap、
  ball_shadow 设 alphaIsTransparency；`Diag`：打印厅 FBX 灯罩材质名；
  **`OpenMain`**：打开 `Assets/Scenes/Main.unity` 并保持编辑器运行（验收用；
  `-openfile` 对该工程不生效）。

---

## 四、PCOL 逆向结论（美术/光影参照系，重要）

PCOL = Babylon.js + CANNON/OIMO 的网页斯诺克。其代码经 js-beautify 后分析：

**相机（`game.js` 第 22~28 行、3713~3818 行）**
- `fov: 0.88`（≈50°）、`minZ: 0.01`
- `eyeStandHeight: 1.7`、`eyeSquatHeight: 1.0`、`innerTrackRadius: 0.38`
- 俯身映射：`wi=-0.9`（垂直角范围）、`Ri=-0.3`（头部前移）、`Pi=-0.2`（下降）、
  `Di=0.3`（注视偏移）、`Li=0.35`；站立↔俯身 `Power1.easeInOut 1.2s`
- 其台呢 y=0.88 → 俯身眼高 1.0 只比台呢高 **0.12m**（本项目换算为 0.11m）

**光影（`game.js` 第 275~360 行）**
- 场景 `.babylon`：`GameLight`（type2 Spot, pos y=3.5, intensity 20, angle 175°）
  + `PocketLight`（type0 Point, intensity 3）；其余全靠烘焙
- 台呢材质 `pcolCarpet`：线性色 `(0.008,0.07,0.015)`、normal ×30 平铺、
  光照图 RGB 打包（G=直射 B=间接 R=遮蔽）、**ACES**
- 木框：`PBRMetallicRoughness` metallic=1/roughness=0.8 + HDR 反射 cubemap + 法线/粗糙度/遮蔽贴图
- 球：`pcolDyIBL`（自定义 PBR）F0=0.04、roughness=0.1、烘焙球 AO、
  **光探针网格 5×3×2** 插值辐射度/辐照度、贴地假阴影（2.6×球径、alpha 0.8）
- **所有 shader 内都有 ACES**（Narkowicz 拟合）——这是"电影感"的主因
- 色板：台呢深赛事绿、木框近黑深胡桃、墙暖棕
- **音效：PCOL 完全没有音频**（两个 chunk + 全部资源零音频代码/文件，是无声游戏）——
  所以"参考网站音效"无法照搬，本项目音效按真实斯诺克声学特征自行合成

**法律边界**：PCOL 美术资源有版权，**禁止拷贝其模型/贴图进本项目（MIT 开源）**；
只复刻技法与参数，贴图全部用本项目程序化管线重生成。

---

## 五、常用命令

```bat
:: 同步源码母本 → 工程（robocopy /MIR，只镜像 *.cs）
cmd //c "E:\Snooker\tools\sync.bat"
:: 同步到开源仓库工程：先 set SNOOKER_PROJECT=E:\Snooker\MySnooker3D\UnityProject 再跑 sync.bat

:: 手工拷（sync 不同步）：shader → Assets\Resources\Shaders\
::   ColorGrade.shader / LiquidGlass.shader / GlassBlur.shader
:: 贴图 → Assets\Resources\Textures\（cloth_normal/wood_normal/ball_shadow）
:: 音效 → Assets\Resources\Audio\（assets\audio\*.wav）

:: 贴图导入修正（法线类型/alpha）+ 编译检查（幂等）
Unity.exe -batchmode -quit -nographics -projectPath "E:\Snooker3D" ^
  -executeMethod AssetImport.Fix -logFile "E:\Snooker\logs\import.log"

:: 全量回归（串行 5 道；Unity 工程锁独占不能并行；期望全绿）
bash E:\Snooker\tools\regress.sh
::   RuleTest 65/65 | PhysTest.Run / CushionTest(3 bounced=True) / SpinTest(ALL SPIN OK) / PocketTest(PASS=6)

:: ShotTest 截图（带窗口，不加 -batchmode/-quit；用重试脚本自动处理冻结）
bash E:\Snooker\tools\shot_retry.sh
::   失败时手动：杀 Unity → 删 Temp → 启动 → python E:/Snooker/tools/pin_unity.py 钉窗

:: 打开主场景供人工验收（保持编辑器）
Unity.exe -projectPath "E:\Snooker3D" -executeMethod AssetImport.OpenMain

:: 构建 APK —— 构建前：taskkill adb.exe java.exe；删 Temp；查注册表死代理
Unity.exe -batchmode -quit -nographics -projectPath "E:\Snooker3D" ^
  -executeMethod BuildGame.BuildAndroid -logFile "E:\Snooker\logs\build.log"

:: 台呢/木纹贴图 + 法线（Blender 烘 + Python 生成）
"D:\Program Files\STEAM\steamapps\common\Blender\blender.exe" -b -P E:\Snooker\blender\bake_cloth.py
python E:\Snooker\tools\make_surface_maps.py
:: 音效重生成
python E:\Snooker\tools\make_audio.py

:: MuMu（Git Bash 下 export MSYS_NO_PATHCONV=1；路径写 E:/ 形式）
"E:\Program files\Netease\MuMu\nx_main\adb.exe" connect 127.0.0.1:16384
adb -s 127.0.0.1:16384 install -r -t E:/Snooker3D/Builds/Snooker3D.apk
adb shell am start -n com.snookerlab.snooker3d/com.unity3d.player.UnityPlayerActivity
```

---

## 六、v1.0 / v0.56 ~ v0.57 改动全记录（本轮）

### v1.0（第 58 次迭代）—— 删除碰撞音效，正式版号

1. **删除 v0.46 的三类碰撞音效**（用户要求）：`Sfx.Cue/Ball/Cush` 与其速度→响度/音调映射、
   变体随机、同类节流、10 路音源池全部移除；`BallController.CollisionSfx()` 与
   `GameManager.Shoot()` 的 `Sfx.Cue` 调用删除；`Assets/Resources/Audio` 下
   `sfx_cue/ball/cush_1..3.wav` 共 9 个文件删除（APK 瘦身）。
2. **保留** v0.51 胜利号角 `sfx_win_1` + 设置面板「音效」开关（该开关现在只控号角）；
   `Sfx.cs` 精简到约 70 行；`tools/make_audio.py` 只生成 `sfx_win_1.wav`。
3. **版本号 0.57 → 1.0**，versionCode 57 → 58（`BuildGame.cs` 的读回校验同步改）。
4. **装机冒烟修掉两处设备侧渲染 bug**（编辑器正常，v0.56/57 从未出包所以没暴露）：
   ① 贴地假阴影在 MuMu/GLES3 上变硬边黑方块（运行时 Standard + `_ALPHABLEND_ON`
   变体不进包、贴图 alpha 移动端可能丢）→ 新增无光照程序化 shader `BallShadow.shader`
   （单变体、无贴图、uv 半径算 alpha；`_Color.a` 仍由 BallController 用 MPB 写）。
   ② 全景机位 2.5m 从罩顶俯视时吊灯侧板单面剔除 → 穿透看到白色发光板（屏幕顶部白楔）
   → `BuildTrapezoidPanel` 改双面（复制顶点 + 反向绕序）。
5. 回归 5 道全绿；构建 APK；MuMu 装机冒烟（菜单/开球/出杆）；同步开源仓库并发布 GitHub Release v1.0。

### v0.57（第 57 次迭代）—— 站立观察 / 台呢去方块纹 / PCOL 吊灯 / ACES 光影

1. **站立观察按钮**：瞄准态 HUD 第二行「起立观察」（按下变「俯 身」）。
   `GameCamera.standView` 切换双机位（详见架构节）。ShotTest 新增 `06b_stand_view`。
2. **台呢方块纹路根治**：
   - `make_surface_maps.py` 的 `make_cloth_normal()` 重写：**删除经纬正弦乘积**
     （正弦乘积天然产生亮斑阵列 = 方块纹），改三层无序 value noise
     （420 细绒 / 180 绒簇 / 60 云斑，权重 0.52/0.30/0.18，strength 5.0）；
   - `bake_cloth.py` 织纹幅度 `weft_strength 1.0 → 0.25`，重烘 cloth/wood 贴图。
3. **主桌吊灯按 PCOL 重建**：`Bootstrapper.BuildPcolLamp()` + `BuildTrapezoidPanel()`
   （详见架构节）。隐藏旧罩体与旧发光面片，避免重叠。
4. **ACES 光影追平**：`ScreenGrade.cs` + `shaders/ColorGrade.shader`
   （ACES + 曝光 1.06 + 对比度 1.06 + lift 0.035 + 暗角 0.24）。
   纯多相机路径：主相机渲 RT → 展示相机画全屏四边形（layer 3）。
5. **灯池对比度**：补光 range 9.5→7.6、环境光下调、主灯 2.1。
6. 版本号 → 0.57/57。

### v0.56（第 56 次迭代）—— 俯身视角 / 暗厅灯池 / 全白 UI / 音效重调

1. **俯身击球机位**：白球后 0.42m、高 0.11m、注视前 0.55m；跟瞄 τ=0.08s。
2. **UI 三 bug**：
   - **黑色圆角阴影** = `AttachShadow` 软投影（暗厅里读作黑块且从磨砂面板后透出）→ **全部移除**；
   - **绿色闪烁** = LiquidGlass 采样高饱和绿呢随相机变化 → **去饱和 0.45/0.60 + 游动光晕 glow=0**；
   - **字体全白** + 深色玻璃主题（GlassSheet/Panel 深灰蓝，Ink/Ink2/Accent/TextRed 全白）。
3. **暗厅灯池布光**：平行光 0.10、补光 0、Trilight 环境、主桌 Spot + 无影补光 Spot +
   四壁洗墙点光、厅材质调暗、灯罩自发光。
4. **材质写实化**：PCOL 校准色（台呢深绿/木框深胡桃/球色）、球 roughness 0.20 光面、
   织纹/木纹法线贴图、**反射探针**（AllFacesAtOnce）、**贴地假阴影**（DestroyImmediate 去碰撞体）、
   相机补面灯。
5. **音效重调**（PCOL 无音频）：球碰球 click 收紧到 2.2~3.5ms、库边 95/168/410Hz 更闷、
   杆头 1350Hz 皮革嗒 + 短房间混响（28ms/0.18/湿 0.15）。
6. 新工具/文件：`tools/make_surface_maps.py`、`tools/pin_unity.py`、`tools/shot_retry.sh`、
   `Editor/AssetImport.cs`、`Scripts/ScreenGrade.cs`、`shaders/ColorGrade.shader`、
   `ref/pcol/`（逆向物与参照图）。

---

## 七、物理要点（容易改错，改动前必读）

1. 白球自旋（加塞）**不能靠 PhysX**，是 `BallController.CueRollStep` 自建滑移摩擦模型。
   三条硬约束：① 白球物理材质摩擦 0；② 白球 angularDrag=0；③ 非对称系数
   （`G.SpinTopK=1.0`/`SpinLowK=2.0`），否则中低杆是"无旋滑行"死区。
2. 定标：球-台呢 μ≈0.2；杆头击点极限 h≤0.5r → 高杆≤1.25×(v/r)、低杆≥-1.0×(v/r)。
   **白球速度任何时刻不得超过出杆初速**（用户明确要求，代码有钳制；只钳速度不动自旋）。
3. spinV=spinH=0（中杆）必须与旧版逐帧一致 —— 加塞实现正确性的第一道关。
4. 低速粘库 = `Physics.bounceThreshold` 0.1。
5. 白球不可与棕球同点重生（PhysX 炸膛）；赋速度前必须 `WakeUp()`。
6. 袋口四条硬约束（v0.41）：① 颚弧与鼻线相切；② 台面挖真洞 127 块板，
   PocketLaunchGuard 钳袋口弹射（只 y≤8cm 生效）；③ 袋内衬 30mm 厚、开口低(-0.015)/
   后侧高(+0.045)、内径≥洞口+36mm、defaultMaxDepenetrationVelocity=0.6；④ 落袋判据
   `G.InPocket` 三层，球一进洞口就成立。
7. 布料板不能比台面多留一圈；角袋布料朝台面外敞开。

---

## 八、规则要点

1. 规则改动**必须**走 `SnookerRules.cs` + RuleTest 断言（65 条），不许写进物理/UI 代码。
2. **逐字读动词**：played / potted / struck 是三种不同条件。
3. 凡出现"指定的球""球 on"限定词，计分前**比对一次**对象是否匹配。
4. 条款号按 2024-25 版：球 on 顺序 = 3(g)/(h)；ball on 定义 = S2 R11；罚分 = S3 R11；
   犯规判定 = R10；Miss = R14；自由球 = R12。
5. Miss 三选一（Rule 14(b)）由非犯规方选择；`ChoicePending` 门禁不许绕过；
   另有非官方"双方同意复位"（`ReplacementApproved`，两人都同意才回退整盘含比分）。

---

## 九、液态玻璃 / UI 要点（v0.56 现行）

1. UIGlass 零贴图顶点网格（**真机 UI 禁运行时贴图**）；uv0=[-1,1] 局部坐标 +
   OnPopulateMesh 推 `_RectHW/_CornerR`。
2. 双场景源：_GlassScene（半分辨率清晰）/ _GlassBlur（四分辨率磨砂）由 GlassSceneCamera 产出。
   **GrabPass/命令缓冲/OnRenderImage 读帧缓冲在 MuMu GLES3 间歇黑帧——别再试。**
3. GLES 铁律：pow() 底数 max(…,1e-4)；**Fallback 吞 shader 编译错误**；
   **MuMu 不作玻璃验收环境**（验收 = 编辑器截图 + 真机）。
4. **去饱和是"绿色闪烁"的解**：控件 0.60 / 面板 0.45；**glow=0**（游动光晕也是闪烁源）。
5. **不要给按钮/面板加 AttachShadow**（暗厅里=黑色圆角阴影 bug）。
6. 深色主题下白字必须配深底；玻璃面板填充 alpha 0.52~0.70。

---

## 十、踩坑精选（新增 50~53；完整列表见 README）

1. MuMu 只认 arm64-v8a/x86_64 → 必须 IL2CPP 双架构；stripEngineCode=false + link.xml
2. 工程物理资产 `m_SimulationMode` 必须为 0；测试代码 try/finally 恢复
3. uGUI 动态字体部分机渲染空白 → 文字一律 IMGUI + 内嵌 DroidSansFallback
4. **运行时创建的 UI 贴图真机不可靠** → 纯色块、顶点网格、预导入贴图 + 按材质名重建
5. uGUI 中心锚定 y 向上 vs 设计坐标 y 向下：pos.y = 540 - 设计y；IMGUI CRect cx 是中心
6. 编辑器里禁调 Screen.SetResolution（ShotTest 时必须）；ShotTest 不加 -quit/-batchmode 且须前台
7. 编辑器测试与 APK 构建**不可并行**（工程锁）；跑完残留场景 → 双击 Main.unity
8. 推送前必 `git fetch`；gh release 建完必须 `release view` 核对非 Draft 且 APK 在列
9. 含中文 .bat 在 GBK 控制台闪退 → 批处理纯 ASCII
10. git push 网络抖动 → 自动重试循环（90~120s 间隔）；连不上按用户授权放弃推送、保留本地 commit
11. `Physics.Simulate` 手动步进不派发 OnCollisionEnter → 测试用 OverlapSphere 自检；
    白球直接赋 rb.velocity 会被限速钳 0 → 测试用 `ApplySpin(dir,speed,0,0)`
12. 构建卡 "Detecting Android SDK"：杀 adb/java + 删 Temp；查注册表死代理 ProxyServer
13. MuMu 被关 → `MuMuManager.exe control -v 0 launch`（约 55s）
14. 布料板拼缝高速弹起 ~90mm（回归红线 0.12m）
15. Blender：中文 UI 节点名本地化 → 用 inputs 索引；OBJ 导入自动转 Z-up
16. Blender FBX `use_selection` 只选根对象会漏件（空父级不遍历）→ 逐对象选中
17. 袋口库边底悬空 → 库边加下摆（CUSH_SKIRT 0.055）
18. 玻璃"去白边"≠"面板调透"——边缘光学与面板填充分开调
19. OBJ 材质集变更后须连 table.mtl 同拷；仍不行删 table.obj.meta 强制重导
20. Blender MCP 会话禁跑 read_factory_settings（会杀服务器）
21. **玻璃元素 alpha=0 后残留"轮廓幽灵"**：shader 边缘 alpha 必须乘法 `a*(1+edge)`，不能加法
22. **IMGUI 永远画在 uGUI 之上**：模态面板期间 HUD 必须整体淡出（`!replayPrompt && !replaceOpen && settingsAlpha<0.4`）
23. **UI 动画**：EaseTo 指数 ease-out，出比入快；弹窗 materialize；高频元素 ≤0.3s
24. **MuMu 不作玻璃验收环境**；玻璃验收 = 编辑器 + 真机
25. **袋口实拍重建**：皮革颚板 + 蜂窝网兜 + 大弧颚（JawR 0.048/0.026）
26. **（50）假阴影四边形的碰撞体必须 `DestroyImmediate`**：`Destroy` 是帧末生效，
    离线测试（Physics.Simulate 手动步进）没有帧循环 → 22 块贴台碰撞体残留，
    直接污染 SpinTest（实测低杆/定杆/高杆三个断言全挂）。任何"纯视觉"物体去碰撞体
    都要用 DestroyImmediate。
27. **（51）ShotTest 冻结（协程全灭）的一个确定性诱因**：`Application.runInBackground`
    原先只在 ShotDriver.Start 设置——启动竞态下编辑器失焦会暂停整个播放循环，
    而设置它的 Start 还没跑 → 死锁在 "entering play mode"。**把 runInBackground
    提前到 Bootstrapper.Init 开头**后，冻结率大幅下降（配合 shot_retry.sh 基本可无人值守）。
28. **（52）`targetTexture` 的相机不会自动上屏**：主相机渲 RT 后若没有第二个渲到屏幕的
    相机，Game 视图显示 "Display 1 No cameras rendering"。ACES 链必须配一台展示相机
    （只画调色四边形）。**且四边形尺寸必须按屏幕宽高比拉伸**（正交相机视野宽度 = 高度×宽高比），
    否则宽屏上球变竖椭圆；**尺寸要在创建时立即设置**（RebuildRT 早于 MakeQuad 执行，
    时序顺序错会让四边形保持 1×1 → 居中黑边 + 图像横向压缩）。
29. **（53）`export MSYS_NO_PATHCONV=1` 会污染同一 shell 后续的 `taskkill //IM`**：
    双斜杠不再被转换 → taskkill 静默失败 → Unity 僵尸进程堆积、工程锁被占，
    后续所有实例都停在 "已有 Unity 实例打开此项目" 对话框。脚本里必须
    避免全局 export，或在每次 taskkill 前 `unset MSYS_NO_PATHCONV`。
30. **反射探针必须 AllFacesAtOnce**：IndividualFaces 分帧模式要等编辑器派帧，
    叠加播放模式进入时机时会冻在 entering play mode。且**必须在创建球之前 RenderProbe**
    （否则球拍到自己的像）。
31. **灯罩内自发光面板从浅掠射角不可见**：罩口 1.60 / 面板 1.63，菜单机位仰角仅 ~5°，
    面板几乎侧棱。解法：罩底整面发光板 + （v0.57 起）重建为 PCOL 式灯箱。

---

## 十一、当前功能全清单（均已验收）

菜单（ACES 暗厅 + 球台外对角机位 + 入场运镜 + 深色玻璃）→ 设置（帧率/分辨率/阴影/物理步长/音效，
持久化）→ 开始游戏 → 瞄准（拖动/微调±0.02°/辅助线开关/准线自动指定彩球）→
**俯身击球视角**（眼高 0.11m）→ **起立观察按钮**（俯瞰全台球型）→ 力度滑条 → 击球 →
真实物理（滚动摩擦/库边反弹/真洞落袋/晃袋/加塞/皮革颚板+蜂窝网兜）→ 完整规则（65 断言）→
计分（HUD 单杆分）→ 147 提示 → 结算（卡片 Q 弹/比分跳数/彩纸/胜利号角/最高单杆）→
深色液态玻璃 UI（磨砂+边缘拉丝+Q 弹+触感+全白字体）→ 胜利结算号角 →
**台球厅环境**（三桌/吊灯/墙裙/球杆架/记分牌/挂钟/海报/座椅/盆栽，暖色室内光 + 四壁洗墙灯）→
**ACES 全屏调色**（曝光/对比度/暗部提亮/暗角）。

---

## 十二、遗留 / 可做（诚实差距清单）

- **未实现规则**：Miss 累计三次判负（Rule 11(c)(i)/14(d)(ii)）、自由球裁判裁量。
- **与 PCOL 的剩余差距**：
  1. PCOL 用**烘焙 GI + 逐材质 ACES + HDR 光探针 IBL**，我们是 Built-in 实时光 + 全屏 ACES
     近似——大关系已接近，极端灯下（高光滚落曲线）仍略逊；
  2. PCOL 房间有海报/沙发/吧台凳等贴图资产，我们的厅是程序化陈设（同量级但丰富度不同）；
  3. 背景两桌无装饰球。
- 台球厅整体偏暗（有意，可调 `MainTableSpot`/`MainTableFillSpot`）；
  MuMu 上玻璃多数元素不渲染（踩坑 43，真机正常）。
- APK 约 78MB：两个未引用备用字体 ~48MiB 可移出 Resources；台球厅贴图可 2k→1k。
- 无背景环境音；球杆无贴图；彩球无数字贴纸。
- 仓库可做：GitHub Actions 自动构建、Topics、英文 README。
- **发版**：v1.0 已构建并发布（删除碰撞音效 + 正式版号）；v0.56/v0.57 的改动一并进入 v1.0。

---

## 十三、工作方式约定（用户明确要求，务必遵守）

- 每轮给一组编号修改要求；versionName 0.NN = 第 NN 次迭代，versionCode 递增（在 BuildGame.cs 改）。
- 物理/玩法 bug 先编辑器复现修复、回归通过再出 APK；中间迭代用 ShotTest 截图验证即可，
  最终打磨/发版阶段才上 MuMu；玻璃 UI 验收 = 编辑器 + 真机（MuMu 不作数）。
- 改视觉模型尽量不动 C#（改 Blender/MCP 定形后参数落回 make_table.py）；源码母本
  `E:\Snooker\unity_src` 必须保留，改动先写母本再 sync。
- 发版流程 = 改母本 → 改版本号 → 构建 → 同步 MySnooker3D → commit/push（先 fetch，抖动重试）→
  gh release（核对非 Draft+APK 在列）；已推送历史不改写，出 bug 直接发新版。
- 用户要求**成品质量**（"不要拿塑料游戏糊弄我"，时间充裕可多迭代）；交付前需在 Unity
  编辑器内打开供其验收。
- 源码三副本：unity_src（权威）→ E:\Snooker3D\Assets（构建）→ MySnooker3D\UnityProject（仓库）。
