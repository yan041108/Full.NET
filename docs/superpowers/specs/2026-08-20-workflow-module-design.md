# Workflow 模块设计规格

**状态：** Spec drafted — **pending review**（审查通过前禁止创建 `.csproj`、迁移或模块代码）  
**日期：** 2026-08-20  
**基线：** `main@e008f64e`  
**适用范围：** 计划中的 `Full.NET.Modules.Workflow`（单主项目垂直切片）、Host/租户流程、Vue 管理端、SQL Server/MySQL  
**Admin.NET 映射：** 对标 `Admin.NET.Plugin.WorkFlow` 的定义、发布、实例、审批、待办、抄送与业务联动语义；**不复制** 动态类型扫描、SqlSugar 或跨模块直连表结构  
**首切片计划：** [`2026-08-20-workflow-first-vertical-slice.md`](../plans/2026-08-20-workflow-first-vertical-slice.md)

## 1. 决策摘要

Workflow 拥有流程定义、不可变定义版本、运行实例、步骤、待办、抄送、执行日志与恢复控制面。业务模块只能通过 **Contracts / 最小 Port / 版本化 Integration Event** 启动流程、查询投影状态或接收完成/拒绝结果；**禁止** JOIN、外键或读写 `fn_workflow_*` 以外模块表。

跨模块推进与副作用必须走事务 Outbox + 幂等 Inbox（或等价消费语义）与补偿；禁止依赖跨模块本地事务。执行器必须可在 Worker 多实例下租约恢复，禁止把进程内内存当作权威进度。

本规格审查通过后，能力状态可由 `Mapped` 进入 `Designing`/`Planned`；实现须遵守 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)、[`ADR-0002`](../../architecture/adr/ADR-0002-modular-monolith-evolution.md) 与双库门禁。

## 2. 目标与非目标

### 2.1 目标

- 流程定义草稿编辑 → 发布为 **不可变** 版本 → 按版本启动实例。
- 步骤状态机：待办、自动节点（后续切片）、人工审批、抄送、超时、取消、驳回、重试与恢复。
- 精确权限码（模块/页面/操作）、租户隔离、审计（定义变更、审批动作、强制恢复）。
- SQL Server 与 MySQL 对称迁移、恢复测试与 Integration。
- 与 Notifications（待办提醒）、Jobs（超时扫描/恢复）通过 Contract/事件协作，不反向耦合。

### 2.2 非目标（1.0 / 本 Spec 边界）

- 可视化设计器全量节点库、子流程嵌套、并行网关复杂编排（后续独立切片）。
- 任意脚本/动态程序集作为节点处理器。
- DataApproval 通用 HTTP 中间件拦截业务写路径（DataApproval 另 Spec，且依赖本模块可用）。
- 创建 `Full.NET.Modules.Workflow.Contracts` 程序集：仅当存在真实跨模块编译期消费者时再拆；首切片可先用主项目内 `Contracts/` 目录 + 事件契约。

## 3. 模块边界与依赖

| 依赖 | 边界 |
| --- | --- |
| **Identity / Tenancy / RBAC** | 权限码、会话、租户上下文；待办指派人必须来自受信主体，禁止信任请求体中的跨租户 UserId |
| **Organization** | 租户实例列表/待办可按机构数据范围过滤；Host 流程 `TenantId IS NULL` |
| **Notifications** | 待办到达/超时提醒经公开 Port 或 Integration Event；Workflow 不直写通知表 |
| **Jobs** | 超时扫描、卡住实例恢复由 Jobs 触发 Workflow 内部 Command；禁止 Jobs 直接 UPDATE 流程表 |
| **Auditing** | 审批/发布/取消/强制恢复写行为审计（非 Outbox） |
| **Messaging** | 跨模块结果与启动副作用使用 Outbox；消费方 Inbox 幂等 |

**硬禁止：** 业务模块 SQL 触达 `fn_workflow_*`；Workflow SQL 触达其他 `fn_*` 业务表；跨模块数据库外键；跨模块 `ICommandTransaction` 包裹多模块写入。

