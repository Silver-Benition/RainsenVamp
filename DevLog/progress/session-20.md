# Session 20 进度归档：地图即时拾取物、全场冻结与高清像素基线

- 日期：2026-09-04
- 基线：`ea17933`（Session 19 Boss、胜利条件与完整战斗结算）
- 实施分支：`main`
- 归档提交：见包含本文件的 `session-20` 提交
- 范围：舰长治疗拾取物、水晶球冻结拾取物、资源拾取外飘动画、冻结蓝色蒙版、高清世界渲染与六枚能力图标优化。

## 本次目标

1. 实现两件可池化地图即时效果拾取物：舰长恢复 45 点生命，水晶球冻结敌对世界 15 秒。
2. 地图即时效果道具只允许玩家近身拾取，不响应玩家磁吸范围。
3. 经验和金币进入磁吸范围后先向外短暂飘散，再加速飞向玩家。
4. 冻结期间保留玩家行动，并在玩家下方、其余世界上方显示蓝色蒙版。
5. 建立高清像素渲染基线，改善地图拾取物、经验、金币和能力图标在游戏内的清晰度。
6. 把高密度场景性能验收登记为正式待办；即使性能不达标，也优先优化性能而不是回退低像素版本。

## 已完成

- 新增无状态 `MapInstantEffectSO` 效果策略与 `MapInstantEffectContext` 玩家上下文，运行时状态不写回共享资产。
- 新增 `HealingMapInstantEffectSO`：舰长实际恢复 45 点生命；满血、死亡或依赖缺失时返回失败，不伪报成功统计。
- 新增 `WorldFreezeMapInstantEffectSO`：水晶球请求 15 秒敌对模拟冻结；重复拾取取当前剩余时间与新时长的较大值，不叠加。
- 新增通用池化 `MapInstantEffectPickup`：只接受同一碰撞对象上的正式 `PlayerStats`，因此 `MagnetRadius` 子物体不会提前触发。
- 地图即时效果道具不实现 `IMagneticPickup`，玩家必须走入半径 0.55 的近身触发器才能拾取。
- 普通弱敌人的地图道具基础掉落概率为 2.5%；判定成功后按舰长 4、水晶球 1 的权重选择，每次最多生成一件。
- 新增 `WorldFreezeController` 作为本局冻结唯一权威，不修改 `Time.timeScale`：玩家、玩家武器、经验金币磁吸与本局权威计时继续运行。
- 冻结期间暂停普通敌人和 Boss 移动、动画、接触伤害、远程攻击、Boss 弹幕、敌方弹体位移与寿命、波次生成和 Boss 预警计时。
- 解冻或对象回池时恢复刚体约束、弹体动量和 Animator 原始速度，避免冻结状态泄漏到下一池生命周期。
- 新增 `WorldFreezeOverlay`：通过冻结状态事件控制蓝色蒙版显隐；蒙版位于普通世界上方、玩家与玩家血条下方。
- `PlayerHealth` 新增 `RestoreHealth(float)` 返回真实恢复量，同时保留 `Heal(float)` 兼容入口。
- 新增 `MagneticPickupMotion`，统一承载经验与金币的三阶段状态：`Idle -> Scatter -> Homing`。
- 外飘参数为 0.18 秒、0.45 世界单位、0.3 横向漂移比例；随后从速度 5 开始以 15 的加速度追踪玩家。
- 外飘方向使用实例 ID 的确定性哈希生成，不污染 Unity 全局随机序列，不在热路径创建协程或临时集合。
- 经验与金币从自身脚本移除重复磁吸状态，改由 Prefab 上唯一 `MagneticPickupMotion` 负责表现。
- 地图拾取物采用 64×64 源图、PPU 128，显示尺寸为 0.5 世界单位；经验和金币统一显示为 0.25 世界单位。
- 金币与经验 Sprite 改为 Point、Clamp、无压缩、无 Mipmap、关闭回退物理形状；Prefab 保持 `localScale=1`，既有碰撞半径不变。
- `MainLevel` 的 Pixel Perfect Camera 改为直接按输出分辨率渲染，并使用 Pixel Snapping + Point；取消 480×270 中间纹理的先缩小后放大链路。
- 六枚能力图标已实际重制并覆盖同名 48×48 PNG：力量训练、疾行训练、冷却优化、磁力核心、逆境本能、反击脉冲。
- 六枚能力图标保留原 `.meta` 与 GUID，统一为硬边 0/255 Alpha；升级选择图标容器由 100×100 改为 96×96，形成精确两倍显示。
- 新增 `DevLog/plans/hd-rendering-performance-validation.md`，记录高清基线下的敌群、弹幕、掉落物与冻结蒙版性能验收方案。

