# Workflow 模块设计规格

**状态：** Approved（2026-08-30，经当前授权用户审查批准）

**建立日期：** 2026-08-20

**批准基线：** `main@007de9aa91dd6a31788b12a829d07b63aeab1e7a`

**适用范围：** 计划中的 `Full.NET.Modules.Workflow` 单主项目、Host/租户审批流、Vue 管理端、uni-app H5/微信/支付宝、SQL Server/MySQL

**Admin.NET 映射：** 对标 `Admin.NET.Plugin.WorkFlow` 的定义、发布、实例、审批、待办、抄送与业务联动语义；不复制动态类型扫描、SqlSugar、跨模块直连表结构或旧运行时协议

**批准依据：** [`2026-08-30-unified-notifications-workflow-design-assessment.md`](../../verification/2026-08-30-unified-notifications-workflow-design-assessment.md)
**实施计划：** [`2026-08-20-workflow-first-vertical-slice.md`](../plans/2026-08-20-workflow-first-vertical-slice.md)、[`2026-08-30-workflow-designer-form-runtime.md`](../plans/2026-08-30-workflow-designer-form-runtime.md)、[`2026-09-05-workflow-multi-approval.md`](../plans/2026-09-05-workflow-multi-approval.md)

## 1. 决策摘要

Workflow 拥有流程定义、不可变定义版本、表单定义与版本、运行实例、步骤、待办、抄送、表单提交、执行日志与恢复控制面。业务模块只能通过最小 Contract Port 或版本化 Integration Event 启动流程、读取权威状态或接收结果；禁止 JOIN、外键或读写其他模块表。

首版采用 Full.NET 自有审批领域内核，保持模块化单体、Dapper、双数据库、显式状态机、事务 Outbox 与 Worker/Jobs 租约恢复。Elsa、Workflow Core、Flowable、Camunda、Dapr Workflow、Temporal 和通用引擎抽象层均不进入首版；以后只有出现 BPMN 交换、复杂网关、子流程或跨系统编排的真实需求，才通过独立 PoC 与 ADR 决定。

项目所有者已确认 Workflow-Vue3 可使用且已取得作者允许。其本地改造成果作为树形工作流设计器的交互基础；VForm3 作为后台表单设计器与受控 Web Adapter。两者都不是服务端运行时协议：发布器必须把受限 Draft 编译为 Full.NET 自有、不可变、可哈希的 Workflow IR 与 `WorkflowFormSchema`。

## 2. 已批准的长期决策

1. 定义权威格式为规范 JSON；MemoryPack 只用于需要可靠跨进程交付的 Integration Event，不作为定义数据库列格式。
2. 树形审批 Draft 经服务端编译为单一 Workflow IR；LogicFlow 首版仅可作为同一 IR 的候选只读轨迹视图。
3. 允许同一业务键保留历史多实例，但同一作用域最多一个占用中的实例（`active` 或 `suspended`）；批准后重开必须由业务模块显式发起新实例。
4. Reject 保持终态 `Rejected`；已批准的首个退回扩展仅支持当前办理人选择同一实例当前有效执行链上已完成且具有单一办理人快照的人工审批步骤，不等同于重走全程、回原驳回节点或任意节点跳转。已完成多人审批步骤没有唯一回退办理人，本阶段明确不作为退回目标；后续若支持，必须另行定义席位重建和新一轮票数语义。
5. 定义、实例和表单支持 Host/Tenant 双作用域；首版禁止租户实例引用 Host 定义，也禁止跨租户引用定义、表单、主体或文件。
6. 当前可执行节点只有发起、人工审批（单人及显式活动用户列表的 `all`、`any`、`nOfM`）、抄送、排他条件和结束；复杂节点不会因设计器已有 UI 而自动获得发布或执行承诺。
7. VForm3 原始 JSON 只作为后台设计输入；服务端发布时单向编译为 `WorkflowFormSchema`。后台使用受控 VForm3 Web Adapter，uni-app 使用 Full.NET 自研轻量渲染器。
8. 表单能力在只有 Workflow 一个真实消费者时留在 Workflow 主项目内；出现第二个独立消费者后再通过 ADR 评估抽取 Forms 模块。
9. Workflow Todo 是可办理工作的权威状态；Notifications Inbox 只是提醒与阅读状态。统一工作台只能通过 API/UI 组合，不合表、不跨模块 JOIN。
10. 没有真实跨模块编译期消费者前不创建 `Full.NET.Modules.Workflow.Contracts` 项目；没有真实结果消费者前不创建占位 Integration Event 或空 Outbox 写入。

