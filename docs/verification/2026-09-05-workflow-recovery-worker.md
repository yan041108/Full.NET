# Workflow Recovery Worker 验证记录

## 结论

- 状态：`Build-verified`，不提升为 `Verified`。
- 已交付 Worker 扫描卡住实例、未完成人工步骤和过期租约，写入 `fn_workflow_recovery_task`，并以 `LeaseOwnerKey + LeaseExpiresAtUtc + LeaseGeneration` 领取、续租、过期回收。
- 失败按有界退避重试；达到 `MaxAttempts` 后任务进入 `dead_lettered` 并暂停仍活动的实例。Jobs 不持有 Workflow SQL。
- 管理 API：`GET/POST /api/v1/workflow/recovery-tasks`，operationId 为 `workflowListRecoveryTasks`、`workflowGetRecoveryTask`、`workflowRetryRecoveryTask`、`workflowReconcileRecoveryTask`。
- 精确权限：`workflow.recovery_tasks.read` / `retry` / `reconcile`。Vue 恢复任务页无权限不创建入口。
- HostedService 只在 Worker `AddBackgroundServices` 注册；API `AddServices` 不启动领取循环。
- 迁移 `110_WorkflowRecoveryTask` 新增恢复任务表与未关闭占用唯一键。

## 并发与幂等

- Worker 领取：SQL Server `UPDLOCK READPAST`；MySQL `FOR UPDATE SKIP LOCKED`。完成/续租必须匹配本轮 `LeaseOwnerKey` 与 `LeaseGeneration`。
- 人工重试/对账必须携带任务 `ExpectedRevision` 与 1–128 字符幂等键；重试原因规范化后不得为空。
- 对账只在源条件已消失（终态/已暂停/活动待办已补齐）时关闭任务；仍卡住返回 `workflow.recovery.reconcile_invalid`。
- 管理查询必须使用可信 `TenantScopeKey`；Worker 扫描是全局的。

## TDD 与本地证据

- 基线：`de3f3745dde90ca9b5487a941d1e201c1c55ee27`，分支 `feat/workflow-recovery-worker`。
- `dotnet build` Workflow / Host.Api / UnitTests / ArchitectureTests / IntegrationTests：Release 0 警告 0 错误。
- `pnpm test:dotnet:unit -- --no-build`：1883 通过，1 个既有 Linux FIFO inconclusive，0 失败；矩阵 `unit.minimum` 1536 → 1552。
- `pnpm test:dotnet:architecture -- --no-build`：204 通过；矩阵 architecture 137 → 139。
- `pnpm test:naming`：30 通过。SQL Server 110 计算列 CONCAT 必须统一 `Latin1_General_100_BIN2`，否则迁移在默认库排序规则下失败。
- `pnpm test:aot:analyzers` 与 `pnpm test:aot:worker:analyzers`：0 警告 0 错误。
- `pnpm openapi:client:snapshot --update --no-build`：SQL Server/MySQL OpenAPI 文档测试通过并更新快照；清单 292 → 296 个 Operation。
- `pnpm openapi:client:generate` 后 `pnpm test:openapi`：124 通过。
- Vue：`vitest` 恢复任务 API/页面/导航 8 通过；`pnpm --filter @fullnet/admin typecheck` 通过；`@fullnet/client-contracts` 163 通过。
- `git diff --check`：无空白错误。

## 延后验证

- 页面级 Playwright / 真实栈 E2E。
- SQL Server/MySQL Workflow API 与 110 迁移恢复的真实库执行（断言代码已加入，本地不跑双库套件）。
- Linux Native AOT publish / 原生进程 E2E（本编号同时改 Host.Api JSON/SQL 与 Worker HostedService，inner 跑分析器，不跑 Linux publish）。
- 视觉微调、人工逐页验收、容量/故障演练、`Verified` 升级。
- 超时催办、退回、转办、加签、会签或签、角色办理人、并行/包容网关、邮箱验证、回执、模板多语言、DataApproval 不属于本编号。

## 规则与 Skill 演进

未命中用户纠正、重复失败、高风险新类别或 Skill 缺口，不更新规则或 Skill 候选。
