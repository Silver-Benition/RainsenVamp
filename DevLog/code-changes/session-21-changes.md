# Session 21 代码变更归档：主世界性能设施、字体修复与诊断

- 日期：2026-09-11；基线 `169c2cf`；实施提交 `bf10ad5`、`d8a3b93`、`d8f3aee`。
- 本文件随同编号归档提交。结果、硬件、证据路径和最终关闭决定以 [进度归档](../progress/session-21.md) 为准。

## 逻辑架构

```text
生成器 -> 隔离场景 / Profile / 角色 / 敌人 / 波次 / 掉落表
                       |
MainWorldPerformanceRunner 阶段状态机
  -> 正式 WorldWaveManager -> WorldEnemySimulation -> PoolManager
  -> PerformanceSampler / ProfilerRecorder -> 阶段与原始帧报告
  -> 连续帧缓冲 / 阶段事件 / 受限 CPU 捕获 -> 离线诊断
```

ScriptableObject 驱动测试配置；敌人、弹体、掉落沿用对象池和正式业务逻辑；生成资产承担测试覆盖，表现层与伤害/统计权威保持分离。只在测试场景禁用副世界，不修改生产副世界实现。

## C# 类与核心方法

| 文件 / 类 | 核心方法与变化 |
|---|---|
| `Assets/Scripts/Core/Diagnostics/MainWorldPerformanceProfile.cs` | Profile、运行/事件/阶段枚举、帧与阶段报告、计数器支持状态、诊断事件；`MainWorldPerformanceReportEvaluator` 负责容量与对照有效性判断，区分完整、负载不足、中断、污染和缺失证据。 |
| `Assets/Scripts/Core/Diagnostics/MainWorldPerformanceRunner.cs` | `RunStateMachine`、`RunCapacitySweepMode`、`RunInstrumentationComparison`、`RunPickupBurstMode`、`RunFreezeMode` 组织真实压力流程；`ApplyEventMode`、`EnsureFullLoadout`、`ApplyWaveTarget` 控制测试副本；`RunTimedStage`、`FlushCurrentStageReport`、`FinalizeReport` 输出证据并清理覆盖。 |
| 同上，短程诊断 | `RunLongFrameDiagnostic`、`StartDiagnosticCapture`、`StopDiagnosticCapture`、`AddDiagnosticEvent`、`WriteDiagnosticTrace`：支持原阶梯前缀、两段满武器观察、阶段标记及受限 CPU 捕获。 |
| `Assets/Scripts/Core/Diagnostics/PerformanceSampler.cs` | `BeginStage`、`RecordFrame`、`EndStage`、`SetInstrumentationEnabled`、`CreateRawSamplesJson`：预分配帧存储、原生 Recorder 生命周期、低频实体扫描、实际计数与估计分离；关闭采样器额外设施时真正释放 Recorder，重新开启时探测支持性。 |
| 同文件 `MainWorldPerformanceDiagnosticTrace` | `Record`、`WriteCsv`：启动时预分配 1,048,576 帧缓冲，记录 Unity 帧号、实时时间、阶段、活动敌人和 GC 累计次数；溢出明确计数，包含阶段统计排除的边界帧。 |
| `Assets/Editor/MainWorldPerformanceBuild.cs` | `GenerateTestAssets`、`BuildWindowsDevelopment`：生成隔离副本、构建 Development Player 并写入源码身份；`AnalyzeDiagnosticCapture`、`WriteDiagnosticSamples` 批处理解析 CPU 文件。 |
| `Assets/Scripts/Core/AccountProgressService.cs` | `SetStorageForTests` 的编译条件扩展为 `UNITY_EDITOR || DEVELOPMENT_BUILD`，允许专用性能 Player 注入内存账号存储；Release 不包含该入口。 |

离线解析使用 Unity 2022.3 的 ProfilerDriver / RawFrameDataView，并通过反射设置仅当前分析进程的 `SetMaxFrameHistoryLength(65536)`。方法缺失或捕获前缀截断即失败，不写 EditorPrefs；该工具内部接口在当前 Unity 版本实际编译、解析验证过，引擎升级后须重新验证。修正前只见末尾 300 帧的解析结果未用于最终结论。