## 关键运行链路

地图道具掉落与拾取：

```text
EnemyBase 死亡
  -> EnemyDropTableSO 独立执行 2.5% 地图道具掉落判定
  -> MapInstantEffectDropResolver 按 4:1 权重选择舰长或水晶球
  -> PoolManager 生成对应 Prefab
  -> 道具保持静止，不响应 PlayerMagnet
  -> 玩家本体进入 CircleCollider2D
  -> MapInstantEffectPickup 调用 MapInstantEffectSO.TryApply
  -> 效果实际成功时由 MapInstantEffectPickupReporter 写入结算统计
  -> 道具归还对象池
```

水晶球冻结：

```text
WorldFreezeMapInstantEffectSO.TryApply(15 秒)
  -> WorldFreezeController 刷新剩余时间
  -> FreezeStateChanged(true)
  -> WorldFreezeOverlay 显示蓝色蒙版
  -> 敌人/敌方弹体/Boss/波次系统读取统一冻结状态并暂停
  -> 玩家与非敌对系统继续运行
  -> 倒计时归零后 FreezeStateChanged(false)
  -> 恢复刚体、动画、弹体动量并隐藏蒙版
```

经验与金币磁吸：

```text
PlayerMagnet 捕获 IMagneticPickup
  -> MagneticPickupMotion.StartFlyingTowards
  -> Scatter：向玩家外侧确定性飘散 0.18 秒
  -> Homing：从速度 5 开始持续加速追踪
  -> 原有 ExpGem / CoinPickup 碰撞逻辑完成奖励与回池
```

## 验证与验收

- 静态检查：通过；变更范围、Prefab/Scene 组件引用、Sprite `.meta` 与 `git diff --check` 已复核。
- 编译验证：通过；Unity 2022.3.62f3c1，`compileErrorDetected=false`。
- 最终独立 QA 门禁：`C:\Unity_Project\RainsenVampSur-QA\Logs\Automation\20260904-000839\summary.json`。
  - EditMode：101/101。
  - PlayMode：44/44。
  - failed/errors/inconclusive/skipped 均为 0，`passedQualityGate=true`。
  - 主项目与 QA 临时同步的 66 个文件 SHA-256 完全一致，测试后 QA 已恢复干净。
- 老大人工验收：地图拾取物、金币和经验的静态清晰度通过；六枚能力图标优化效果通过，本次素材优化确认完成。
- 性能验收：未完成。当前尚无目标规模敌群与弹幕，已转入正式待办，不影响本次静态画质验收结论。

## 当前边界

- 冻结只覆盖敌对模拟与敌对表现；不暂停玩家、玩家武器、经验金币磁吸、游戏权威计时，也不修改全局 `Time.timeScale`。
- 同类水晶球重复拾取不会累加时长，只会把剩余时间刷新到较大值。
- 舰长在满血时仍会消费世界道具，但因没有实际恢复生命，不写入成功拾取统计。
- 当前弱敌掉落率和 4:1 权重是首版策划值，尚未经过长局经济与稀有度平衡验证。
- 冻结蒙版为单层蓝色世界覆盖，没有倒计时 UI、专属音效或进入/结束动画。
- 高密度敌群、弹幕、掉落物与全屏蒙版同时出现时的 GPU Fill Rate、Overdraw、CPU 和 GC 尚未通过目标设备 Profiler。
- 1920×1080 为当前高清视觉基准；其他分辨率仍需关注 Canvas 缩放倍率和像素均匀性。

## 下一步 Todo

### MVP 回归

1. 后续修改敌人移动、Animator、远程攻击或对象池时，保留冻结与解冻状态恢复测试。
2. 后续修改 `PlayerMagnet`、经验或金币 Prefab 时，保留“先外飘、后追踪、复用重置”的 PlayMode 回归。
3. 后续增加地图即时效果时，继续使用无状态 `MapInstantEffectSO`，并只在效果真实成功后写入结果统计。

### 性能与画质

1. 当敌群、敌方弹幕和地面掉落达到目标密度后，执行 `hd-rendering-performance-validation.md` 中的 Profiler 验收。
2. 若性能不达标，优先优化对象池预热、批处理、Sprite/材质状态、查询频率、冻结状态传播与 Overdraw。
3. 不把降低素材像素或恢复低分辨率中间画布作为默认方案；任何画质回退都需要再次明确确认。

### 玩法与表现

1. 为地图即时拾取物补充拾取音效、短促闪光和水晶球冻结倒计时提示。
2. 在真实长局中验证 2.5% 总掉落率与舰长/水晶球 4:1 权重，再调整策划数值。
3. 继续扩展地图 X 变量：不同世界使用不同的即时拾取物池、掉落权重和环境机制。