## 3. 目标与非目标

### 3.1 目标

- 定义草稿编辑、校验、规范化和发布不可变版本；运行实例固定绑定启动版本。
- 表单草稿编译、不可变表单版本、流程版本固定绑定、节点字段策略和服务端权威校验。
- 人工待办、单人审批、显式用户会签/或签/N-of-M、抄送、排他条件、终态拒绝、取消、幂等、并发修订和恢复。
- 精确权限码、Host/Tenant 隔离、资源级授权、B0 领域审计和敏感数据边界。
- SQL Server/MySQL 成对迁移、部分 DDL 恢复、Integration、Native AOT 与真实栈验证。
- 与 Notifications、Jobs、Identity、Organization、Files 和业务模块通过明确 Port/事件协作。

### 3.2 非目标

- BPMN 全量兼容、自由图运行时、并行/包容网关、子流程和任意循环。
- 任意 JavaScript、动态程序集、反射扫描、自定义 HTML/iframe、任意 CSS 或任意远程数据源。
- 任意 SQL、HTTP 或跨模块表写入节点；“修改/删除数据”只能在后续变为拥有者模块公开的强类型 Business Command。
- DataApproval 通用 HTTP 中间件拦截业务写路径。
- uni-app 复用 VForm3、Element Plus 或 Web 动态组件运行时。
- 提前创建通用 Forms、Workflow Engine、Provider 或传输项目。

## 4. 模块边界与依赖

| 依赖 | 边界 |
| --- | --- |
| Identity / Tenancy / RBAC | 会话、租户上下文和精确权限；指派主体来自受信目录，禁止信任请求体中的跨租户 UserId |
| Organization | 通过最小批量 Port 解析组织、负责人和数据范围；Workflow 不查组织表 |
| Files | 表单只保存受控 FileId 引用；提交和读取时重新验证租户、资源权限与状态 |
| Notifications | 待办到达/超时/完成提醒走版本化事件；Workflow 不直写通知表，Notifications 不办理待办 |
| Jobs | 超时扫描和卡住实例恢复只触发 Workflow Command；禁止直接 UPDATE Workflow 表 |
| Auditing | 发布、取消、审批、改派和强制恢复写 B0 领域审计；Audit 不使用 Outbox |
| Messaging | 有真实消费者的重要结果事件与 Workflow 本地状态同事务写 Outbox；消费方 Inbox 幂等 |

Workflow 生产 SQL 只访问 `fn_workflow_*`；其他模块禁止访问这些表。跨模块本地事务、外键、视图、同义词和隐藏 JOIN 全部禁止。

## 5. 定义、设计器与单一 IR

### 5.1 发布链路

```text
Workflow-Vue3 Tree Designer（受限节点目录）
  → WorkflowDefinition Draft
  → 客户端强类型结构校验
  → Publish API
  → 服务端权限 + Schema + 语义 + 安全校验
  → 规范化并编译为不可变 WorkflowDefinitionVersion IR
  → 内容哈希
  → Runtime 只执行已发布 IR
```

每个节点使用稳定字符串 `NodeTypeKey + NodeSchemaVersion + NodeKey + Config`。首批类型键为 `start`、`human.approval`、`notify.cc`、`gateway.exclusive`、`end`；发布时拒绝未知节点/字段版本、重复 NodeKey、悬空引用、不可达节点、无终点、非法回边和当前部署不可执行的节点。

服务端 `NodeTypeCatalog` 返回 `Designable / Publishable / Executable` 状态。前端只能发布当前部署已经 `Publishable + Executable` 的闭合节点集合；`Empty` 仅作为布局占位并在编译时消除。

