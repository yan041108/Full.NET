# DataApproval 首个纵向切片验证记录

**日期**: 2026-09-05  
**基线**: `3687b04fc77d962a6f56cb7ab0e981d0aef20412`  
**分支**: `feat/data-approval-first-slice`  
**状态**: `Build-verified`；双库 Integration、页面真实栈 E2E 与 Linux Native AOT 以目标提交 GitHub Actions 为最终门禁。

## 范围

Host 流水号规则 UPDATE 通过 DataApproval 发起工作流审批，审批通过后由 SerialNumbers 应用变更。

- 场景键: `serial_numbers.host_rule.update`
- 工作流 BusinessType: `data_approval.serial_rule.update`
- DataApproval 不读取业务表，仅持久化快照与工作流关联

## 已交付

| 区域 | 说明 |
| --- | --- |
| Workflow 跨模块端口 | `IWorkflowPublishedDefinitionDirectory` / `IWorkflowInstanceStarter` / `IWorkflowInstanceCanceller` 适配器 |
| SerialNumbers 桥接 | `ISerialRuleChangeApprovalSource` / `ISerialRuleChangeApprovalApplier` |
| DataApproval 模块 | 表 `fn_data_approval_request`、CRUD+cancel API、工作流终态事件处理器 |
| 迁移 | `118_DataApprovalRequest.sql`（SqlServer + MySql） |
| OpenAPI | `data-approvals-v1.json`、客户端生成、契约测试 |
| Vue 管理端 | `DataApprovalRequestsView.vue` + 路由/导航/i18n |
| 单元测试 | 状态机、场景校验、Workflow 端口注册、快照序列化 |

## 本地验证

```powershell
dotnet build src/Modules/Full.NET.Modules.DataApproval/Full.NET.Modules.DataApproval.csproj -c Release
dotnet build src/Modules/Full.NET.Modules.Workflow/Full.NET.Modules.Workflow.csproj -c Release
dotnet build src/Modules/Full.NET.Modules.SerialNumbers/Full.NET.Modules.SerialNumbers.csproj -c Release
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~DataApproval|FullyQualifiedName~WorkflowCrossModule|FullyQualifiedName~SerialRuleApproval"
node --test tests/openapi/data-approvals-contract.test.mjs
pnpm --filter @fullnet/admin vitest run src/views/DataApprovalRequestsView.test.ts
```

## Deferred

- 双库 Integration 测试与迁移恢复测试（118）
- 角色权限种子迁移（非 super-admin 角色授权）
- 真实栈 E2E 与页面级人工验收
- SerialNumbers 直接 UPDATE 端点改为强制走 DataApproval（本切片仅建立并行能力）
- `ISerialRuleChangeApprovalApplier` 进程内静态幂等字典需替换为持久化幂等（多实例/重启）

## 规则演进

未触发 `rules/rule-evolution.md` 升级条件。