测试角色具有高生命和延后升级阈值，测试波次补怪倍率为 4。Controlled 仅修改显式绑定的生成掉落表：宝箱概率清零、冻结权重清零，保留 Captain、经验和金币；缺表失败，结束恢复。Manual 强制 Natural、正常成长/输入、60 FPS、不自动退出。清理时解除追踪引用，不对旧对象池引用强行释放。

## 资产与工具

- `Assets/Tests/Performance/Generated/`：独立场景、Profile、容量角色、延后 Boss、主世界波次/世界线、弱敌与远程敌人的数据/Prefab、远程攻击与弱敌掉落表。所有 `.meta` 随交付保留。
- `Tools/Run-MainWorldPerformance.ps1`：Build / Run / All，capacity / normal / pickup / freeze / diagnostic；事件模式、档位、拾取数量、重复次数、时间倍率、种子、`DiagnosticPrefix` 和 `CaptureCpu`。运行结果同时写脚本摘要并检查退出状态。
- 性能构建使用 `MAINWORLD_PERFORMANCE_BUILD`；真实性能运行必须使用图形设备，不能拿 NoGraphics 编译/测试当作性能结果。没有改全局 Build Settings 或 ProjectSettings。
- 正式 `Assets/Scenes/MainLevel.unity` 与生成场景：宝箱 Toast 的字体引用从 `msyhl.ttc`（原始字体，类型错误）改为已有 `Assets/Fonts/msyh SDF.asset`。正式场景只有该引用变化，UI C# 与字体资产没有变更。

## 自动化覆盖

- `MainWorldPerformanceTests`：生成资产隔离、Profile / 负载判定、采样对照有效性、缺失计数、连续诊断记录等。
- `MainWorldPerformancePlayModeTests`：正式生成链路、冻结/恢复、测试覆盖生命周期及采样器开关等。
- `TreasureChestPresentationTests`：新增正式字体类型、正确资产路径、图集、材质及 `_MainTex` 对应检查。
- 新增 `TreasureChestPresentationPlayModeTests`：激活真实 Toast、触发奖励观察入口、强制文字网格更新，检查显示状态、字体、字符/材质生成和无异常日志。沿用现有反射测试边界，未改变程序集配置。
- QA 最终全量结果 112 个 EditMode、47 个 PlayMode 全通过；主分支集成门禁及其新报告路径见进度归档。

## Unity 等价配置步骤

这些配置已保存，本轮归档无需再次生成或手工操作。

1. 日常打开 `C:/Unity_Project/RainsenVampSur-QA`，使用 Unity 2022.3.62f3c1。需要重建性能资产时执行菜单 `RainsenVampSur/Performance/Generate Main World Test Assets`；输出限定在生成目录。
2. 打开生成的 `MainWorldPerformance.unity`，确认 Runner 绑定生成 Profile、测试角色/波次/掉落引用；`SubWorldRuntime` 场景对象保持未激活，避免 Awake 已启动副世界。
3. 正式 `MainLevel` 中定位 `TreasureChestRewardToastUI`，Inspector 的 Font 指向 `Assets/Fonts/msyh SDF.asset`，不能拖入原始 TTC 字体。生成测试场景使用相同 SDF 引用。
4. 如未来明确需要重跑图形性能测试，可使用构建菜单 `RainsenVampSur/Performance/Build Windows Development Player` 或现有启动脚本。不要修改正式场景波次数量来造负载；本轮不再启动新压力测试。
5. 未新增正式按键、Layer、Collider 或手工组件挂载要求；正式输入、敌人碰撞和对象池协议沿用原配置。

## 范围结束

主世界约定负载验收通过、字体引用修复完成。2026-09-11 老大决定将未复现/未完整捕获的尖峰从当前工作关闭；保留原始数据但不列为后续必做项。副世界和其他硬件没有被本次结论覆盖。
