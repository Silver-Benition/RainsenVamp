# Session 5 · 复盘与隐患记录

> **本次性质**：工程环境整理专项复盘
> **日期**：2026-05-29
> **关键决策**：从"Git + Plastic 双 VCS 并存"切换至"Plastic 单一可信源"

---

## 一、工程教训

| 教训点 | 价值 |
|---|---|
| 🚨 双 VCS 并存是隐性陷阱 | 容易导致两套历史不一致，必须早发现早收敛 |
| 🚨 改动 ignore 规则的顺序至关重要 | **必须先改 ignore，再 checkin**。反过来会污染仓库 |
| 🚨 Unity VCS（Plastic）≠ Git | Checkin 即推送，不存在"本地 commit + 远端 push"两阶段，理解差异才能正确判断同步状态 |
| 💡 知识库 = 项目内的普通文件 | DevLog/CLAUDE.md 跨设备同步的本质是文件同步，与 VCS 选型无关 |
| 💡 `.local.json` 后缀语义化 | 凡是带 `.local` 的配置文件都是本机专属，跨设备同步反而出问题 |

---

## 二、关键认知差异沉淀

### Plastic 与 Git 的工作模式对比

| 系统 | Commit 行为 | 是否需要单独 Push |
|---|---|---|
| **Git** | 本地 commit → 远端 push（两步） | ✅ 必须 |
| **Unity VCS（默认）** | Checkin = 直接提交到云端中央仓库（一步） | ❌ 不需要 |

**判断 Plastic 同步成功的标志**：
- ✅ 点了 "Checkin" 按钮
- ✅ Pending changes 列表变空
- → 即视为已在 Unity Cloud 留档

**进一步验证（可选）**：打开 Plastic 客户端 → "Branch Explorer" 或 "Changesets"，能看到带编号（如 `cs:42`）的最新记录。

---

## 三、本次确立的工程规范（5 条）

1. **VCS 单一可信源原则** ── 项目仅由 Unity VCS（Plastic）管理，GitHub 降级为只读历史档案。
2. **`ignore.conf` 三层分组规范** ── 标准 Unity 项 / Claude Code 个人数据 / VCS 元数据，相互独立分组、附中文注释。
3. **`.claude/settings.local.json` 不跨设备同步** ── 个人本机授权，新设备重新点允许即可。
4. **Plastic Checkin 节奏** ── 每完成一个小功能（30 分钟 ~ 2 小时）即 checkin 一次，写清晰中文注释。
5. **DevLog 同步原则** ── 每次 `/save` 总结产生的进度文档，必须 checkin 进 Plastic，否则跨设备会丢。

---

## 四、⚠️ 当前遗留隐患记录（待处理）

> **背景**：本次会话末期，雨弦因时间紧迫选择"先同步本次总结到 GitHub"，与本会话刚刚确立的"VCS 单一可信源"原则产生临时冲突。本节诚实记录该隐患，待回头处理。

### 隐患 4-1：双 VCS 临时并存复活

| 项 | 当前状态 |
|---|---|
| Plastic（主同步通道） | ✅ 正常工作，已是单一可信源 |
| 本地 `.git/` | ⚠️ **将被重新创建**（用于 push 本次总结至 GitHub） |
| GitHub 远端 | ⚠️ **将更新**（违背"只读档案"原则） |
| `.gitignore` | ⚠️ **将被重新创建** |
| `ignore.conf` 中对 `.git/` 的忽略 | ✅ 已有规则（不会污染 Plastic） |

### 隐患 4-2：潜在的双向同步混乱风险

未来如果在多台设备上开发，且某台设备只 checkin 到 Plastic 没 push 到 GitHub，会导致：
- GitHub 上看到的是某个旧版本
- 不同设备之间 `git pull` / `git push` 可能与 Plastic 状态不一致
- 复杂场景下出现"以哪边为准"的纠结

### 处理建议（待办，优先级 P1）

下次有完整时间块时，需要做以下任意一种处理：

**方案 A · 彻底清理 Git，回归"VCS 单一可信源"**
1. 在 GitHub 上把仓库标记为 "Archived"（只读）
2. 删除本地 `.git/` 和 `.gitignore`
3. 未来如需 GitHub 备份，定期手动从 Plastic 工作区另起临时 Git 仓库导出（不放在项目根目录）

**方案 B · 正式接受双 VCS 并存，建立同步纪律**
1. 制定明确规则：每次 Plastic checkin 后，必须配套 `git add . && git commit && git push`
2. 在 `CLAUDE.md` 里写入这条工程纪律
3. 接受额外的操作成本作为"双重保险"的代价

**雨弦的倾向**：未表态，需后续讨论决定。

---

## 五、新设备首次同步流程（标准化沉淀）

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

---

## 六、本次 retrospective 的元教训

> 本次复盘文档本身的产生过程，也暴露出一个流程问题：
>
> 当 AI（Claude）输出"建议归档至 XXX 路径"的提示时，主创容易误以为 AI 已完成落盘。
> **后续约定**：Claude 输出归档建议时，必须主动询问"是否需要我直接落盘"，避免认知错位。

