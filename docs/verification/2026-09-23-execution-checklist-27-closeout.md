# 执行清单 27 — 审计日志导出 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **27**（有界 XLSX 导出、敏感列授权、响应头元数据）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `POST …/access|operation|exception-logs/exports` |
| 服务 | `HostAuditLogExportService` |
| 权限 | `auditing.*.export`、`auditing.export.sensitive_fields` |
| Vue | `AuditLogExportDialog.vue`、`operation-logs-action-export` 等 |

## 本机验证

| 命令 | 结果 |
|------|------|
| 单元/集成 | 随 Auditing 导出边界套件 |
| real-stack | `host-operation-logs.spec.mjs` 增补清单 27 导出 API + Vue 导出对话框 |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
