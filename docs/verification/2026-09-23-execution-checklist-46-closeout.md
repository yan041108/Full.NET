# 执行清单 46 — DataApproval 第二场景（流水号禁用）closeout（2026-09-23）

**范围**：清单 §7.B 编号 **46**（第二个真实场景的 Source/Applier 与策略接线；对应业务表单提交审批；非万能 JSON 审批器）。

## 第二场景定义

| 项 | 值 |
|----|-----|
| 场景键 | `serial_numbers.host_rule.disable` |
| 业务域 | Host 流水号规则 **禁用**（第一场景为 `serial_numbers.host_rule.update`） |
| Workflow BusinessType | `data_approval.serial_rule.disable` |
| 业务模块 | `Full.NET.Modules.SerialNumbers` |

## 交付锚点

| 层 | 位置 |
|----|------|
| 目录 | `DataApprovalScenarioCatalog`（双场景登记） |
| Source | `ISerialRuleChangeApprovalSource` / `SerialRuleDisableApprovalService`（快照 + `IDataApprovalSubmissionPort`） |
| Applier | `ISerialRuleDisableApprovalApplier` / `SerialRuleDisableApprovalApplier` → `HostSerialRuleService.ApplyApprovedDisableAsync` |
| 编排应用 | `DataApprovalRequestApplicationService.TryApplyApprovedChangeAsync`（disable 分支） |
| 工作流终态 | `DataApprovalWorkflowOutcomeService`（`SerialRuleDisable` 业务类型） |
| API | `POST …/disable-approval-preview`、`…/disable-approval-requests` |
| 权限 | `serial_numbers.rules.submit_disable_approval`（迁移 `145` 恢复） |
| Vue | `SerialNumberRulesView`（提交禁用审批 vs 直接禁用）；`DataApprovalScenariosView`（「流水号规则禁用」）；`DataApprovalRequestsView`（场景标签） |
| 集成 | `DataApprovalApiAssertions`（目录 ≥2 场景）；`NotificationsApi*` 同夹具外独立 `DataApprovalApiSqlServerTests` 路径见 Host 纵向切片 |
| 第一场景文档 | [2026-09-05-data-approval-first-slice](2026-09-05-data-approval-first-slice.md)（update） |

## 核验结论

- **已有**：禁用与更新共用 DataApproval 请求/工作流/应用管线，但 **独立场景键、快照语义与 Applier**；策略启用时 UI 隐藏直接禁用按钮。
- **本槽**：新增 real-stack `data-approval-serial-rule-disable.spec.mjs`（API 预览+提交 + Vue 禁用审批跳转请求详情）；场景页已有 `data-approval-scenarios.spec.mjs` 双场景文案。
- **未验**：第二个 **非 SerialNumbers** 业务域场景（清单要求由真实业务需求选定，不在本槽虚构）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`DataApprovalScenarioCatalog` + `SerialRuleDisable` + `Serial_rule_disable_business` | **6/6** |
| `pnpm exec vitest run` …`SerialNumberRulesView.test.ts` + `DataApprovalScenariosView.test.ts` | **13/13** |
| real-stack | `data-approval-serial-rule-disable.spec.mjs`、`data-approval-scenarios.spec.mjs` |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 与 Wave3 WF+DA 门禁约束。
