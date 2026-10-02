# pool_hall.blend —— 台球厅场景（v1，2026-10-02）

用 Blender MCP（blender.exe 5.2.2 + MCP for Blender 插件）围绕游戏球桌构建的台球厅环境场景。
**不含任何人物**。

## 预览

- `shots\hall_main_v4.png` —— 游戏观感机位（主桌斜上 45°）
- `shots\hall_wide_v5.png` —— 全厅广角（三桌 + 吊灯排 + 陈设）

## 场景构成（集合 COL_*）

| 集合 | 内容 |
|---|---|
| COL_Room | 21×10m 房间壳：旧木地板、暗绿灰泥墙 + 深木墙裙、暗色天花 |
| COL_Table | 游戏球桌 ×3（主桌在原点 + x=±7.2 两张关联复制背景桌，台呢 z=0、腿底 z=-0.72） |
| COL_Lamps | 每桌一盏**低垂长条吊灯**（灯罩下沿距台呢 0.85m，真实球房 32~36″ 标准；墨绿外罩/奶白内衬/暖白发光板/双吊杆）+ 天花筒灯 15 盏 |
| COL_Props | 东墙球杆架（6 根球杆）、斯诺克记分牌、挂钟、海报框×3（暗绿呢面+红球图案）、绿椅×4（沿墙）、扶手椅×1（西角）、盆栽×4（Poly Haven 灌木 + 程序化陶盆） |
| COL_Lights | 每桌 300W 暖色面光（灯罩下）+ AgX 曝光 0.55 |

尺度：米制；台呢面 z=0（游戏同款）；地板 z=-0.72；墙高 3.4m。

## 资源与授权

| 资源 | 来源 | 授权 |
|---|---|---|
| wood_floor_worn / painted_plaster_wall（2k 贴图） | Poly Haven | CC0 |
| abandoned_games_room_01（2k HDRI，环境光） | Poly Haven | CC0 |
| GreenChair_01 / ArmChair_01（1k GLTF） | Poly Haven | CC0 |
| anthurium_botany_01 / calathea_orbifolia_01（1k GLTF，灌木） | Poly Haven | CC0 |
| 房间壳/吊灯/球杆架/球杆/海报/记分牌/时钟/花盆/筒灯 | 本项目程序化建模 | MIT（随仓库） |
| 球桌 | make_table.py（本项目） | MIT |

Poly Haven API 直连下载（脚本 `tools\fetch_hall_assets.py`），资产存于 `assets\hall\`。
CC0 无需署名；本文件即为出处记录。

## 产物

- `blender\pool_hall.blend` —— 工程源文件
- `assets\hall\pool_hall.glb` —— 导出模型（~29MB，内嵌贴图；不含 Blender 灯光/相机）

## Unity 集成注意（下一轮）

1. glTF 是 Y-up：Unity 直接导入即可，台呢面 ≈ y=0，与现有物理坐标一致。
2. 场景不含灯光 —— 游戏内由 Bootstrapper 自建（平行光 + 每桌点光/面光），HDRI 不导出。
3. 背景两桌与主桌共享网格数据（关联复制），Unity 导入后同样共享 Mesh，注意只实例化。
4. 绿椅/灌木/扶手椅曾出现"glTF 导出器跳过"问题：已通过把导入网格 JOIN 进程序化网格
   （ArmChair_Joined / Plant_*_Bush）解决 —— 再改场景时，若新加导入模型，导出前先
   解析 GLB JSON 确认节点齐全。
5. 29MB 对移动端偏大：地板/墙是 2k，可降 1k（省一半以上）再进工程。