## 4. 核心领域模型

命名遵循 [`naming-conventions.md`](../../../rules/naming-conventions.md)：`fn_workflow_*`，应用端 UUID v7，PascalCase 列。

| 表（草案） | 用途 |
| --- | --- |
| `fn_workflow_definition` | 定义头：稳定 `DefinitionKey`、显示名、作用域、当前草稿指针、最新已发布版本指针 |
| `fn_workflow_definition_version` | **不可变** 版本快照：版本号、图 JSON/MessagePack、校验哈希、发布时间、发布人；发布后禁止 UPDATE 业务列 |
| `fn_workflow_instance` | 运行实例：绑定 `DefinitionVersionId`、业务关联键（类型+外部 Id 摘要）、状态、租约、取消标记 |
| `fn_workflow_step` | 实例内步骤行：节点键、类型、状态、指派、截止时间、尝试次数 |
| `fn_workflow_todo` | 人工待办：步骤、办理人/角色、到达/完成时间、动作结果 |
| `fn_workflow_cc` | 抄送：只读知会，不阻断主路径（除非显式配置，首切片默认不阻断） |
| `fn_workflow_execution_log` | 追加式执行日志：事件类型、载荷摘要、关联步骤、幂等键 |
| `fn_workflow_recovery_checkpoint`（或实例列） | 恢复所需租约世代、下一可调度步骤、挂起原因 |

### 4.1 不可变定义版本

- 草稿可编辑；`Publish` 生成新 `VersionNumber` 并固化图快照与内容哈希。
- 已发布版本禁止修改图结构；纠错只能发布更高版本。进行中实例继续绑定启动时的版本。
- `DefinitionKey` + `VersionNumber` 在作用域内唯一。

### 4.2 实例与步骤

- `Start`：校验已发布版本 → 插入实例与首步骤 → 同事务写 Outbox（如需通知业务方“已启动”）。
- 步骤状态：`Pending` → `Active` → `Completed` | `Rejected` | `Cancelled` | `TimedOut`；非法迁移失败关闭。
- 业务关联键（`BusinessType` + `BusinessId`）在同一租户内对“活跃实例”唯一（策略可配置；首切片建议唯一以防重复启动）。

### 4.3 待办、抄送、人工审批

- 人工节点创建 `todo`；办理动作（同意/拒绝/驳回）必须校验办理人权限与待办归属。
- 抄送写入 `cc` 并可选发通知；默认不参与法定人数。
- 审批意见有长度上限；敏感字段按审计脱敏策略处理。

### 4.4 执行日志与恢复

- 每次状态迁移与外部副作用意图追加 `execution_log`（幂等键防重复）。
- Worker/API 推进使用租约（`LeaseOwner` + `LeaseUntilUtc` + generation）；过期可被其他实例抢占恢复。
- 恢复不得跳过幂等门禁；不得在未补偿情况下重复产生外部副作用。

## 5. 跨模块协作与可靠性

1. **启动：** 业务模块调用 Workflow Contract（同步 Port 返回 `InstanceId`）或发送“请求启动”命令事件；Workflow 为数据所有者。
2. **完成/拒绝：** Workflow 在本地事务提交时写 Outbox；业务模块 Inbox 幂等更新本域状态。
3. **补偿：** 取消或超时触发已声明的补偿端口/事件；禁止静默吞掉失败。
4. **幂等：** 所有外部可观察动作携带稳定幂等键（实例+步骤+动作世代）。
5. **切流无关：** Workflow 不改变 Messaging `DeliveryCutover`；使用当前平台已批准的 Outbox 路径。

## 6. 租户、权限、审计

### 6.1 租户

- 定义与实例均带作用域：`TenantId` 可空表示 Host。
- 列表/详情/待办 SQL 必须带租户谓词；禁止客户端传租户覆盖服务端上下文。

