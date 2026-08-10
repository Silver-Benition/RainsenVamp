# Session 6 · X 变量地图基建与双世界线 MVP

> **本次性质**：核心玩法开发会话，完成地图流式基建，并验证“双世界线并行运行”的最小可行路径。
> **日期**：2026-08-10
> **任务编号**：`XVAR-PARALLEL-WORLD-MVP-001`

---

## 一、本次对话的核心工作

| 类别 | 项目 | 状态 |
|---|---|---|
| 协作规范 | 新增 Agent 双阶段规划/执行规范与 Unity 工程规范 | ✅ 已完成 |
| 地图素材 | 生成草地、机械地板及两套掩体像素素材 | ✅ 已完成 |
| 地图基建 | 建立数据驱动主题、MapSegment 和 MapStreamManager | ✅ 已完成 |
| 世界线 MVP | 主世界/副世界并行运行与 F 键切换 | ✅ 已完成 |
| 场景配置 | MainLevel 改为 WorldLineCoordinator + 两个世界运行节点 | ✅ 已完成 |
| 原有刷怪 | 暂停 MainLevel 中的 WaveManager，避免与 MVP 测试敌人重复刷怪 | ✅ 已完成 |

---

## 二、新增/修改的 C# 类与核心方法

### 数据层

#### `Assets/Scripts/Data/MapThemeDataSO.cs`

- 保存地面 Sprite 变体、区块尺寸和确定性随机种子。
- `IsValid`：检查主题 ID 和全部地面 Sprite 引用。
- `GetGroundTile()`：将 Sprite 包装为共享的运行时无碰撞 Tile。
- `ReleaseRuntimeTiles()`：卸载运行时 Tile，避免编辑器反复进入 Play Mode 时积累对象。

#### `Assets/Scripts/Data/WorldLineDataSO.cs`

- 保存世界线稳定 ID、本地化键、地面主题、掩体 Sprite 和掩体布局。
- 保存 MVP 测试敌人 Prefab 及其初始生成偏移。
- `IsValid`：校验世界线是否具备完整运行配置。
- `GetCoverTile()`：创建共享的运行时 Grid Collider Tile。

### 地图运行时层

#### `Assets/Scripts/Core/MapSegment.cs`

- `Initialize()`：同时填充地面 Tilemap 和掩体 Tilemap。
- `FillGroundBuffer()`：根据世界坐标和主题种子稳定生成地面变体。
- `FillCoverBuffer()`：按世界线布局生成掩体，并为玩家保留安全半径。
- `SetPresentationActive()`：开关地面与掩体 Renderer。
- `SetInteractionActive()`：开关掩体 TilemapCollider2D。
- `Clear()`：清理区块中的地面和掩体 Tile。

#### `Assets/Scripts/Core/MapStreamManager.cs`

- 每个世界独立维护固定数量的 `3x3` 区块窗口。
- `SwitchWorldLine()`：替换单个世界上下文使用的世界线配置。
- `SetPresentationActive()`：隐藏副世界地图，但不停止其区块更新。
- `SetInteractionActive()`：只允许当前世界的掩体参与物理交互。
- 跨区块移动时复用已有 MapSegment，不持续 Instantiate/Destroy。

### 双世界线运行时层

#### `Assets/Scripts/Core/WorldLineCoordinator.cs`

- 同时持有主世界和副世界上下文。
- `SwitchWorldLine()`：切换玩家的世界归属，不重置玩家 Transform、PlayerStats、经验或武器。
- 当前世界开启地图 Renderer、掩体 Collider 和敌人交互。
- 副世界关闭表现和交互，但不停止敌人 AI。
- MVP 阶段使用 F 键触发切换。

#### `Assets/Scripts/Core/WorldEnemySimulation.cs`

- 每个世界固定生成 3 只测试敌人。
- 使用现有 `PoolManager` 和 `EnemyBase`。
- 两个世界的敌人都读取同一个 Player Transform 并持续追踪。
- `SetWorldActive()`：仅控制敌人 Renderer 和 Collider，不停止 FixedUpdate AI。

### 删除的临时脚本

- `MapThemeDebugSwitcher.cs`：删除旧的单世界主题切换入口，改由 `WorldLineCoordinator` 管理主世界/副世界切换。

### 明确未修改的核心系统

- `PlayerStats.cs`
- `EnemyBase.cs`
- `WaveManager.cs`
- `ProjectileBase.cs`
- 武器升级、经验拾取和 UI 系统

---

## 三、引入的新机制与设计模式

1. **双世界并行运行时上下文**
   - 主世界和副世界各自拥有地图区块和测试敌人。
   - 两个世界共享玩家坐标，但只允许当前世界与玩家交互。

2. **表现层/交互层双重开关**
   - 副世界不是停止运行，而是关闭 Renderer 和 Collider。
   - 敌人 AI 继续追踪玩家，切换后可以看到其已经移动到新的位置。

3. **WorldLineDataSO 内容包**
   - 世界线不再等同于地面主题。
   - 当前内容包包含地面、掩体和测试敌人；未来可扩展独立波次、道具和世界机制。

