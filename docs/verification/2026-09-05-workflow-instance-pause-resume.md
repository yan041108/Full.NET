# Workflow 实例暂停、恢复与强制恢复验证记录

## 结论

- 状态：`Build-verified`，不提升为 `Verified`。
- 已交付 `POST /api/v1/workflow/instances/{instanceId}/pause|resume|recover`，operationId 分别为 `workflowPauseInstance`、`workflowResumeInstance`、`workflowRecoverInstance`。
- 暂停只把实例切到 `suspended` 并释放租约，步骤/待办保留原 Id；普通恢复与强制恢复只把实例切回 `active`，禁止新建步骤、待办、抄送或通知。
- 强制恢复继续使用 `workflow.instances.recover`，必须填写规范化后非空原因；普通暂停/恢复使用独立权限码 `workflow.instances.pause` / `workflow.instances.resume`。
- 迁移 `109_WorkflowSuspendedInstanceOccupancy` 把业务唯一占用扩展到 `active|suspended`。编号 106 已被通知公告生命周期占用，因此本切片使用下一可用编号 109；102 恢复测试上界未改。

## 并发与幂等

- 请求必须携带实例 `ExpectedRevision` 与 1–128 字符幂等键。
- 实例修订更新受影响行数必须为 1，否则 `ExecuteResultAsync` 回滚回执、轨迹和审计。
- 同一操作人、幂等键和规范摘要可重放；同键不同摘要返回 `workflow.revision.conflict`。
- 暂停后办理/改派返回 `workflow.transition.invalid`；终态返回 `workflow.instance.terminal`。暂停实例可取消。

## TDD 与本地证据

- Unit：`pnpm test:dotnet:unit` Release，1868 发现 / 1867 通过 / 1 个 Windows 上 Inconclusive（Linux FIFO 用例）；`unit.minimum` 1519 → 1536。
- Architecture：全量 202/202；`api-native-aot` 选择集 73/73。
- Vue：`WorkflowInstancesView.test.ts` + `workflow-instances.test.ts` 10/10；`pnpm --filter @fullnet/admin typecheck` 与 production `build` 通过。
- 命名：`pnpm test:naming` 30/30（109 成对脚本的 PREPARE / 条件 DROP 已精确登记命名债务）。
- OpenAPI：离线 `pnpm test:openapi` 124/124；客户端生成清单 292 个 Operation；快照与 `pnpm openapi:client:generate` 零漂移。
- AOT：`pnpm test:aot:analyzers` 退出码 0（分析构建 2 个既有风格警告、0 错误）。IntegrationTests Release 编译通过。
- `git diff --check` 无空白错误。

## 延后验证

- 页面级 Playwright / 真实栈 E2E。
- SQL Server/MySQL Workflow API 真实库执行（断言代码已加入 `WorkflowRuntimeApiAssertions`，本地不跑双库套件）。
- Linux Native AOT publish / 原生进程 E2E。
- 视觉微调、人工逐页验收、容量/故障演练、`Verified` 升级。
- Recovery Worker、超时、退回、转办 Vue、加签、网关与 Notifications 新事件不属于本编号。

## 规则与 Skill 演进

未命中用户纠正、重复失败、高风险新类别或 Skill 缺口，不更新规则或 Skill 候选。
