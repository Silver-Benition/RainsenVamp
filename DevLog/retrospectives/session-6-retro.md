# Session 6 · 复盘与隐患记录

> **本次性质**：X 变量地图基建与双世界线 MVP。
> **日期**：2026-08-10
> **核心结论**：X 变量不能被建模为单纯的地图主题切换，至少需要独立的世界运行上下文。

---

## 一、关键架构决策

### 1. 从单世界重绘升级为并行世界上下文

最初的实现是：

```text
F 键 → MapStreamManager.SwitchTheme() → 重绘当前地图
```

该模型只能改变背景和当前区块内容，无法保存副世界敌人状态，也无法让副世界继续运行。

MVP 改为：

```text
MainWorldRuntime
SubWorldRuntime
        ↓
两个世界同时运行
        ↓
WorldLineCoordinator 只切换玩家交互归属
```

这个方向更接近 X 变量的真实需求，也为完整版世界状态快照保留了入口。

### 2. 主世界/副世界是角色，不是永久类型

当前用 `main` 和 `sub` 表示初始角色，但底层仍应以稳定的 `worldLineId` 识别世界。

未来切换后：

- 玩家当前所在世界成为交互主世界。
- 另一个世界成为非交互副世界。
- 世界本身的 ID 不应该因为角色变化而改变。

### 3. 副世界不能简单 SetActive(false)

如果直接关闭副世界 GameObject：

- 敌人 AI 会停止。
- 波次计时会停止。
- 世界状态无法继续推进。

因此 MVP 只关闭：

- Renderer。
- 掩体 Collider。
- 敌人 Collider。

不关闭：

- 世界运行对象。
- 敌人 GameObject。
- 敌人 FixedUpdate AI。

---

## 二、本次工程经验

### 1. Unity 配置可以通过序列化文件自动完成

本次没有要求雨弦在 Inspector 中手动创建大量对象，因为 Agent 直接生成或修改了：

- `.unity` 场景文件。
- `.prefab` Prefab 文件。
- `.asset` ScriptableObject 文件。
- `.meta` GUID 和导入配置。

这不是“没有 Unity 配置”，而是把 Inspector 操作转化为 Unity 序列化数据写入。

复盘时仍必须记录等价的手动搭建步骤，否则开发者会失去从脚本到场景对象的完整链路。

### 2. 本地 fileID 不能凭经验猜测

多 Sprite 图集的子资源引用必须使用实际 `.meta` 中的 `internalID`。

教训：

- 不能看到 Sprite 就默认使用 `21300000` 系列 fileID。
- 资产生成后必须检查 `.meta`。
- ScriptableObject 引用错误时，Unity 可能只在运行时表现为“资源为空”。

### 3. Prefab YAML 的 GameObject ID 和 Transform ID 不同

`Transform.m_Children` 必须引用 Transform fileID，不能引用子节点 GameObject fileID。

教训：

- 手工写 Prefab YAML 时必须检查组件类型和 fileID 对应关系。
- Unity Prefab 导入日志必须纳入验证流程。

### 4. 静态导入通过不等于运行时通过

本次 Unity 批处理成功完成了：

- C# 编译。
- ScriptableObject 导入。
- Prefab 导入。
- 场景序列化读取。

但自动 Play Mode 测试受到 Plastic 认证交互阻塞，不能仅凭批处理退出成功就宣称双世界行为验证通过。

后续归档必须明确区分：

- 已静态检查。
- 已编译验证。
- 已运行验证。
- 未验证。

---

## 三、当前 MVP 的明确风险

### 1. 两个世界的敌人处理成本翻倍

当前两个世界都运行 EnemyBase AI，敌人逻辑成本至少翻倍。

MVP 只生成每世界 3 只测试敌人，避免过早引入完整双波次系统。

### 2. 副世界敌人不真正处理掩体路径

副世界掩体 Collider 被关闭，副世界敌人可以穿过掩体继续追踪玩家。

这是为了避免 MVP 同时解决双世界物理隔离。完整版需要重新设计：

- 世界专属 Layer。
- 世界专属 Collider 过滤。
- 或独立 `PhysicsScene2D`。

### 3. 世界状态仍然是“活对象状态”，不是可保存快照

当前切换后，两个世界的敌人位置会保留，因为它们一直存在。

但还没有：

- 世界独立波次时间。
- 道具拾取状态。
- 掩体破坏状态。
- 世界专属玩家状态。
- 存档和恢复机制。

### 4. 原有 WaveManager 暂停而不是世界化

这只是 MVP 的有意取舍，不代表 WaveManager 已支持双世界。

后续恢复刷怪系统时，必须让每个 WorldRuntimeContext 拥有独立的波次和生成统计。

---

## 四、需要保留的长期设计方向

完整版应围绕以下对象展开：

```text
WorldLineDataSO
        ↓ 静态配置
WorldRuntimeState
        ↓ 本局状态
WorldRuntimeContext
        ↓ 运行时系统
WorldLineCoordinator
        ↓ 玩家世界归属
Player Interaction Context
```

未来需要讨论：

- 玩家状态是全局共享还是按世界保存。
- 敌人是否跨世界保存位置、血量和状态。
- 道具和经验球是否按世界独立存在。
- 世界切换时是否存在无敌帧和过渡动画。
- 子弹、武器范围和掩体的世界归属。
- 是否需要真正的独立 PhysicsScene2D。

---

## 五、下一次复盘前的验证目标

- 在 Play Mode 等待一段时间后切换到副世界，确认副世界敌人位置已经发生变化。
- 切回主世界，确认主世界敌人没有被重置。
- 确认玩家只能碰撞和伤害当前世界敌人。
- 确认副世界掩体不会阻塞当前玩家。
- 记录两个世界同时运行时的敌人数量和帧率表现。