### 5.2 Workflow-Vue3 资产边界

- 选择性迁移树形插入、分支编辑、节点卡片和已批准 Drawer，不迁移旧项目 C#/SqlSugar 运行时、数字 NodeType、无版本 FlowJson、`Math.random()` Id、Mock API、远程字体/图片或 Debug 覆盖层。
- 任意 `new Function`、脚本投票、`remoteUrl/headers/body` 和直写业务数据节点均禁止。
- 实现前归档作者授权、上游提交、本地修改范围、再分发条件和第三方声明；不得把第三方来源标记为 Full.NET 自有 MIT 源码。

## 6. 表单引擎与跨端渲染

### 6.1 权威链路

```text
VForm3 Designer（受限组件目录）
  → WorkflowForm Draft
  → Workflow Publish API
  → 服务端 Schema / 语义 / 安全校验
  → WorkflowFormVersion + WorkflowFormSchema + WebRenderSchema + Hash
  → WorkflowDefinitionVersion 固定绑定 FormVersionId
  → Instance 固定绑定 DefinitionVersionId + FormVersionId
      ├─ Vue：受限 VForm3 Web Adapter
      ├─ uni-app：FullNetFormRenderer
      └─ 服务端：WorkflowFormSchema Validator
```

`WorkflowFormSchema` 是唯一权威表单协议，包含 `SchemaVersion`、`AdapterVersion`、稳定 `FieldKey`、字段类型、服务端约束、语义布局和数据分级。`WebRenderSchema` 只是 VForm3 Adapter 派生产物。客户端提交 `FieldPatch + ExpectedRevision + IdempotencyKey`；服务端按实例绑定版本和当前节点字段策略重新校验，不接受客户端提交或替换 FormJson。

节点字段策略固化为 `NodeKey + FieldKey → Hidden / ReadOnly / Editable / Required`。隐藏字段不渲染且不随任务详情 API 返回；只读、隐藏或未知字段出现在 Patch 中必须失败关闭。

### 6.2 首批组件与安全边界

首批允许文本、文本域、整数、小数、金额、日期、时间、日期时间、单选、多选、下拉和开关。金额使用十进制定点字符串线格式；每种类型固化长度、范围、Scale、舍入、时区、空值和序列化规则。

文件/图片和用户/组织/角色/字典选择器只有在对应受信 Port 与资源授权完成后才能开放。子表格、富文本、签名、公式、关联流程、脚本、任意 HTML/iframe、任意 CSS 和远程数据源不进入首版。首版不得接收需要 S2 字段级加密但尚未有批准保护策略的数据。

VForm3 示例中的 `cssCode`、`functions`、生命周期事件和表单数据事件全部在发布时拒绝；动态条件只使用服务端可验证的声明式规则 AST。

### 6.3 uni-app 轻量渲染器

`clients/uniapp` 使用 Vue 3 + uni-ui/原生组件实现 `FullNetFormRenderer`，同时支持 H5、微信小程序和支付宝小程序。渲染器使用静态组件目录和显式分支/编译期映射，不依赖 `<component :is>`、异步插件注册、VForm3 或 Element Plus。

工作流表单页面进入 uni-app `subPackages`；H5 使用页面级懒加载。Schema 以 `FormVersionId + Hash/ETag` 缓存，候选项按需分页加载。三端允许把后台网格确定性折叠为移动端单列，但字段类型、可见性、必填、只读、选项和值序列化必须一致。

## 7. 核心数据模型

所有表使用应用端 UUID v7、SQL Server `uniqueidentifier`、MySQL `BINARY(16)`、PascalCase 列和显式 Dapper SQL。

