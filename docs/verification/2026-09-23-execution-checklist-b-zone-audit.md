# 执行清单 B 区（19–47）源码审计（2026-09-23）

**目的**：全量排期 Phase B 的串行槽位以 **核验 + 补证据** 为主；下列「已有」指 Host.Api + `ui/admin` 可见实现，**不等同 Verified**。

| 编号 | 判定 | 主要锚点 |
|------|------|----------|
| 19 | 已有 | `identityCopyHostRole`、`RolesView` copy |
| 20 | 已有 | enable/disable/delete、members API + `RolesView` |
| 21 | 已有 | `tenancy.tenants.enable`、租户生命周期 Endpoint |
| 22 | closeout | `TenantBranding` + `host-tenant-branding.spec.mjs` |
| 24 | closeout（职位） | 职位 import/template；机构批量仍独立子切片 |
| 25 | closeout | `EnumCatalogsView` + dict-generation API |
| 26 | closeout | 趋势 + domain-change-diffs；`host-operation-logs` |
| 27 | closeout | 日志 XLSX 导出；`AuditLogExportDialog` |
| 28 | closeout | `server-instances` + `ObservabilityServerMonitorView`；运行日志见 `host-observability-log-files` |
| 29 | closeout | `cache-policies` API + `ObservabilityCachePoliciesView` |
| 30 | closeout | `revoke-all` + `session-policy` + `OnlineSessionsView` |
| 31 | closeout | 虚拟目录 + 元数据/引用；批量上传删除另切片 |
| 32 | closeout | `batch-upload` / `batch-delete` + 预览；`host-files.spec.mjs` |
| 33 | closeout | `HostJobExecutionCancelService` + 执行历史取消 UI |
| 34 | closeout | `batch-pause` / `batch-resume` + 调度页批量 UI |
| 35 | closeout | `my-personal-schedules` API + `PersonalSchedulesView` |
| 36 | closeout | `column-sync` API + `CodeGenerationPreviewsView` 增量合并；`host-code-generation-catalog-column-sync.spec.mjs` |
| 37 | closeout | Host/My release-notes API + 管理页；`host-release-notes.spec.mjs` |
| 38 | closeout | 行政区域 CRUD/导入/级联；`host-administrative-regions.spec.mjs` |
| 39 | closeout | 接入方 CRUD/轮换/停用；`host-open-access-clients.spec.mjs` |
| 40 | closeout | 访问日志/用量/签名调试；`host-open-access-clients-observability.spec.mjs` |
| 41 | closeout | `host-dashboard-summary` 趋势/待办聚合；`host-dashboard-overview.spec.mjs` |
| 42 | closeout | 收件/已读/阅读统计；`host-announcements-receipts.spec.mjs` |
| 43 | closeout | Intent 附件协调器 + Files 引用；`notification-intent-attachments.spec.mjs`（无厂商 SMTP） |
| 44 | closeout | SMTP `ReceiptModeKey=none` + Webhook fail-closed + 渠道 UI；`notification-smtp-receipt-capability.spec.mjs`（无邮件厂商验签） |
| 45 | closeout | `sms.aliyun` 模板/验证码/验签回执 + 投递 UI；`notification-aliyun-sms-capability.spec.mjs`（无真实 dysmsapi） |
| 46 | closeout | `serial_numbers.host_rule.disable` Source/Applier + 策略 UI；`data-approval-serial-rule-disable.spec.mjs` |
| 47 | closeout | Host 文档版本指针回滚 + 权限 146；`host-document-version-rollback.spec.mjs` |

**Phase B 串行纪律**：仍按计划 B-1…B-28 顺序，每编号一次 **closeout 提交**（测试/文档/OpenAPI），无源码缺口则仅更新 verification，不重复实现。
