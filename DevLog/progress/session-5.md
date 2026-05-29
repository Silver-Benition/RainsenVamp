# Session 5 · 工程环境整理专项

> **本次性质**：非代码开发会话，专项处理项目版本控制环境整理。
> **日期**：2026-05-29

---

## 一、本次对话的核心工作

| 类别 | 项目 | 状态 |
|---|---|---|
| 配置文件修改 | `ignore.conf` 补全规则（追加 `.claude/`、`.git/`、`.plastic/`、`.gitignore` 屏蔽项） | ✅ 已落盘 |
| 工程清理 | 删除项目根目录的 `.git/` 文件夹与 `.gitignore` 文件 | ✅ 已执行 |
| VCS 切换 | 从 "Git + Plastic 双 VCS 并存" → "Plastic 单一可信源" | ✅ 已完成 |
| Plastic checkin | 全部合法变更已提交至 Unity Cloud 中央仓库 | ✅ 已确认 |

> 本次未新增/修改任何 C# 类。

---

## 二、引入的新工程规范

1. **VCS 单一可信源原则**：项目仅由 Unity VCS（Plastic）管理，GitHub 仓库降级为"只读历史档案"。
2. **`ignore.conf` 三层分组规范**：标准 Unity 项 / Claude Code 个人数据 / VCS 元数据相互独立分组、附中文注释。
3. **`.claude/settings.local.json` 不跨设备同步原则**：后缀 `.local.json` 始终视为本机专属，新设备重新授权即可。
4. **Plastic Checkin 节奏**：每完成一个小功能（30 分钟 ~ 2 小时）即 checkin 一次。
5. **跨设备工作流标准化**：新设备首次同步流程沉淀（详见 retrospective 文档）。

---

## 三、推荐的下一步 Todo List

按优先级排列：

### 🔴 P0 · 验证类任务
- [ ] 在另一台设备上做一次"灾难演练"：拉取 Unity VCS 仓库 + 重建 Library + 启动 Unity，确认能正常打开
- [ ] 验证 DevLog 文件夹在新设备上完整可见

### 🟡 P1 · 工程优化类
- [ ] 在 `.claude/settings.json`（不带 .local）里建立一份**团队级共享 AI 规则**，预留未来扩展空间
- [ ] 建立 `DevLog/conventions/` 子目录，沉淀代码规范、命名约定、Prefab 组织规则等
- [ ] 解决"双 VCS 临时并存"隐患（详见 retrospective 文档第四节）

### 🟢 P2 · 回归核心开发主线
基于现有进度（截至 Session 4），以下任务等待主创指示开工：
- [ ] **对象池系统设计**：海量同屏敌人/子弹的核心性能基础设施（最高优先级）
- [ ] **武器升级 ScriptableObject 数据架构**：把 Session 4 的 FireBall.asset 模式扩展为完整体系
- [ ] **状态机框架**：Player / Enemy 共用的有限状态机基类，为未来动态地图切换打基础
- [ ] **多语言（i18n）底层接口**：CLAUDE.md 强调过的运营基础设施

---

## 四、跨设备首次同步标准流程（沉淀）

```
1. 安装 Unity Hub + Unity 2022.3 LTS（2D Core 模板）
2. 安装 Plastic SCM 客户端（或直接用 Unity Hub 自带的 VCS 集成）
3. 用 Plastic 客户端从 Unity Cloud 仓库拉取（Update workspace）
   → 自动得到：Assets / ProjectSettings / Packages / DevLog /
                CLAUDE.md / .cursorrules / ignore.conf
   → 不会得到：Library / .claude / .plastic / .git
4. 用 Unity Hub 打开项目根目录
   → Unity 会根据 Packages/manifest.json 自动重建 Library/（首次较慢，10-20分钟）
5. 启动 Claude Code，根据需要重新点几次"允许"授权 git/plastic 命令
6. 开发愉快
```
