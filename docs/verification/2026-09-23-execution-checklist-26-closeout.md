# 执行清单 26 — 审计趋势与变更差异 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **26**（时间范围聚合、域审计变更差异脱敏只读查询）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `…/access|operation|exception-logs/trends`、`GET /api/v1/auditing/domain-change-diffs` |
| 服务 | `HostAuditLogTrendQueryService`、`DomainAuditChangeDiffQueryService` |
| 权限 | `auditing.trends.read`、`auditing.change_diff.read` |
| Vue | `AuditLogTrendPanel.vue`、`AuditLogDetailDrawer` 变更差异页签 |

## 本机验证

| 命令 | 结果 |
|------|------|
| 单元/集成 | 随 Auditing 模块套件 |
| real-stack | `host-operation-logs.spec.mjs` 增补清单 26 趋势 API + Vue 趋势标题 |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
