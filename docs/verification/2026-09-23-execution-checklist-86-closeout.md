# 执行清单 86 — Files/Document/Jobs/CodeGen/运维页面 closeout（2026-09-23）

**范围**：清单 §7.D 编号 **86**（上传/引用/回滚/取消/Apply 等 **真实流程** 的 real-stack 证据汇编 + 主导航可达性；逐页人工/axe 仍归 **87**）。

## 模块 → 深度 E2E（Phase B 已补强项加粗）

| 模块 | 关键流程 | real-stack 锚点 |
|------|----------|-----------------|
| **Files** | 目录/元数据/引用、批量上传删除、预览 | **`host-files.spec.mjs`**（B-31/32） |
| **Document** | 库/分类/标签/分享/权限/统计/回收站、**版本回滚** | `host-documents`、`document-*.spec.mjs`、**`host-document-version-rollback`**（B-47） |
| **Jobs** | 定义/计划/执行历史、**取消**、**批量暂停恢复** | `host-jobs.spec.mjs`、`host-jobs-b1-closeout`、`host-job-executions-cancel`（B-33）、`host-job-schedules-batch`（B-34） |
| **CodeGeneration** | 模板/预览/下载/Apply、**column-sync**、rollback-chain | `host-code-generation-*.spec.mjs`、**`host-code-generation-catalog-column-sync`**（B-36） |
| **运维观测** | 运行日志、服务器监控、缓存策略 | `host-observability-log-files`、`host-observability-server-monitor`（B-28）、`host-observability-cache-policies`（B-29） |

## 本槽交付

| 产物 | 说明 |
|------|------|
| `phase-d-86-ops-modules-nav.spec.mjs` | 上表模块 **15+2** 路由标题冒烟（含文档权限、执行历史直达） |
| [D 区审计 §86](2026-09-23-execution-checklist-d-zone-audit.md) | 完整 spec 清单 |

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`HostFileReference` + `CodeGenerationRollback` | **20/20** |
| real-stack | `phase-d-86-ops-modules-nav.spec.mjs` + 上表深度 spec |

**状态**：Build-verified；Verified 与 CI fresh 归 **87**。
