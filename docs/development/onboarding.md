# Full.NET 人类阅读入口（Onboarding）

面向新加入的开发者与审查者。AI 代理仍以根目录 [`AGENTS.md`](../../AGENTS.md) 与 [`rules/`](../../rules/README.md) 为准；本文只提供**最短阅读路径**与文档权威关系，不复制强制规则正文。

## 1. 五分钟定位

| 问题 | 答案入口 |
|---|---|
| 现在到底能用到什么？ | [`docs/roadmap/capability-status.md`](../roadmap/capability-status.md)（唯一能力总览） |
| 和 Admin.NET 差多远？ | [`docs/roadmap/adminnet-feature-parity.md`](../roadmap/adminnet-feature-parity.md)（长期对标，≠ 已交付） |
| 怎么在本机跑起来？ | [`getting-started.md`](getting-started.md) |
| 架构为什么是模块化单体？ | [`ADR-0002`](../architecture/adr/ADR-0002-modular-monolith-evolution.md) |
| 写代码前读哪些规则？ | [`rules/README.md`](../../rules/README.md) → 至少 `development-quality`；动库/API/机器码再加 `naming-conventions`；动前端再加 `client-frontend` |
| 新增模块怎么交付？ | [`.agents/skills/fullnet-module-delivery`](../../.agents/skills/fullnet-module-delivery/SKILL.md) |

当前阶段一句话：**M2 安全与基础设施底座已可用；完整后台 CRUD 仍在路线图。** 禁止把路线图上的 `Mapped`/`Planned` 说成已交付。

## 2. 文档权威分层（防漂移）

| 层级 | 目录 | 怎么读 |
|---|---|---|
| 强制规则 | `rules/*.md`、`AGENTS.md` | **当前必须遵守**；与实现冲突时不得假装已合规 |
| 重大决策 | `docs/architecture/adr/` | 单项取舍与后果；冲突时先对齐 ADR |
| 已批准设计 | `docs/superpowers/specs/` | 长期基线与验收条件 |
| 实施步骤 | `docs/superpowers/plans/` | 可执行任务；勾选 ≠ 已验证 |
| 事实与审查 | `docs/verification/` | 带日期/基线的评估与测试证据；建议不自动改架构 |

`rules/` 与某份历史 `specs/` 若表述重叠：**以 `rules/` 与最新已批准 Spec/ADR 为准**，历史 Spec 是设计过程，不是第二套强制源。

详见 [`ADR-0001`](../architecture/adr/ADR-0001-document-artifact-governance.md) 与 `development-quality` §12.1。

## 3. 建议阅读顺序（首日）

1. 本文 + [`capability-status.md`](../roadmap/capability-status.md) 第 2–4 节。
2. [`getting-started.md`](getting-started.md)：构建、分层测试、AppHost、双库切换。
3. [`ADR-0002`](../architecture/adr/ADR-0002-modular-monolith-evolution.md) + 架构 Spec 中与你工作相关的章节（数据/安全/Outbox）。
4. 若改 Identity/会话：会话基础 Spec + 超级管理员 Spec。
5. 若改前端：[`client-frontend.md`](../../rules/client-frontend.md) + [`client-delivery-roadmap.md`](../roadmap/client-delivery-roadmap.md)。
6. 动手前打开对应 `plans/`，按任务做，不要从零发明目录结构。

## 4. 仓库地图（按需下钻）

```text
src/BuildingBlocks/   可复用基础设施（数据、缓存、宿主、模块性…）
src/Modules/          业务模块（当前主要 Identity、Tenancy）
src/Composition/      宿主模块目录与 Profile（唯一权威清单）
src/Hosts/            Api / Worker / Migrator / AppHost
tests/                Unit / Compatibility / Architecture / Integration / e2e
ui/admin              Vue 主管理端
ui/admin-layui        Layui 对等管理端
clients/uniapp        多端基础（业务页仍少）
docs/roadmap/         能力与对标总览
docs/verification/    审查与验证事实
```

## 5. 近期优先工作（会变，以矩阵为准）

以 [`capability-status.md` §4](../roadmap/capability-status.md) 为准。2026-07-22 架构巡检后：

- P0 先移出 E2E Seed 发布物并恢复 Layui 客户端聚合门禁（硬化计划 Task 3A～3B）
- P1 随后关闭跨模块实现依赖、API 迁移能力、Migrator 完整 HTTP 装配和空健康检查（硬化计划 Task 4A～4D）
- Identity **用户管理**为已批准的首个业务纵向切片（见[计划](../superpowers/plans/2026-07-21-identity-user-management-vertical-slice.md)）
- Vue / Layui **长期并行**（后台模块必须双端同步）
- Outbox 死信 / 多 Worker 验证（硬化计划 Task 6）与 PR 集成冒烟加宽仍为近期工程项

## 6. 常见误读

| 误读 | 纠正 |
|---|---|
| “有状态矩阵行 = 生产可用” | 只有 `Verified` 且证据齐全才接近发布表述 |
| “有测试文件 = Build-verified” | 必须有可定位的新鲜通过记录 |
| “Integration 在 PR 绿了 = 双库全矩阵绿了” | PR 默认只跑冒烟；全量在 `main`/发布档 |
| “Vue 做完 = 管理端完成” | 必须 Vue + Layui 同模块同步（除非所有者改定规则） |
| “specs 日期更新 = 规则已改” | 规则变更必须进 `rules/` 并接受审查 |

## 7. 下一步

本地验证命令与双库说明 → [`getting-started.md`](getting-started.md)  
外部分析吸收背景 → [`external-review-2026-07-21.md`](../verification/external-review-2026-07-21.md)