4. **同坐标世界线切换**
   - 两个世界使用相同的世界坐标系和区块尺寸。
   - 切换世界不移动玩家、不重载场景、不重置玩家状态。

5. **掩体安全区**
   - 世界线切换或区块生成时，玩家周围保留安全范围，避免被新掩体嵌入。

6. **MVP 范围控制**
   - 暂不复制完整 WaveManager。
   - 暂不保存世界状态快照。
   - 暂不实现子弹与掩体碰撞。
   - 暂不实现玩家属性的世界线差异。

---

## 四、Unity 资源与场景配置

### 新增地图素材

- `Assets/Art/Tilemaps/Grass/GrassTiles.png`
- `Assets/Art/Tilemaps/Grass/GrassCover.png`
- `Assets/Art/Tilemaps/Mechanical/MechanicalTiles.png`
- `Assets/Art/Tilemaps/Mechanical/MechanicalCover.png`

地图素材采用 `32x32` 像素单元、`32 PPU`、Point 过滤和无压缩配置。

### 新增数据资产

- `Assets/Data/Map/GrassMapTheme.asset`
- `Assets/Data/Map/MechanicalMapTheme.asset`
- `Assets/Data/Map/GrassWorldLine.asset`
- `Assets/Data/Map/MechanicalWorldLine.asset`

### MapSegment Prefab 层级

```text
MapSegment
├── Grid
├── Ground Tilemap
├── Ground TilemapRenderer
├── Rigidbody2D（Kinematic）
└── Cover
    ├── Cover Tilemap
    ├── Cover TilemapRenderer
    └── TilemapCollider2D
```

### MainLevel 场景层级

```text
WorldLineCoordinator
├── MainWorldRuntime
│   ├── MapStreamManager
│   └── WorldEnemySimulation
└── SubWorldRuntime
    ├── MapStreamManager
    └── WorldEnemySimulation
```

### Inspector 等价配置

- `WorldLineCoordinator` 绑定主世界资产、副世界资产和 Player Transform。
- `MainWorldRuntime/MapStreamManager` 使用 `GrassWorldLine.asset`。
- `SubWorldRuntime/MapStreamManager` 使用 `MechanicalWorldLine.asset`。
- 两个 `WorldEnemySimulation` 分别绑定对应世界线和 Player Transform。
- 两个世界的 `MapSegment Prefab` 均绑定 `MapSegment.prefab`。
- `WorldLineCoordinator.switchKey` 配置为 `KeyCode.F`。
- `WaveManager` 在 MainLevel 中停用。
- `TestSpawner` 保持停用。

本次以上 Unity 搭建均由 Agent 直接写入 `.unity`、`.prefab`、`.asset` 和 `.meta` 序列化文件完成，未要求雨弦手动拖拽配置。

---

## 五、问题修复与验证

### 已修复问题

- 世界主题 Sprite 初次使用了错误的多 Sprite `fileID`，导致主题被判定为无地面素材；已改为 `.meta` 中对应的 `internalID`。
- MapSegment Prefab 初次将 GameObject ID 写入 Transform 子节点列表；已改为正确的 Transform ID。
- 原有 WaveManager 会与 MVP 测试敌人重复刷怪，已在 MainLevel 场景停用。

### 验证状态

- Unity 2022.3 脚本编译：✅ 通过。
- 世界线 ScriptableObject 导入：✅ 通过。
- MapSegment Prefab 导入：✅ 通过。
- 场景序列化引用静态检查：✅ 通过。
- Play Mode 自动测试：⚠️ 因 Plastic 认证交互阻塞，未完成自动化验证。
- 用户已在 Unity 中看到地图和掩体运行效果；双世界敌人同步和 F 切换仍需手动回归验证。

---

## 六、推荐的下一步 Todo List

### P0 · MVP 回归

- [ ] 在 Play Mode 等待数秒后按 F，确认副世界敌人已经移动到不同位置。
- [ ] 再按 F 切回主世界，确认主世界敌人状态没有重置。
- [ ] 验证玩家只与当前世界掩体和敌人交互。
- [ ] 验证两个世界的活动区块数量始终为 9。

### P1 · MVP 完善

- [ ] 增加当前世界和两个世界敌人数的调试 UI。
- [ ] 将固定测试敌人替换为按世界归属运行的轻量 WaveManager。
- [ ] 增加世界切换冷却、切换动画和切换失败反馈。

### P2 · 完整 X 变量

- [ ] 编写完整版 X 变量功能说明文档。
- [ ] 设计每个世界独立的运行时状态快照。
- [ ] 设计独立敌人池、波次、道具和拾取物状态。
- [ ] 设计玩家状态在不同世界线中的差异。
- [ ] 评估世界专属碰撞层与独立 PhysicsScene2D。
- [ ] 决定子弹与掩体交互规则。

---

## 七、归档格式变更

从 Session 6 起，所有 `/save` 摘要新增：

**Unity 手动搭建/等价配置步骤**

必须记录脚本挂载对象、Inspector 引用、Prefab 层级、场景对象、按键、Layer、Collider 及本次由 Agent 自动完成的等价配置操作。