| 表 | 用途与关键约束 |
| --- | --- |
| `fn_workflow_definition` | 稳定 DefinitionKey、作用域、Draft/最新发布指针；Host/Tenant 内唯一 |
| `fn_workflow_definition_draft` | 唯一可变 Draft JSON、DraftRevision、Hash 和编辑审计；发布版本不回写 |
| `fn_workflow_definition_version` | 不可变规范 IR JSON、SchemaVersion、VersionNumber、Hash、发布审计；发布后禁止更新业务列 |
| `fn_workflow_form_definition` | 表单稳定标识、作用域和草稿生命周期 |
| `fn_workflow_form_version` | 不可变 WorkflowFormSchema、WebRenderSchema、Hash、Adapter/组件目录版本和发布审计 |
| `fn_workflow_instance` | DefinitionVersionId、FormVersionId、业务关联键、状态、修订号、租约和取消信息 |
| `fn_workflow_step` | NodeKey、NodeTypeKey、状态、单人指派或多人审批模式/法定票数/席位总数快照、截止时间、尝试和并发修订 |
| `fn_workflow_todo` | 步骤、办理主体、状态、到达/完成时间和动作结果；资源级授权权威表 |
| `fn_workflow_approval_slot` | 多人审批节点激活时固化的一人一票事实；同一步骤的办理人和 Todo 均唯一，决定后不可再次投票 |
| `fn_workflow_cc` | 只读知会；默认不阻断主路径 |
| `fn_workflow_form_submission` | 实例表单数据、FormVersionId、修订号和数据分级摘要 |
| `fn_workflow_action_record` | 追加式同意、拒绝、取消和系统动作；保存稳定 ActionKey、主体、Revision 和意见摘要，不以 Todo 字符串代替历史 |
| `fn_workflow_execution_log` | 追加式状态迁移摘要、关联步骤、幂等键；不保存 Secret 或完整敏感表单 |
| `fn_workflow_domain_audit` | 模块自有 B0 领域审计；与发布、审批、取消和强制恢复状态同事务写入，失败回滚 |
| `fn_workflow_recovery_task` | Worker 扫描卡住实例、未完成步骤和过期租约后写入的恢复任务；租约、世代、重试与死信占用键由本表表达 |

实例表通过等价双库约束保证同一 `(Scope, BusinessType, BusinessId)` 最多一个 Active；历史终态实例可并存。具体唯一键与状态投影必须在迁移 RED 测试中证明 SQL Server/MySQL 等价，不使用跨库不一致的过滤索引假设。

## 8. 状态、事务、并发与恢复

- Definition Draft 可变；每次 Publish 创建更高 VersionNumber，不可变版本只能新增不能修补。
- Instance 状态为 `Running / Completed / Rejected / Cancelled / Suspended`；首版 Reject 为终态。
- Step 状态为 `Pending / Active / Completed / Rejected / Cancelled / TimedOut / Returned / RolledBack`；Todo 只能由 Active 走向已办理或关闭。
- 多人审批节点激活时只创建一个 Step，并为 2 至 20 名显式活动用户分别创建 Slot 与 Todo。`all` 的法定票数为 M，`any` 为 1，`nOfM` 必须满足 `1 < N < M`；赞成票达到 N 时批准，赞成票加剩余票不足 N 时提前驳回，收敛后其余未决 Slot/Todo 统一取消。
- Slot 投票、Step/Instance 推进、动作回执和审计使用同一实例行锁顺序与本地事务；相同办理人、动作、请求摘要和幂等键必须返回首次持久化结果，不能用随后变化的票数重算回放响应。
- `selected_completed_human_step` 退回策略只接受同实例、`Completed` 且类型为 `human.approval` 的当前有效链步骤。来源步骤记为 `Returned`，目标及其之后的旧完成链记为 `RolledBack` 并永久退出合法候选；目标节点创建新的 Step/Todo 尝试，保留旧记录作为历史，不重新执行中间自动节点。
- Step 以实例内严格单调 `ExecutionSequence` 表达执行位置，候选按该序号倒序分页，退回失效不得依赖可回拨或被数据库截断的时间戳。升级存量以 ActionRecord 的 `InstanceRevision` 和同事务时间戳关联重建人审与自动节点区间；无法证明顺序的异常行保持空值并从退回能力失败关闭。Expand 迁移保持列可空以兼容滚动期间旧写入，新版本写入必须始终显式分配序号。
- Start、Todo 动作和恢复命令均要求 IdempotencyKey；写操作携带 ExpectedRevision，重复相同请求返回同一确定结果，冲突返回稳定 ProblemDetails。
- 表单 Patch、Todo 动作、步骤/实例推进、execution log、模块自有 `fn_workflow_domain_audit` 和必要 Outbox 在同一 Workflow 本地事务提交。事务内禁止调用其他模块或外部 Provider。
- Worker/API 使用 `LeaseOwner + LeaseUntilUtc + LeaseGeneration`；过期可重领，续租、终态和恢复使用一致锁顺序。失败达到上限进入 Suspended，强制恢复需要独立权限和审计。

