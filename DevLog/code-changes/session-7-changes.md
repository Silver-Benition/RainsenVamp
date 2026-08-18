# Session 7 代码改动详情

> **本次性质**：双世界独立波次、调试 UI 和配置资产。
> **日期**：2026-08-18

## 一、新增文件

### `Assets/Scripts/Core/WorldWaveManager.cs`

- 为单个世界维护独立的波次计时器。
- 按 `WaveConfigSO.SpawnRule` 进行速率积分生成。
- 维护每条规则的 `aliveCounts`，支持 `maxAlive`。
- 通过 `WorldEnemySimulation.SpawnEnemy()` 使用对象池生成。
- 副世界隐藏时仍继续推进。

### `Assets/Scripts/UI/WorldWaveDebugUI.cs`

- 只读取协调器和两个世界波次管理器状态。
- 显示当前世界、双方敌人数和 `mm:ss` 波次时间。
- 运行时创建 TMP 文本，避免手写字体材质引用。

### 波次配置资产

- `Assets/Data/Map/GrassWaveConfig.asset`
- `Assets/Data/Map/MechanicalWaveConfig.asset`

两个资产都复用现有弱敌人 Prefab，但使用不同生成速率、半径和数量上限。

## 二、修改文件

### `Assets/Scripts/Core/WorldEnemySimulation.cs`

- 从固定 3 个测试敌人生成器改为世界实体池管理器。
- 缓存敌人的 Renderer 和 Collider2D。
- 新增 `SpawnEnemy()`，绑定世界波次回收通知。
- 当前世界只开启对应敌人的表现和碰撞，副世界 AI 继续运行。

### `Assets/Scripts/Core/WaveSpawnedNotifier.cs`

- 新增 `WorldWaveManager` 绑定重载。
- 回收时将计数通知发送给正确的世界波次管理器。
- 保留旧 `WaveManager` 兼容路径。

### `Assets/Scripts/Data/WorldLineDataSO.cs`

- 新增 `waveConfig` 引用和 `WaveConfig` 属性。
- `IsValid` 改为检查有效波次配置，不再依赖固定测试敌人列表。

### `Assets/Scripts/Core/WorldLineCoordinator.cs`

- 世界槽位增加 `WorldWaveManager` 引用。
- 暴露主世界和副世界波次管理器只读属性，供调试 UI 使用。

### `Assets/Scenes/MainLevel.unity`

- 两个世界运行节点挂载独立 `WorldWaveManager`。
- 协调器绑定两个世界波次管理器。
- Canvas 挂载 `WorldWaveDebugUI`。

### `Assets/Data/Map/GrassWorldLine.asset`

- 绑定 `GrassWaveConfig.asset`。

### `Assets/Data/Map/MechanicalWorldLine.asset`

- 绑定 `MechanicalWaveConfig.asset`。

### `ProjectSettings/EditorBuildSettings.asset`

- 启用场景从 `SampleScene.unity` 修正为 `Assets/Scenes/MainLevel.unity`。
- 同步 `MainLevel.unity.meta` 的真实 GUID。

## 三、Unity 配置等价步骤

1. 在 `MainWorldRuntime` 下挂载 `WorldWaveManager`，绑定 Grass 世界线、同节点 `WorldEnemySimulation` 和 Player。
2. 在 `SubWorldRuntime` 下挂载 `WorldWaveManager`，绑定 Mechanical 世界线、同节点 `WorldEnemySimulation` 和 Player。
3. 在两个世界线资产中分别绑定对应 `WaveConfigSO`。
4. 在 Canvas 上挂载 `WorldWaveDebugUI`，脚本自动创建调试文本。
5. 在 Build Settings 中启用 `MainLevel.unity`。

## 四、验证记录

- Unity 批处理导入/编译：通过，退出码 0。
- 编译期间曾发现 `WorldWaveManager` 通知重载缺失，已补齐并重新验证通过。
- 场景 YAML 引用和组件列表已静态检查。
- 老大已在 Play Mode 验证双世界刷怪、速度、数量上限、切换和调试 UI。
