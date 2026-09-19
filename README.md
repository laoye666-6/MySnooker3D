# MySnooker3D 🎱

**双人斯诺克 3D** —— 基于 Unity 的安卓双人同屏斯诺克游戏（当前为Beta版v0.33）。标准比赛尺寸球桌由 Blender
脚本化建模，真实物理（PhysX + 自定义滚动摩擦），完整简化斯诺克规则，可开关辅助瞄准线，
内置入场运镜动画与设置界面（帧率 / 分辨率 / 阴影）。

*A 2-player split-screen snooker game for Android, built with Unity 2022.3 +
procedural Blender modeling. Realistic physics, full simplified snooker rules,
toggleable aiming aids, cinematic intro camera and in-game settings.*

## 📥 下载安装（Android）

[![Download APK](https://img.shields.io/badge/下载_APK-v0.33_约70MB-brightgreen?style=for-the-badge)](https://github.com/laoye666-6/MySnooker3D/releases/latest)

- **直接下载** → **[Snooker3D.apk](https://github.com/laoye666-6/MySnooker3D/releases/download/v0.33/Snooker3D.apk)**（约 70MB）
- 或前往 [**Releases 页面**](https://github.com/laoye666-6/MySnooker3D/releases) 查看全部版本与更新说明
- **安装步骤**：下载 APK → 传到手机 → 点击安装；首次安装需在系统设置里允许「安装未知来源应用」
- **系统要求**：Android 7.0+；支持 arm64-v8a 真机与 x86_64 模拟器（MuMu 等）

> 💡 GitHub 仓库主页只能看到源码文件，安装包放在 **Releases**（右侧栏「Releases」也可进入）。
> 想从源码自行编译请见下文《从源码构建》。

| 主菜单 | 对局中 | 设置 |
|---|---|---|
| ![menu](docs/screenshots/menu.png) | ![gameplay](docs/screenshots/gameplay.png) | ![settings](docs/screenshots/settings.png) |

| 记分板 HUD | 入场运镜 |
|---|---|
| ![hud](docs/screenshots/hud.png) | ![intro](docs/screenshots/intro.png) |

## ✨ 特性

- **真实物理**：4ms 物理步长 + 连续碰撞检测，球-球弹性 0.92、库边反弹、台呢滚动摩擦，
  低速撞库也能正常弹开；袋口捕获 + 出界兜底
- **简化斯诺克规则**：红/彩计分（1~7 分）、首触犯规与误落罚分（+4~+7）、白球重置、
  彩球回点、清彩阶段、黑球决胜；单杆分实时显示；连续 5 套红黑弹出 147 满分提示
- **辅助瞄准线（可开关）**：幽灵球落点、目标球走向（按球色）、白球分离线、库边反弹预测
- **双人同屏**：轮流击球、实时记分板（单杆分 + 总分 + 目标球 + 剩余红球）
- **设置界面**：帧率四档（60/90/120/144）、渲染分辨率（50%/75%/100%）、阴影开关，
  滑入动画，PlayerPrefs 持久化
- **入场运镜**：贝塞尔弧线绕台飞行 + smootherstep 缓动 + 注视点时间平滑
- **全脚本化美术管线**：球桌/贴图/图标均由 Blender 无头脚本生成，改参数即可复现

## 🎮 操作

| 操作 | 方式 |
|---|---|
| 瞄准 | 桌面空白处左右拖动；或左下 ◀ ▶ 微调（每次 0.2°） |
| 力度 | 右下角滑条 |
| 击球 | 右下角"击球"按钮 |
| 辅助线 | 左下"辅助线：开/关"切换 |

## 🛠 从源码构建

### 环境要求
- Unity **2022.3.62f3c1**（含 Android Build Support：SDK/NDK/OpenJDK）
- Blender 3.x+（仅重新生成模型/贴图时需要）
- 目标设备：Android 7.0+（真机 ARM64 或 x86_64 模拟器，如 MuMu）

### 步骤
1. 用 Unity Hub 打开 `UnityProject/`（首次导入会自动生成 Library，需数分钟）
2. 命令行构建 APK：
   ```bat
   "C:\Program Files\...\Unity.exe" -batchmode -quit -nographics ^
     -projectPath <本仓库>\UnityProject ^
     -executeMethod BuildGame.BuildAndroid -logFile build.log
   ```
   产物：`UnityProject\Builds\Snooker3D.apk`（调试可加 `-x86only` 只编 x86_64，IL2CPP 时间减半）
3. 或直接在 Unity 编辑器中 **File → Build Settings → Build**

### 重新生成美术资产（可选）
```bat
blender -b -P blender/make_table.py     :: 球桌模型（改尺寸/袋口参数）
blender -b -P blender/bake_cloth.py     :: 台呢+木纹贴图 + UV 重导出
blender -b -P blender/make_icon.py      :: 应用图标
```
产物在输出目录，拷入 `UnityProject/Assets/Resources/` 对应子目录即可。

> `tools/` 内的 sync.bat / m.bat 是开发期便捷脚本，**内含本机绝对路径**，克隆后请按
> 自己的环境修改。

## 📁 目录结构

```
├─ UnityProject/          # Unity 工程（Assets 含全部 C# 源码与资源，可直接打开）
│  └─ Assets/Scripts/     # 运行时代码（G/Bootstrapper/GameManager/... 全中文注释）
├─ blender/               # Blender 建模/烘贴图/图标脚本
├─ tools/                 # 开发期便捷脚本（需按本机路径修改）
└─ docs/screenshots/      # 截图
```

核心脚本一览：`G.cs`（常量）、`Bootstrapper.cs`（纯代码搭场景）、`GameManager.cs`
（规则引擎）、`BallController.cs`、`CueController.cs`、`AimLine.cs`、`GameCamera.cs`、
`UIManager.cs`、`GameSettings.cs`、`Editor/BuildGame.cs`（命令行构建）、
`Editor/PhysTest.cs`（离线物理回归）。

## ⚠️ 已知坑（改代码前必读）

- 模拟器（MuMu）只认 arm64-v8a/x86_64，必须 IL2CPP；引擎代码裁剪必须关闭
  （`stripEngineCode = false` + `Assets/link.xml`），否则 SphereCollider 等被剥掉
- 白球不可与棕球同点重生（PhysX 会炸膛）；休眠刚体赋速度前必须 `WakeUp()`
- `ProjectSettings/DynamicsManager.asset` 的 `m_SimulationMode` 必须为 `0`——
  编辑器里跑过手动步进测试会把 Script 模式持久化，导致真机物理冻结
- 部分安卓机 uGUI 动态字体渲染空白 → 本项目文字全部走 IMGUI，中文用内嵌
  DroidSansFallback（Apache-2.0）
- 更多细节见源码内中文注释

## 📄 许可证

本项目代码以 [MIT License](LICENSE) 开源。

第三方资产致谢：
- [DroidSansFallback.ttf](UnityProject/Assets/Resources/Fonts/) —— Apache-2.0（AOSP）
- NotoSansSC / NotoSansCJK（可选备用字体，未随仓库分发）—— SIL OFL 1.1
- Unity、Blender、MuMu 为各自所有者的商标