## 9. 跨模块协作

1. 启动可由受信任同步 Port 返回 InstanceId，或由有真实消费者的版本化 StartRequested 事件触发；Workflow 为数据所有者。
2. Completed/Rejected/Cancelled 结果只有真实消费方确定后才建立具体 MemoryPack 事件；Workflow 同事务写 Outbox，业务模块用 Inbox 幂等收敛。
3. Assigned/Completed/Rejected/Cancelled 提醒由 Notifications 消费版本化事件；通知失败不回滚审批事实，依靠 Outbox、重试和对账修复。
4. 业务模块保存的 InstanceId 只是无外键关联快照；不得用跨模块事务保证同时创建。
5. Jobs 只调用 Timeout/Recover 等公开命令；不得持有 Workflow Repository 或 SQL。

## 10. 权限、资源授权与审计

| 权限码 | 页面/操作 |
| --- | --- |
| `workflow.definitions.read` | 定义与版本页面 |
| `workflow.definitions.create` | 新建定义 |
| `workflow.definitions.update` | 编辑草稿 |
| `workflow.definitions.publish` | 发布版本 |
| `workflow.forms.read` | 表单定义与版本页面 |
| `workflow.forms.create` | 新建表单 |
| `workflow.forms.update` | 编辑表单草稿 |
| `workflow.forms.publish` | 发布表单版本 |
| `workflow.instances.read` | 实例、步骤和轨迹页面 |
| `workflow.instances.start` | 启动实例 |
| `workflow.instances.cancel` | 取消实例 |
| `workflow.instances.pause` | 暂停运行中实例 |
| `workflow.instances.resume` | 普通恢复已暂停实例 |
| `workflow.instances.recover` | 强制恢复/改派，高权限 |
| `workflow.todos.read` | 我的待办页面 |
| `workflow.todos.approve` | 同意本人待办 |
| `workflow.todos.reject` | 拒绝本人待办 |
| `workflow.cc.read` | 我的抄送页面 |
| `workflow.cc.mark_read` | 标记本人抄送已读 |
| `workflow.recovery_tasks.read` | 恢复任务页面 |
| `workflow.recovery_tasks.retry` | 人工重试恢复任务 |
| `workflow.recovery_tasks.reconcile` | 对账关闭恢复任务 |

页面和操作同时受权限与资源授权保护。`workflow.todos.approve/reject` 不能授权用户办理他人的 Todo；实例详情还必须校验作用域、数据范围和关联主体。无权限时 Vue 不创建入口，直接 API 返回 403。发布、取消、暂停、恢复、审批、改派、强制恢复、恢复任务重试和对账写 B0 审计；显示文本不作为审计机器码。Recovery Worker 扫描与领取是全局后台循环，只允许注册在 Worker `AddBackgroundServices`；管理查询必须携带可信 `TenantScopeKey`。

## 11. API 与序列化边界

基路径 `/api/v1/workflow`，使用标准状态码、ProblemDetails、稳定 operationId、System.Text.Json 源生成和强类型响应。