### 6.2 权限码（草案，稳定机器码）

| 权限 | 说明 |
| --- | --- |
| `workflow.definitions.read` | 定义与版本只读 |
| `workflow.definitions.write` | 草稿编辑 |
| `workflow.definitions.publish` | 发布不可变版本 |
| `workflow.instances.read` | 实例/步骤/日志只读 |
| `workflow.instances.start` | 启动实例（亦可由业务 Port 在服务端代呼并审计） |
| `workflow.instances.cancel` | 取消 |
| `workflow.todos.act` | 办理本人待办 |
| `workflow.instances.recover` | 强制恢复/改派（高权限，独立码） |

无权限时 Vue 不创建入口；直接 API 必须 403。角色授权树按模块/页面/操作展示上述码。

### 6.3 审计

发布、取消、强制恢复、审批同意/拒绝必须写审计；日志不含连接串、令牌或超大图全文（可存哈希与摘要）。

## 7. 超时、取消、重试、人工审批

| 机制 | 规则 |
| --- | --- |
| **超时** | 步骤可配置 `DueAtUtc`；Jobs/扫描器触发 `Timeout` 命令；超时策略：自动拒绝、升级改派或保持挂起（定义时声明） |
| **取消** | 仅允许实例所有者权限或业务补偿路径；取消后待办关闭；已完成外部副作用走补偿 |
| **重试** | 自动节点（后续）与推进失败使用有界重试+抖动；进入死信/挂起需可运维恢复 |
| **人工审批** | 首切片核心路径；同意推进下一节点，拒绝结束或回退策略由版本图声明（首切片可仅“结束为 Rejected”） |

## 8. API 形状（草案）

基路径 `/api/v1/workflow/...`，ProblemDetails，OpenAPI 独立契约。

- 定义：`GET/POST /definitions`，`PUT /definitions/{id}/draft`，`POST /definitions/{id}/publish`
- 版本：`GET /definitions/{id}/versions`，`GET /definition-versions/{versionId}`
- 实例：`POST /instances`（start），`GET /instances/{id}`，`POST /instances/{id}/cancel`
- 待办：`GET /todos/mine`，`POST /todos/{id}/approve`，`POST /todos/{id}/reject`
- 日志：`GET /instances/{id}/execution-logs`

## 9. SQL Server / MySQL

- 成对 DbUp 迁移；过滤唯一索引表达“未删除/活跃”约束时双库语义一致。
- 列表索引覆盖 `(TenantId, Status, UpdatedAtUtc, Id)`；待办 `(AssigneeUserId, Status, DueAtUtc)`。
- 恢复测试覆盖半完成索引/约束与二次执行幂等。

## 10. 验收与升档

| 级别 | 条件 |
| --- | --- |
| Spec 审查通过 | 本文件状态改为 Approved；parity 备注更新 |
| Build-verified | 首切片双库 Integration + Architecture + 权限 403 + Vue 门控 |
| Verified | 真实栈 E2E（含恢复与幂等推进）+ 审计抽检；仍受大型模块队列门禁约束 |

## 11. 开放问题（审查时裁定）

1. 业务关联唯一性：同业务键是否允许历史多实例（仅一个 Active）？
2. 首切片是否包含改派与会签（多人全部同意）？
3. 定义图存储：JSON 文本 vs MessagePack 列，以及最大体积。
4. Host 级系统定义是否允许租户实例引用（禁止跨租户数据）。

## 12. 参考

- 队列与门禁：[`adminnet-feature-parity.md`](../../roadmap/adminnet-feature-parity.md) §4.1
- 源码吸收：[`adminnet-source-design-absorption-review-2026-07-30.md`](../../verification/adminnet-source-design-absorption-review-2026-07-30.md)
- 模块通信：[`ADR-0002`](../../architecture/adr/ADR-0002-modular-monolith-evolution.md)、总体架构 Spec §5.3
- 事件交付：[`ADR-0006`](../../architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md)