- 定义：`GET/POST /definitions`、`PUT /definitions/{id}/draft`、`POST /definitions/{id}/publish`
- 表单：`GET/POST /forms`、`PUT /forms/{id}/draft`、`POST /forms/{id}/publish`
- 版本：`GET /definitions/{id}/versions`、`GET /definition-versions/{versionId}`、`GET /form-versions/{versionId}`
- 实例：`POST /instances`、`GET /instances/{id}`、`POST /instances/{id}/pause`、`POST /instances/{id}/resume`、`POST /instances/{id}/recover`、`POST /instances/{id}/cancel`
- 待办：`GET /todos/mine`、`GET /todos/{id}`、`POST /todos/{id}/approve`、`POST /todos/{id}/reject`
- 轨迹：`GET /instances/{id}/execution-logs`
- 恢复任务：`GET /recovery-tasks`、`GET /recovery-tasks/{id}`、`POST /recovery-tasks/{id}/retry`、`POST /recovery-tasks/{id}/reconcile`

客户端 TenantId、AssigneeUserId、FormJson、NodeType 能力状态和字段权限均不是可信授权输入。公开 DTO 和闭合泛型 DI 必须进入 Host.Api Native AOT 静态闭包；禁止运行时反射式多态。

## 12. 双库、AOT、性能与运维门禁

- SQL Server/MySQL 成对 DbUp 迁移，覆盖未记账的部分 DDL 恢复、二次执行、数据保留、唯一性和并发领取。
- 关键查询使用稳定排序和覆盖索引；Todo 列表按作用域、办理人、状态、DueAtUtc、Id 分页，禁止无上界全表扫描。
- Dapper 参数与物化保持 AOT 静态闭包；Host.Api Linux 原生发布后执行双库 HTTP/JSON 真实进程 E2E。Worker AOT 状态按其独立路线如实标记。
- 记录 Active 实例、Active Todo、推进吞吐/错误率/P95/P99、租约冲突、恢复次数、最老挂起时长；标签不得含用户、租户、业务 Id 或表单内容。
- uni-app 首次实现记录 H5 minified/gzip/Brotli、初始/懒加载 Chunk、微信/支付宝主包与分包字节，以及 30/100 字段冷/热渲染指标；基线后再设相对预算。
- 专用生产等价容量认证前保持 `Capacity-not-verified`，不承诺固定 QPS 或毫秒指标。

## 13. 分阶段交付与状态

1. 首切片：定义/表单规范协议、不可变版本、单人审批、Todo、终态拒绝、Vue 受控运行时和双库/AOT 闭环；不含可视化设计器。
2. 通知联动：Assigned/Completed/Rejected/Cancelled 事件、Notifications Inbox 提醒和重放对账。
3. 设计器与跨端：Workflow-Vue3 树形设计器、VForm3 表单设计器、Vue Web Adapter、uni-app 轻量渲染器。
4. 人工审批增强：显式活动用户会签/或签/N-of-M 已进入实现；角色/组织负责人、转办、加签、多人步骤退回和版本化驳回策略仍需独立切片。
5. 耐久控制流与受控集成：延时、超时、汇聚、子流程、Connector/Business Command；每类能力需独立发布与执行门禁。

Spec 批准不代表实现。能力保持 `Designing/Planned`，只有实施计划的 RED/GREEN、双库、权限、客户端和真实栈证据完成后才能晋级。

## 14. 后续阶段启动门禁

- 首个真实业务结果消费者必须在结果事件切片启动前确定；否则不创建 Contracts 项目和占位事件。
- VForm3 精确版本、与当前 Vue/Element Plus/TypeScript/Vite 的兼容 PoC、许可证归档和 THIRD-PARTY-NOTICES 必须在设计器切片开工前完成。
- Workflow-Vue3 授权原件、上游提交和本地修改说明必须在源码迁入前归档。
- 文件、富文本、敏感字段、复杂节点、外部 Connector 和跨租户能力各自需要满足本 Spec 的安全与数据门禁，不能由本次批准自动开放。

## 15. 参考

- [批准评估](../../verification/2026-08-30-unified-notifications-workflow-design-assessment.md)
- [ADR-0002 模块化单体演进](../../architecture/adr/ADR-0002-modular-monolith-evolution.md)
- [ADR-0006 事务 Outbox/CDC/Kafka](../../architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md)
- [ADR-0008 API Native AOT](../../architecture/adr/ADR-0008-api-native-aot-runtime-boundary.md)
- [Admin.NET 对标路线](../../roadmap/adminnet-feature-parity.md)
