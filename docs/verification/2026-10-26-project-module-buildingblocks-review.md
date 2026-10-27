# Full.NET 项目模块、基础模板与 BuildingBlocks 设计审查

> **命名整改（2026-10-27）：** 第 9 节保留的 4 项 MySQL 命名违规已修复，具体变更及本轮验证见第 10 节。

> **合并状态（2026-10-27）：** 修复提交 `77af180c8` 已快进合并至原工作区 `main`。原有未提交改动已恢复，冲突处理及组合验证见第 9 节；下方第 8 节的独立工作树状态属于当时交付记录。

> **修复状态（2026-10-26）：** 第 8 节记录本轮实现、针对性验证与剩余限制。修复位于独立开发工作树 `Full.NET-review-remediation` 的 `codex/review-remediation` 分支，基于下述远程提交；原工作区的未提交修改保持原样。第 1～7 节是历史审查证据，不代表修复后仍存在全部问题。

> **最新复审：** 已于 2026-10-26 获取远程 `main`，固定审查提交为 `ce2539cb0fb46bcd3b3801610bf4ba07147887a2`。由于本地未提交修改与远程重叠，快进合并被 Git 拒绝，工作区 HEAD 未更新。第 7 节是直接读取远程 Git 对象的复审结论，优先于前次结论；第 1～6 节保留首轮审查基线，源码相对链接可能指向不同的本地版本。最新结论共 11 项代码路径问题、1 项设计建议，其中 R07 已收窄范围，R09 为本轮新增。

## 1. 范围、基线与结论

- 审查日期：2026-10-26。
- Git 基线：`main`，`a77902afd87af1662af3b43206d85160eeec566b`；结论针对审查时的工作区，包含既有未提交修改，并非只针对该提交。
- 范围：29 个官方业务模块的入口与依赖、主要业务协作链路、21 个 BuildingBlocks 项目、官方 Host 装配、`fullnet-app` 基础模板、CRUD 生成与 EnterpriseRequest 示例。
- 方法：源码与规则静态审查、项目依赖图检查、关键调用链追踪、现有测试意图核对。未运行应用、数据库、Kafka、Redis、浏览器、构建或测试。
- 本次产出：仅本审查文档；未修复实现。写入前使用任务快照 `review-modules-buildingblocks-20261026` 记录既有改动的路径和哈希，未复制仓库或生成构建产物。
- 本文是审查记录，建议不构成已批准的实施方案，也不替代已有设计决策或验收证据。

**总体判断：强化型模块化单体的方向应保留；主要风险集中在模块协作的失败恢复、实时连接的隔离与撤销、模板与正式 Host 的行为一致性。** 当前证据不足以支持按菜单拆分模块、统一改成微服务或增加通用 Repository。

本报告合并前次 8 项审查意见，并补充 BuildingBlocks 的 3 项问题：共 **10 项代码路径问题、1 项模块组合设计建议**。其中 R04 是已有专项审查中仍未闭环的问题，R08 是设计建议；不能把全部条目视为本次新发现的运行故障。P1 表示应优先处理的权限、隔离、可靠性或部署门禁问题；P2 表示需安排处理的一致性与恢复问题。静态确认的路径不等于已经完成运行时复现。

## 2. 覆盖与已有设计

### 2.1 业务模块与模板

| 范围 | 审查重点与覆盖深度 |
| --- | --- |
| Identity、Tenancy、Organization、Platform、Settings、Regions | 入口、依赖、权限与租户上下文边界；深入检查 Identity 媒体引用释放及会话撤销链路 |
| Workflow、DataApproval、SerialNumbers | 入口与依赖；深入检查审批发起、业务应用、重试与恢复、流水号能力的依赖闭包 |
| Files、Document | 引用声明、删除保护与释放恢复；结合工作区已有 Document 修复，未把旧缺陷直接套用到新实现 |
| Notifications、Messaging、Jobs、Auditing、ObservabilityAdmin、Webhooks | 模块装配与后台职责；追踪通知投递、审计中间件、事件消费及跨模块边界 |
| Payments、CodeGeneration | 深入检查创建订单幂等、生成 Apply 的多阶段写入和回滚边界 |
| Reporting、ImportExport、Printing、Calendar、Ai、Ocr、Cryptography、GoView、K3Cloud、Mqtt | 入口、依赖与对外职责抽查；不代表每个功能分支已逐行审计或实测 |
| API、Worker、Migrator、AppHost、Composition | 职责分离与静态装配；比较正式 API 与生成项目的启动管线 |
| `templates/fullnet-app`、CRUD 模板、`samples/enterprise-request` | 项目启动管线、生成代码的交付边界、自定义审批操作如何延续权限与恢复约束 |

29 个官方模块的入口均已纳入依赖盘点。检查中未发现业务主项目直接引用其他业务模块实现项目的情况；跨模块 Contract 引用本身符合边界，但不能据此推断运行时写入一致性已经成立。EnterpriseRequest 和审批应用链路说明：使用接口调用，也仍然需要明确失败恢复与执行事实。

### 2.2 BuildingBlocks 全部 21 个项目

表中项目名均省略 `Full.NET.` 前缀；“未发现新增问题”只表示本次覆盖路径未形成可确认条目。

| 项目 | 职责与重点检查 | 审查判断 |
| --- | --- | --- |
| [Abstractions](../../src/BuildingBlocks/Full.NET.Abstractions) | Result、时间、ID、租户与命令事务基础契约 | 保持小型稳定契约；未发现需要引入通用 Repository 的理由 |
| [Data.Abstractions](../../src/BuildingBlocks/Full.NET.Data.Abstractions) | 显式 SQL、执行器、连接与事务契约 | 租户绑定与 SQL 范围在执行边界校验，方向正确 |
| [Data.Dapper](../../src/BuildingBlocks/Full.NET.Data.Dapper) | DbSession、CommandTransaction、执行器、Outbox/Inbox、健康检查 | 事务失败与嵌套回滚有显式处理；Schema 门禁见 B03 |
| [Data.MySql](../../src/BuildingBlocks/Full.NET.Data.MySql) | MySQL 连接参数与 UUID 表示 | 集中控制 Binary16、冲突连接参数及受影响行语义；本次无双库运行证据 |
| [Data.CodeGeneration](../../src/BuildingBlocks/Full.NET.Data.CodeGeneration) | CRUD 生成、文件集成与恢复 | 生成与集成缺少统一恢复边界，见 R04；保留已有专项整改跟踪 |
| [Caching.Abstractions](../../src/BuildingBlocks/Full.NET.Caching.Abstractions) | 缓存策略、作用域与失效契约 | 强一致类别与普通缓存有区分 |
| [Caching.Fusion](../../src/BuildingBlocks/Full.NET.Caching.Fusion) | FusionCache、HybridCache、L1/L2 与 Backplane | 已区分本节点失效和同步全层失效，禁止缓存类别失败关闭；未发现需要改用 Outbox 的理由 |
| [Hosting](../../src/BuildingBlocks/Full.NET.Hosting) | HTTP、ProblemDetails、代理、限流、可观测性与健康端点 | 公共能力集中；模板的组合顺序漂移见 R01，Schema 检查语义见 B03 |
| [Hosting.Migrator](../../src/BuildingBlocks/Full.NET.Hosting.Migrator) | 迁移与播种宿主流程 | 先迁移、成功后播种，错误与取消有独立路径；应继续隔离于 API/Worker |
| [Localization](../../src/BuildingBlocks/Full.NET.Localization) | 请求和后台文化上下文 | 按当前异步上下文读取并恢复 Culture；未发现新增问题 |
| [Logging.Kafka](../../src/BuildingBlocks/Full.NET.Logging.Kafka) | 有界日志投递、优先级与停机排空 | 队列接受与 Broker 确认分开计量；不应宣称每条日志可靠入库 |
| [Messaging.Abstractions](../../src/BuildingBlocks/Full.NET.Messaging.Abstractions) | 事件信封、订阅与发布所有权契约 | 至少一次、幂等及切流约束有明确表达 |
| [Messaging.Kafka](../../src/BuildingBlocks/Full.NET.Messaging.Kafka) | 消费作用域、重试/DLQ、位点提交 | 处理与转存成功后才推进安全位点；本次未进行 Broker 故障验证 |
| [Migrations.DbUp](../../src/BuildingBlocks/Full.NET.Migrations.DbUp) | 双库脚本、迁移范围与执行 | Migrator 可选择脚本范围；应用启动尚未验证对应范围已齐备，见 B03 |
| [Modularity](../../src/BuildingBlocks/Full.NET.Modularity) | 模块图、Command/Query 管线、事件分派 | 有重复/缺失/循环依赖检查；消费在事务内处理 Inbox、业务及完成标记 |
| [Realtime.Abstractions](../../src/BuildingBlocks/Full.NET.Realtime.Abstractions) | 消息、发布目标与组名 | 用户目标缺少作用域语义，见 B01 |
| [Realtime.SignalR](../../src/BuildingBlocks/Full.NET.Realtime.SignalR) | 连接鉴权、分组与多实例投递 | 初次连接校验较完整；用户组隔离及连接生命周期见 B01、B02 |
| [Seeding.Abstractions](../../src/BuildingBlocks/Full.NET.Seeding.Abstractions) | Baseline/Overlay、贡献者与执行契约 | 生产播种边界清晰 |
| [Seeding.Dapper](../../src/BuildingBlocks/Full.NET.Seeding.Dapper) | 排序、执行锁、幂等状态与审计 | Production 限制、依赖排序、执行锁已有实现；锁连接故障场景未实测 |
| [Serialization.MemoryPack](../../src/BuildingBlocks/Full.NET.Serialization.MemoryPack) | 受控事件载荷序列化 | 保持显式 DTO 与静态生成边界；未进行 Native AOT 发布验证 |
| [Validation.FluentValidation](../../src/BuildingBlocks/Full.NET.Validation.FluentValidation) | 命令验证与 Result 转换 | 应继续与 Endpoint 授权分离；本次未形成实际消费者缺陷条目 |

对 21 个 BuildingBlocks 的 `ProjectReference` 静态检查得到 34 条引用边，未发现项目引用环，也未发现反向引用业务模块或 Host 的项目。该检查不涵盖所有运行时服务依赖或字符串注册关系。

### 2.3 应保留的关键边界

- API、Worker、Migrator 分离，AppHost 负责编排，Composition 使用显式模块目录。
- Dapper 执行前校验 SQL 范围与可信租户；`DbSession` 管理事务连接，嵌套失败通过 rollback-only 阻止外层错误提交。
- Outbox 发布所有权在事务内受门禁保护，Legacy/CDC 路由互斥；消费侧在恢复租户上下文后，以本地事务协调 Inbox 与业务处理。
- 缓存的强一致类别、共享 L2 类别及同步失效已有独立语义；日志和实时刷新通知不应因为本报告被统一改为业务 Outbox。
- 数据库迁移与播种留在 Migrator，生产 Baseline 与环境 Overlay 保持分离。

上述判断来自实现结构，不代表已通过故障恢复、性能或生产容量验收；容量状态仍应保持 `Capacity-not-verified`。

## 3. 合并的前次审查意见

### R01 · P1 · 基础项目模板遗漏审计管线阶段

**证据：** [模板 Program.cs](../../templates/fullnet-app/src/FullNetAppNameToken.Host.Api/Program.cs) 第 36 行直接安装异常处理，未调用 `BeforeExceptionHandler` 模块阶段；第 38 行的限流在认证前。[正式 API Program.cs](../../src/Hosts/Full.NET.Host.Api/Program.cs) 包含该阶段，且认证先于限流。[AuditingModule.cs](../../src/Modules/Full.NET.Modules.Auditing/AuditingModule.cs) 第 210 行开始在遗漏阶段安装访问日志与 `AuditWriteCoordinatorMiddleware`。

**触发与影响：** 从基础模板创建应用并启用 Auditing 后，即使模块注册成功，也缺少请求级审计汇总写入边界和访问日志。模板中认证前限流还会让依赖身份的分区得不到已认证主体。模板与正式 Host 因复制管线而产生行为差异，使用者难以从装配成功发现问题。

**建议：** 将必要阶段、认证及限流顺序收敛为可复用的 API 管线组合入口，保留明确扩展点；模板与正式 Host 复用同一约束。

**针对性验收：** 从模板生成应用，分别触发成功写入、业务失败和异常请求，确认重要审计及访问日志落地；验证认证用户的限流分区，并比较两种 Host 的模块阶段顺序。

### R02 · P1 · 示例自定义提交操作未延续精确权限与数据范围

**证据：** [SubmitForApproval/Endpoint.cs](../../samples/enterprise-request/src/Full.NET.Modules.EnterpriseRequest/Features/SubmitForApproval/Endpoint.cs) 第 34 行仅调用 `RequireAuthorization()`；[SubmitEnterpriseRequestForApprovalService.cs](../../samples/enterprise-request/src/Full.NET.Modules.EnterpriseRequest/Features/SubmitForApproval/SubmitEnterpriseRequestForApprovalService.cs) 以 ID 读取记录后直接校验状态并提交，没有复用组织数据范围或申请人约束。

**触发与影响：** 同租户已登录用户知道其他组织范围内草稿 ID，即可到达提交路径，即使生成的 CRUD 不允许其访问该记录。租户 SQL 过滤仍在，问题不是已证实的跨租户读写，而是同租户权限及行范围绕过。示例容易把这一模式传播到业务扩展。

**建议：** 定义稳定的提交权限，在 Endpoint 授权；应用服务独立检查可信组织范围及业务允许的操作者身份。

**针对性验收：** 无提交权限、同租户不同组织、非允许申请人均不能提交；具有权限且在允许范围内的操作者正常提交。

### R03 · P1 · 示例提交状态与 Workflow 发起之间存在不可恢复窗口

**证据：** 同一 [SubmitEnterpriseRequestForApprovalService.cs](../../samples/enterprise-request/src/Full.NET.Modules.EnterpriseRequest/Features/SubmitForApproval/SubmitEnterpriseRequestForApprovalService.cs) 先执行 `ApplySubmittedStatus`，再调用 `workflowStarter.StartAsync`；失败只返回错误。入口又要求记录必须为 Draft。示例未提供这一中间状态的恢复处理。

**触发与影响：** 状态更新成功后进程退出，或 Workflow 发起失败，业务单据停在 Submitted，重试提交会被状态校验拒绝，不能依靠原入口补建流程。Workflow 自身的幂等键无法补偿调用方丢失的推进机会。

**建议：** 在业务模块本地事务中持久化提交意图及可靠事件，以可重放状态机推进 Workflow，并提供失败重试、补偿或对账。不要用跨模块共享本地事务消除窗口。

**针对性验收：** 在业务提交后、流程创建前中断；恢复后最终只创建一个流程，单据不永久卡住，重复消息不重复推进。

### R04 · P1 · CodeGeneration 的 Apply 失败可能留下无法按标准入口回滚的部分写入

**证据：** [ModuleIntegrationHostOrchestrator.cs](../../src/BuildingBlocks/Full.NET.Data.CodeGeneration/Integration/ModuleIntegrationHostOrchestrator.cs) 顺序写入后端集成、模块入口、Composition、Vue 和授权等文件；后续阶段失败不会统一撤销前序阶段。[CodeGenerationApplyService.cs](../../src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/CodeGenerationApplyService.cs) 第 214 行为生成计划创建检查点，第 246 行才执行集成；检查点未覆盖完整集成变更集合。[CodeGenerationRollbackService.cs](../../src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/CodeGenerationRollbackService.cs) 第 60 行仅接受 Succeeded 的 Apply。

**触发与影响：** 后续集成失败时，磁盘已部分修改，运行状态为 Failed，普通回滚入口又拒绝它；用户必须手工辨认和恢复文件。

**建议：** 先规划全部将修改的文件，建立统一检查点，再执行写入；失败状态也应具有明确的恢复入口，恢复前校验用户后续修改，不能覆盖并发编辑。

**针对性验收：** 在每个集成阶段注入失败，验证已写文件都可恢复；存在用户并发修改时明确报冲突。已有 [CodeGeneration 专项审查](2026-10-24-codegeneration-remediation-review.md) 与 原工作区整改计划（`docs/superpowers/plans/2026-10-24-codegeneration-remediation.md`，该未提交文件不在远程修复基线内） 应继续作为该项跟踪入口，本报告不另建重复计划，也不将工作区已修正的其他生成问题重新判为未修复。

### R05 · P1 · 支付订单创建缺少调用方业务幂等契约

**证据：** [CreatePaymentOrderRequest](../../src/Modules/Full.NET.Modules.Payments.Contracts/PaymentOrderContracts.cs) 未包含稳定业务请求标识；[PaymentOrderManagementService.cs](../../src/Modules/Full.NET.Modules.Payments/Features/ManageOrders/PaymentOrderManagementService.cs) 的创建路径每次生成新的订单 ID 与 OutTradeNo。

**触发与影响：** 服务端创建订单后响应丢失，调用方重试会创建第二个可支付订单。已有订单的渠道查询、回调幂等与对账，不能识别两个新订单属于同一次业务请求。这不等于已证实会自动重复扣款。

**建议：** 将租户、业务来源及调用方幂等键纳入订单创建契约，使用数据库唯一约束，并持久化请求摘要；同键同载荷返回原结果，同键不同载荷拒绝。

**针对性验收：** 并发重复请求和响应丢失重试返回同一订单；不同载荷重用幂等键产生明确冲突；SQL Server 与 MySQL 均验证唯一性行为。

### R06 · P2 · 媒体绑定清除后，Active 文件引用释放失败会失去重试依据

**证据：** [SelfServiceProfileMediaService.cs](../../src/Modules/Full.NET.Modules.Identity/Features/SelfServiceProfile/SelfServiceProfileMediaService.cs) 第 306 行附近先清空头像或签名绑定，第 312 行再释放引用；`ReleaseMediaClaimAsync` 未处理 `ReleaseAsync` 的失败 Result。重复清除时已没有原文件 ID。[PendingHostFileReferenceClaimReconciliationRunner.cs](../../src/Modules/Full.NET.Modules.Files/Reconciliation/PendingHostFileReferenceClaimReconciliationRunner.cs) 只扫描 Pending 引用，不恢复这类 Active 孤立引用。

**触发与影响：** 绑定清除成功、释放失败或进程中断后，原 Active 引用可能长期阻止文件清理。Tenancy Logo 的类似调用也应一起核对。此结论不能直接扩展为当前 Document 同样缺失恢复；工作区 Document 已出现引用释放事件相关实现。

**建议：** 在持有绑定的模块本地事务中记录待释放事实，通过幂等消费者完成 Files 释放，并保留对账依据；不要仅忽略失败或依赖用户再次点击。

**针对性验收：** 在清除绑定与释放之间中断、返回失败及重复释放，最终引用收敛且文件可按保留策略清理，不错误释放仍在使用的引用。

### R07 · P2 · 审批应用使用当前值比对代替持久执行回执

**远程复审限定：** 本项仅适用于仍调用 `DataApprovalRequestApplicationService.TryApplyApprovedChangeAsync` 的原审批应用链路。远程已新增带事务执行回执的业务命令路径，不能把该路径也归为此缺陷；详见第 7.3 节。

**证据：** [SerialRuleChangeApprovalApplier.cs](../../src/Modules/Full.NET.Modules.SerialNumbers/Features/DataApprovalBridge/SerialRuleChangeApprovalApplier.cs) 只校验 `idempotencyKey` 长度，没有持久记录该键；版本冲突时以当前字段是否等于批准快照判断已执行。[DataApprovalRequestService.cs](../../src/Modules/Full.NET.Modules.DataApproval/Features/ManageRequests/DataApprovalRequestService.cs) 与 [恢复处理器](../../src/Modules/Full.NET.Modules.DataApproval/Execution/DataApprovalRequestApplicationRecoveryBatchProcessor.cs) 的重试入口使用不同后缀的执行键，首次事件路径又使用消息标识。

**触发与影响：** 业务变更提交后、审批侧记账前中断，随后规则被合法再次修改；重放时当前值不再等于旧快照，无法证明该审批已成功应用，可能把已完成的业务变更留在失败或待恢复状态。当前版本检查可防覆盖，但不能充当历史执行凭证。

**建议：** 所有入口使用稳定审批请求 ID；在业务所有者本地事务中同时提交业务变更和执行回执，重放读取回执而非从当前业务值推断历史。

**针对性验收：** 应用成功后中断审批记账，再修改业务记录并重放旧审批；应识别原审批已应用，保持后续合法修改，不覆盖也不误判为未执行。

### R08 · P2 设计建议 · 流水号基础能力与审批链路组合过紧

**证据：** [SerialNumbersModule.cs](../../src/Modules/Full.NET.Modules.SerialNumbers/SerialNumbersModule.cs) 声明 Identity 与 DataApproval 为必需依赖。依赖图展开后，流水号模块需要 DataApproval、Files、Identity、Notifications、Organization、Tenancy、Workflow 七个其他模块。[DataApprovalScenarioCatalog.cs](../../src/Modules/Full.NET.Modules.DataApproval/Domain/DataApprovalScenarioCatalog.cs) 的场景目录又直接固定流水号规则场景。

**设计影响：** 只需要流水号分配的消费者也必须装配审批体系；通用审批目录容易随业务场景增长而修改中央代码。这里描述的是运行时能力组合，不是已经发现实现程序集相互引用或循环依赖。

**建议：** 明确流水号分配与“规则变更必须审批”的不同能力，按实际 Host Profile 显式组合审批桥接；场景由业务消费者通过静态目录贡献。没有真实消费者证据时不机械增加项目；配置要求审批而能力未装配时必须启动失败，不能静默绕过。

**针对性验收：** 最小流水号组合可启动并分配；启用审批的组合仍执行强制门禁；缺失审批实现时失败关闭。此项应先确认最小组合用例，再安排实现。

## 4. BuildingBlocks 补充问题

### B01 · P1 · 用户实时消息组未区分 Host 与租户作用域

**证据：** [RealtimeGroups.cs](../../src/BuildingBlocks/Full.NET.Realtime.Abstractions/RealtimeGroups.cs) 的 `User(Guid)` 只生成 `user:{id}`；[FullNetNotificationHub.cs](../../src/BuildingBlocks/Full.NET.Realtime.SignalR/FullNetNotificationHub.cs) 第 44 行附近把 Host 和各租户连接加入同一个用户组。[SignalRRealtimePublisher.cs](../../src/BuildingBlocks/Full.NET.Realtime.SignalR/SignalRRealtimePublisher.cs) 的 `PublishToUserAsync` 不使用 `currentTenant`，而租户广播和 Host 广播均有上下文检查。

**实际消费者：** [NotificationRealtimeDelivery.cs](../../src/Modules/Full.NET.Modules.Notifications/NotificationRealtimeDelivery.cs) 按作用域读取未读数，却将该数值、站内信标题和消息 ID 投递到无作用域用户组；这些载荷也没有作用域字段供客户端区分。

**触发与影响：** 同一账号同时连接租户 A、租户 B 或 Host，A 的私信提示及未读数会到达该账号的其他作用域连接，造成跨作用域标题投递和徽标污染。该证据不表示任意其他用户可以读取消息，也不表示 HTTP 查询已绕过租户过滤。

**建议：** 业务私信目标使用可信作用域加用户 ID；明确区分按作用域投递的业务通知与全账号控制消息。会话撤销确有跨作用域通知需求，应使用单独的控制目标，不要直接把所有用户组一刀切改为租户组。

**针对性验收：** 同一用户同时保持 Host、A、B 三条连接，业务消息仅到指定作用域；另一用户不接收；全账号或指定会话撤销仍按契约到达，多实例下保持一致。

### B02 · P1 · 实时连接缺少服务端身份过期及撤销闭环

**证据：** [WebApplicationExtensions.cs](../../src/BuildingBlocks/Full.NET.Realtime.SignalR/WebApplicationExtensions.cs) 第 29 行映射 Hub 时未设置 `CloseOnAuthenticationExpiration`。[FullNetNotificationHub.cs](../../src/BuildingBlocks/Full.NET.Realtime.SignalR/FullNetNotificationHub.cs) 在建立连接时检查身份、作用域与租户活跃状态，后续未重新校验或服务端关闭连接。[IdentitySessionRealtimeDelivery.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageHostOnlineSessions/IdentitySessionRealtimeDelivery.cs) 发送 `SessionRevoked`，由客户端比对并清理凭据；未发现相应服务端连接撤销实现。

**触发与影响：** 已建立的 WebSocket 在令牌过期或会话撤销后，若客户端忽略通知且保持连接，仍可能继续接收原组推送。HTTP 请求的权威撤销校验仍可生效，不能据此推断新的 HTTP 操作可绕过认证。

ASP.NET Core 默认不会在整个连接期间持续重新验证令牌，过期后既有连接可继续工作；过期关闭需要显式配置。参见 [Microsoft Learn：SignalR 身份验证与授权（ASP.NET Core 10.0）](https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0)。这里的配置只能处理过期，不能独自解决即时撤销。

**建议：** 对鉴权连接启用过期关闭；建立以会话、用户及作用域为索引的服务端撤销机制，在各实例关闭或取消失效连接的组成员关系。租户或成员资格失效也应有明确收敛时限。客户端撤销消息保留为体验提示，不能承担隔离保证。

**针对性验收：** 使用忽略撤销通知的客户端，分别测试令牌到期、会话撤销、租户停用；在单节点与跨节点场景确认超过约定期限不再收到业务消息。仅测试客户端主动断线不足以验收。

### B03 · P1 · Schema 启动门禁只验证 UUID 模式标记，不能验证应用所需迁移

**证据：** [DatabaseSchemaHealthCheck.cs](../../src/BuildingBlocks/Full.NET.Data.Dapper/Health/DatabaseSchemaHealthCheck.cs) 读取 `fn_uuid_contract_state.SchemaMode`，非空即返回 Healthy；没有比较当前应用所需迁移或模块 Schema 版本。[ServiceCollectionExtensions.cs](../../src/BuildingBlocks/Full.NET.Data.Dapper/ServiceCollectionExtensions.cs) 第 227 行将其注册为 `startup`，数据库连通性另注册为 `ready`。[HealthEndpointExtensions.cs](../../src/BuildingBlocks/Full.NET.Hosting/Observability/HealthEndpointExtensions.cs) 按这两个标签分别映射端点。MySQL 的模式启动校验也只核对 UUID 存储模式，不能补足迁移完整性。

**触发与影响：** 数据库保留有效 UUID 模式标记，但尚未执行新增业务列或表的迁移，数据库连通性与 Schema 启动检查仍可通过；新应用因此可能通过探针后在业务路径报表或列不存在。现有 Migrator 执行顺序可以降低发生概率，但当前探针不能提供其注释宣称的版本兼容保证。

**建议：** 为当前发布及所选模块生成最低 Schema 契约清单，Migrator 成功后记录完成事实，API/Worker 通过只读契约检查齐备性；同时考虑 Provider、模块范围和失败中的迁移。避免仅为检查版本让 API 引入 DbUp 执行依赖，也不要把外部依赖加入存活探针。

**针对性验收：** SQL Server 与 MySQL 均构造“UUID 标记正常但缺少一项必需迁移”的数据库，启动门禁必须失败；正确迁移后通过，未装配模块的脚本不应导致误拒绝。

## 5. 建议处理顺序与架构取舍

以下是审查后的建议分组，不是已执行的修复或全量测试指令。

| 顺序 | 范围 | 期望闭环 |
| --- | --- | --- |
| 1 | B01、B02、R02 | 先补实时作用域隔离、服务端连接撤销，以及示例提交授权 |
| 2 | R01、B03 | 使模板、正式 Host 与部署门禁提供一致且可证明的行为 |
| 3 | R03、R05、R07 | 分别明确业务意图、调用幂等和业务执行回执，避免把当前状态当作历史事实 |
| 4 | R04、R06 | 完成文件系统 Apply 恢复与跨模块文件引用释放恢复；R04 沿用已有整改入口 |
| 5 | R08 | 以实际最小消费者验证模块组合需求，再收窄强制依赖 |

建议遵守三个设计约束：模块内维护本地事务；跨模块写入以可靠意图、幂等及恢复协调；基础设施只承载通用机制，业务权限和执行事实仍归业务所有者。不要因本次问题引入跨模块表访问、共享事务、通用 CRUD Repository，或为了目录对称机械拆出更多项目。

## 6. 验证记录与限制

本次实际完成：

- 29 个官方模块入口和依赖盘点；21 个 BuildingBlocks 项目引用关系检查，34 条引用边，无发现的项目引用环或反向模块/Host 项目引用。
- 上述问题的入口、关键写入或投递点、失败分支及已有恢复机制静态追踪；对相关现有测试意图进行阅读，不将测试文件存在视为测试通过。
- 文档本地相对链接、UTF-8 内容、条目编号、空白格式和本任务变更范围检查。

未执行：

- 单元、集成、端到端或全量测试；模板生成运行；SQL Server/MySQL 迁移及并发实测。
- Redis/Kafka 断连、重投递、重平衡、多实例 SignalR 与会话撤销实测。
- Native AOT 发布、裁剪检查、浏览器验收或容量压测。

因此，本报告可用于安排设计整改和针对性验证，不能作为生产可用、双库兼容、AOT 可发布或容量达标证明。每项建议所列场景是后续验收目标，不是本次已通过的测试。工作区存在其他任务的未提交变更，后续修复前应重新确认相应代码与本报告证据是否仍一致。

## 7. 获取远程最新代码后的复审

### 7.1 获取结果与审查版本

- 执行 `git fetch origin` 成功，远程地址为 `https://github.com/yan041108/Full.NET.git`。
- 本轮固定版本：[`ce2539cb0fb46bcd3b3801610bf4ba07147887a2`](https://github.com/yan041108/Full.NET/commit/ce2539cb0fb46bcd3b3801610bf4ba07147887a2)，提交说明为 `feat: checkpoint Workflow, Messaging and VForm3 delivery`。
- 本地 HEAD 为 `a77902afd87af1662af3b43206d85160eeec566b`，落后远程 1 个提交。该提交实际涉及 1,324 个文件，不能因提交数少而按小改动处理。
- 获取时工作区有 314 条状态记录；`git merge --ff-only origin/main` 因 18 个已修改文件会被覆盖而中止。没有执行 stash、reset、checkout 覆盖或冲突合并，也没有创建源码副本。
- 本轮使用 `git show <固定提交>:<路径>`、提交间 diff 与 Git 对象依赖图开展审查。下文证据使用固定提交链接，不把本地后续修改当成远程已交付能力。
- 快进尝试前创建任务快照 `remote-review-20261026`，只保存变更路径与内容状态；本轮文档修改仍限于本文件。

**结果：远程代码已获取并完成静态复审，当前工作区尚未合并该远程提交。** 本文不提供合并后组合版本的行为保证。

### 7.2 增量范围与覆盖深度

在复用首轮未变更模块证据的基础上，本轮逐条复核原意见，并重点追踪新增运行边界：

| 范围 | 远程变动规模 | 本轮重点 |
| --- | --- | --- |
| Workflow 主项目 | 231 个文件 | 连接器领取、外部调用、结果接纳与恢复；业务命令发布、所有者处理及回执；后台租户上下文；表单发布与运行绑定 |
| Messaging 主项目 | 39 个文件 | 运维重放持久进度、取消与未知结果；交付所有权回退、控制资源身份、生产者保护；DLQ 投影与权限入口 |
| BuildingBlocks | 7 个项目涉及变更，含迁移项目 | Dapper 查询异常归类、Legacy/Shadow 写入、Kafka 重放与 DLQ、控制面回退，以及 HTTP 请求体错误契约 |
| VForm3 与后台 | `admin-form-designer` 383 个文件，`ui/admin` 151 个文件 | 隔离帧宿主、消息来源与通道核验、发布产物摘要、切换任务时旧回执失效、字段策略与静态资源交付；未逐行审计全部第三方源码 |
| 模块协作与客户端 | DataApproval、SerialNumbers、Organization、Files 等；Flutter 31、uni-app 15 个文件 | 审批新旧应用入口和契约影响；客户端变更盘点，未进行移动端或浏览器行为验收 |
| 基础项目模板及 EnterpriseRequest | 基础模板未变，示例模块注册变更 1 个文件 | 对照正式 Host 新增管线能力；复核自定义提交的权限及失败窗口 |

重新从远程对象计算：仍为 29 个官方模块、21 个 BuildingBlocks 项目、34 条 BuildingBlocks 项目引用边；未发现 BuildingBlocks 项目引用环或反向业务模块/Host 引用。该结果不等于所有运行时组合均无依赖问题。

### 7.3 原审查意见的远程状态

| 编号 | 远程结论 | 固定版本证据与变化 |
| --- | --- | --- |
| R01 | 保留，模板漂移范围增加 | [模板入口](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/templates/fullnet-app/src/FullNetAppNameToken.Host.Api/Program.cs#L36) 仍遗漏审计前置阶段，限流仍在认证前；[正式 Host](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/Hosts/Full.NET.Host.Api/Program.cs#L48) 又新增 `UseFullNetRequestBodyProblemDetails`，模板未同步，框架绑定产生的空 400/413/415 响应契约也会不同 |
| R02 | 保留 | [提交 Endpoint](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/samples/enterprise-request/src/Full.NET.Modules.EnterpriseRequest/Features/SubmitForApproval/Endpoint.cs#L34) 仍只有登录授权；新增业务类型所有权注册没有补充提交权限和数据范围校验 |
| R03 | 保留 | [提交服务](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/samples/enterprise-request/src/Full.NET.Modules.EnterpriseRequest/Features/SubmitForApproval/SubmitEnterpriseRequestForApprovalService.cs) 仍先修改状态再调用流程启动，失败后的原入口不可重入 |
| R04 | 远程仍存在 | [Apply](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/CodeGenerationApplyService.cs#L214) 与 [Rollback](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/CodeGenerationRollbackService.cs#L60) 仍保留原边界；本地这些文件已与远程不同，本结论不能直接套用于本地正在进行的修复 |
| R05 | 保留 | [订单创建契约](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/Modules/Full.NET.Modules.Payments.Contracts/PaymentOrderContracts.cs) 与创建实现未补充调用方业务幂等键 |
| R06 | 保留 | [Identity 媒体服务](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/Modules/Full.NET.Modules.Identity/Features/SelfServiceProfile/SelfServiceProfileMediaService.cs#L306) 与 Pending 对账逻辑未变；本项仍限定于已确认的绑定清除链路 |
| R07 | 部分路径改善，原应用链路仍存在 | [原应用服务](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/Modules/Full.NET.Modules.DataApproval/Features/ProjectWorkflowOutcomes/DataApprovalRequestApplicationService.cs) 继续调用旧场景 Handler；新增业务命令执行器已有真实回执，见下文 |
| R08 | 设计建议保留 | [SerialNumbers 注册](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/Modules/Full.NET.Modules.SerialNumbers/SerialNumbersModule.cs#L33) 新增命令执行器，但必需依赖仍为 Identity 与 DataApproval，没有缩小最小装配闭包 |
| B01 | 保留 | [用户发布目标](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/BuildingBlocks/Full.NET.Realtime.SignalR/SignalRRealtimePublisher.cs) 和组名未变；Notifications 的实际私信投递路径仍不按作用域分组 |
| B02 | 保留 | [Hub 映射](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/BuildingBlocks/Full.NET.Realtime.SignalR/WebApplicationExtensions.cs#L29) 未启用过期关闭；会话撤销仍是客户端通知，无发现的服务端连接撤销闭环 |
| B03 | 保留 | [Schema 健康检查](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/BuildingBlocks/Full.NET.Data.Dapper/Health/DatabaseSchemaHealthCheck.cs) 仍仅判断 UUID 模式标记非空，新迁移数量增加并未改变该判定 |

R07 的范围需要明确修正：新增的 [SerialRuleApprovedCommandExecutor](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/Modules/Full.NET.Modules.SerialNumbers/Features/WorkflowCommands/SerialRuleApprovedCommandExecutor.cs) 将规则 CAS 与命令回执放在同一个本地事务，校验输入摘要，并提供取消墓碑和带版本保护的补偿。这是对原设计问题的实质改善，应保留。

但 [DataApprovalWorkflowOutcomeService](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/Modules/Full.NET.Modules.DataApproval/Features/ProjectWorkflowOutcomes/DataApprovalWorkflowOutcomeService.cs#L92) 仅在查到已应用命令回执时走直接结清，否则第 99 行仍调用原应用服务；人工重试与后台恢复也仍走原入口。因此不能把整个 R07 标为已修复。后续应让这些入口统一使用同一持久执行事实，或者在设计上明确消除无回执的应用路径。

### 7.4 R09 · P1 · Workflow 连接器后台执行未重新确认租户活跃状态

**证据链：**

1. [WorkflowConnectorHostedProcessor](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/Modules/Full.NET.Modules.Workflow/Execution/Connectors/WorkflowConnectorHostedProcessor.cs) 创建服务作用域后直接调用处理器，没有活跃租户准入。
2. [WorkflowConnectorSql.DispatchDue](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowConnectorSql.cs) 选择 active 实例及其 pending/retryable 调用，只查询 Workflow 自己的实例和步骤状态。
3. [WorkflowConnectorProcessor.DispatchAsync](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/Modules/Full.NET.Modules.Workflow/Execution/Connectors/WorkflowConnectorProcessor.cs#L47) 只检查实例作用域自洽及流程 active，第 48 行创建租户上下文，第 84 行发起外部 HTTP 调用。
4. [WorkflowExecutionTenantScope](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/Modules/Full.NET.Modules.Workflow/Execution/WorkflowExecutionTenantScope.cs#L16) 根据持久 TenantId 直接构造 `TenantContext`，不调用有效租户读取 Port。[连接器目录](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/Modules/Full.NET.Modules.Workflow/Execution/Connectors/WorkflowConnectorCatalog.cs) 校验的是部署允许的 scope 和版本，也不校验当前租户状态。
5. Tenancy 的禁用路径修改租户状态并写审计，未同步终止所有 Workflow 实例；相对地，[WorkflowBusinessCommandProcessor](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/Modules/Full.NET.Modules.Workflow/Execution/BusinessCommands/WorkflowBusinessCommandProcessor.cs#L44) 会先调用 `ResolveActiveByIdAsync`，说明不同后台入口的准入语义不一致。

**触发与影响：** 租户启用时生成连接器任务，之后管理员停用该租户；只要流程仍为 active、部署连接器配置仍允许其 scope，Worker 仍可领取待执行或待重试调用，以服务端配置的凭据向外部系统发起新的业务请求。持久 TenantId 能证明归属，不能证明当前仍获准执行业务。此问题不需要跨租户伪造，也不只是 UI 显示不一致。

**建议：** 在后台开始新业务副作用前，通过可信 `IActiveTenantContextResolver` 重新确认租户有效性，并使用返回的上下文。明确区分“禁止新的外部调用”和“仍须保存已发送调用的响应、进行恢复对账”，停用租户不能导致已发生效果的证据被丢弃。复用准入机制到连接器及异步汇合入口时，应避免把无效任务长期留在队列头而阻塞其他租户。

**针对性验收：** 排队后停用租户，Worker 不再发出首次请求或重试；重新启用后的推进行为明确；停用前已发出的响应仍可安全记账；有效租户和 Host 路径正常。分别验证持久状态及外部接收端请求数。该场景本轮未实测。

### 7.5 新增实现中应保留的设计

- **Workflow 外部调用分阶段持久化。** 领取及尝试记录与网络调用分离，网络调用不占 Workflow 数据库事务；固定激活 ID 用作外部幂等键，不支持幂等时把失联归为未知结果；连接器目录固定版本与摘要，受控网络连接禁用代理和重定向并校验实际连接地址。本轮未据此宣称外部系统一定支持幂等。
- **Messaging 重放具备持久操作边界。** [KafkaReplayService](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaReplayService.cs) 固定 Cluster/Topic 身份与分区窗口；[KafkaReplayOperationProgress](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/Modules/Full.NET.Modules.Messaging/Features/ReplayKafkaRange/KafkaReplayOperationProgress.cs) 记录处理前意图、确认后进度及未知结果，不能跨过拒绝位置后声称全部完成。
- **交付所有权回退保留持久保护。** [回退服务](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/Modules/Full.NET.Modules.Messaging/Features/RollbackDeliveryOwner/DeliveryRollbackService.cs) 与控制面检查分离数据库事务；记录回退代次、原资源身份及暂停意图，未确认旧发布者恢复时保留生产者保护。不要退化为修改一个配置开关后直接恢复写入。
- **Shadow 采集使用同一真实事件身份。** [DapperRoutedOutboxWriter](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperRoutedOutboxWriter.cs) 在启用受控采集时复用 Legacy 信封写独立影子源；数量、字节和时间窗口有界。它仍受业务事务约束，不能将首轮“Shadow 写入独立于该业务调用”的描述当作最新实现。
- **VForm3 有明确隔离及发布绑定。** [帧宿主](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/packages/admin-form-designer/src/VForm3DesignerHost.vue) 使用不带 `allow-same-origin` 的 sandbox；[消息客户端](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/packages/admin-form-designer/src/frame-client.ts) 核验来源窗口、opaque Origin、随机通道及运行产物摘要。[运行渲染器](https://github.com/yan041108/Full.NET/blob/ce2539cb0fb46bcd3b3801610bf4ba07147887a2/ui/admin/src/workflow/VForm3RuntimeRenderer.vue) 用装载代次拒绝旧任务回执，按字段策略形成 Patch；服务端发布绑定与字段授权仍是业务边界。这些静态防护不替代浏览器及实际发布产物验收。

### 7.6 本轮验证和后续优先级

实际完成了远程获取、快进可行性检查、固定提交源码审查、原 11 项意见复核、远程依赖图重算，以及本报告本地链接和固定提交链接的路径检查。未运行构建、测试、迁移、Kafka/Redis、浏览器或移动端；现有测试与提交说明不作为本轮通过证据。

优先处理顺序仍以隔离、授权和可靠性为先：将 R09 纳入 B01/B02/R02 同批风险评估；R07 复用新增命令回执能力收敛旧应用入口；R04 先对齐远程与本地整改状态，避免重复修复。实现时只运行受影响代码及必要边界的针对性测试，不自动扩大为全量验收。

## 8. 审查问题修复与针对性验证

### 8.1 交付范围与基线

用户在远程复审后要求修复。本轮以 `ce2539cb0fb46bcd3b3801610bf4ba07147887a2` 为唯一代码基线，创建 `codex/review-remediation` 开发分支，实际工作树为 `E:/wwwroot/github/Full.NET-review-remediation`。没有将原工作区数百项未提交变更带入本轮，也未覆盖、暂存、提交或合并原工作区。此目录是后续可审阅、提交的开发工作树，不是验收用整仓副本。

11 项代码路径问题均已实施修复；R08 保留为有真实消费者证据后再处理的组合设计建议。下表区分实现和已验证范围，不能用单元测试替代实际多节点、浏览器或发布验收。

### 8.2 修复映射

| 编号 | 实际修复 | 关键文件与验证边界 |
| --- | --- | --- |
| R01 | 正式 API 和基础项目模板共用 `UseFullNetApiPipeline`，集中可信代理、异常与请求体错误、认证、限流、授权及模块钩子的顺序。 | [共享管线](../../src/Composition/Full.NET.Composition/FullNetApiPipelineExtensions.cs)、[模板入口](../../templates/fullnet-app/src/FullNetAppNameToken.Host.Api/Program.cs)；架构测试核对两入口共用管线和代理先于消费者。未重新打包并运行生成模板。 |
| R02 | EnterpriseRequest 新增精确 submit 权限，并在服务中校验申请人身份和组织写入范围。 | [提交服务](../../samples/enterprise-request/src/Full.NET.Modules.EnterpriseRequest/Features/SubmitForApproval/SubmitEnterpriseRequestForApprovalService.cs)；单元验证非申请人和范围不足时无写入；双库 API 验证非申请人被拒绝。 |
| R03 | 在申请单本地事务中冻结工作流版本、更新提交状态并写入 Outbox；后台处理器用申请单及提交版本形成稳定请求键，验证可信租户上下文后启动工作流。 | [提交事件处理器](../../samples/enterprise-request/src/Full.NET.Modules.EnterpriseRequest/Features/SubmitForApproval/EnterpriseRequestSubmittedHandler.cs)；双库验证重复 HTTP 只记录一条意图、同一消息处理两次只产生一个实例。进程强杀和实际 Broker 重投未测。 |
| R04 | API 与 CLI 在首次业务写盘前合并后端、入口、Composition、Vue 和授权候选，并编译整个候选集合；一个完整检查点覆盖受管文件及手写文件原文，再复用存储的统一提交/失败恢复。完成摘要反映实际文件集合，失败 Apply 保留恢复身份；具有有效检查点且当前文件与该检查点一致的失败 Apply 可执行回滚。 | [整链规划](../../src/BuildingBlocks/Full.NET.Data.CodeGeneration/Integration/ModuleIntegrationFullPlanner.cs)、[候选编译](../../src/BuildingBlocks/Full.NET.Data.CodeGeneration/Integration/ModuleIntegrationFullCompilation.cs)、[检查点](../../src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationRollbackCheckpointStore.cs)、[回滚服务](../../src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/CodeGenerationRollbackService.cs)；验证迟到冲突不写盘、手写原文可恢复且不被回滚清单误认领、成功/失败 Apply 的回滚及提交边界。人工漂移、强杀留下的恢复材料仍失败关闭，不能宣称任意损坏均自动恢复。 |
| R05 | 支付创建请求必须携带稳定 UUID 幂等键；数据库约束租户加键唯一，记录请求摘要，同输入重试返回原订单，异输入冲突；并发唯一键冲突退出事务后重读。Vue 在一次创建对话框内复用键。 | [订单服务](../../src/Modules/Full.NET.Modules.Payments/Features/ManageOrders/PaymentOrderManagementService.cs)、迁移 322 双库脚本；单元验证不重复调用渠道和异输入拒绝，真实双库验证唯一约束及不同租户可复用键。未连接真实支付渠道，也未执行多进程竞态压测。 |
| R06 | 头像、签名、租户 Logo 替换或清除时，在本模块事务中写入持久释放事件；Files 消费者负责幂等释放，失败抛出以重试。清除与替换均使用读取版本进行并发校验。 | [释放契约](../../src/Modules/Full.NET.Modules.Files.Contracts/HostFileReferenceReleaseRequested.cs)、[释放消费者](../../src/Modules/Full.NET.Modules.Files/Features/HostFileReferenceClaims/HostFileReferenceReleaseRequestedHandler.cs)；单元覆盖释放失败后重试和上下文恢复；双库验证旧版本不能清除新绑定。 |
| R07 | 旧 DataApproval 应用入口改用已有场景命令处理器，以申请单 ID 作为稳定 CommandId，传递冻结载荷和原提交人，要求返回匹配命令的 applied 回执。 | [旧入口收敛](../../src/Modules/Full.NET.Modules.DataApproval/Features/ProjectWorkflowOutcomes/DataApprovalRequestApplicationService.cs)；复用远程基线已有的业务命令回执，不新增并行机制；相关申请应用和工作流结果单元测试通过。 |
| R08 | 保留设计建议，未进行无消费者证据的模块拆分或强依赖重构。 | 仍遵守强化型模块化单体、Contract Port 和明确预设的既有基线。 |
| R09 | 连接器首次产生外部副作用前，重新解析权威活动租户；停用时通过 revision CAS 延后待发调用，不访问外部系统；已发出的结果仍允许恢复收敛。 | [连接器处理器](../../src/Modules/Full.NET.Modules.Workflow/Execution/Connectors/WorkflowConnectorProcessor.cs)；单元验证停用租户无网络/事务副作用，双库验证延后 CAS、到期前不可领取和原有连接器流程。 |
| B01 | 用户业务组包含 Host/租户作用域；跨作用域账号控制组只通过专用会话撤销发布方法发送类型固定的控制消息。 | [组名](../../src/BuildingBlocks/Full.NET.Realtime.Abstractions/RealtimeGroups.cs)、[发布器](../../src/BuildingBlocks/Full.NET.Realtime.SignalR/SignalRRealtimePublisher.cs)；单元验证分组、作用域匹配与撤销路由。 |
| B02 | SignalR 开启认证到期关闭；每个 API 节点维护最长 30 秒的连接授权租约，周期性重新验证会话、用户、安全戳和活动租户。独立定时器确保扫描拥堵也不会无限续用旧授权。 | [连接租约](../../src/BuildingBlocks/Full.NET.Realtime.SignalR/RealtimeConnectionLeases.cs)、[权威鉴权](../../src/Modules/Full.NET.Modules.Identity/Security/RealtimeConnectionAuthorizer.cs)；可控时钟单元验证到期、撤销及迟到结果不能复活连接。未做真实 WebSocket 多实例、Redis 断连或 1 万连接容量测试。 |
| B03 | 应用嵌入当前发布迁移文件名，复用 Migrator 的模块清单解析；ready/startup 要求 UUID 契约和全部选定迁移记账齐备，API 不引入 DbUp 执行依赖。 | [发布要求](../../src/BuildingBlocks/Full.NET.Data.Abstractions/DatabaseSchemaRequirements.cs)、[健康检查](../../src/BuildingBlocks/Full.NET.Data.Dapper/Health/DatabaseSchemaHealthCheck.cs)；单元验证完整清单、预设与未知脚本；双库验证 UUID 正常但删除必需记账时 Unhealthy、恢复记账后 Healthy。 |

另补齐远程基线中 `WebhookFanoutBudget`、`DocumentAnonymousPreviewRequester`、`SerialRuleApprovalCommands`、`DataApprovalCommandReceipts` 的模板迁移归属，维持已有 Platform/Workflow 聚合映射；新迁移 322、323 同步登记。这是模板清单完整性修复，不代表实施 R08 的模块拆分建议。

### 8.3 数据与契约变化

- SQL Server/MySQL 成对新增 `322_PaymentOrderIdempotency.sql`：两个可空历史字段及租户/幂等键唯一索引；所有新创建请求要求非空键。SQL Server 按批次增加列再建索引，MySQL 按对象存在性支持迁移重入。
- 成对新增 `323_CodeGenerationFailureRecovery.sql`：允许失败 Apply 保留冻结摘要，同时接受既有无摘要失败记录。SQL Server 在事务内重建约束；MySQL 按对象状态重入。两者不改变失败 Preview/Rollback 的原有摘要形态。
- 更新支付请求的 OpenAPI、手写 TypeScript 契约和生成客户端；服务端仍采用 System.Text.Json 静态上下文。
- 新内部事件使用规范名称 `demo.enterprise_request.request.submitted` 和 `fullnet.files.reference.release_requested`，载荷使用 MemoryPack 生成。事件处理仍遵守至少一次和幂等边界。
- 部署须先运行 Migrator 完成 322、323，再使新版 API/Worker 通过就绪门禁；新支付客户端须发送固定的 `idempotencyKey`。本轮未向外部环境部署。

### 8.4 验证记录

验证环境：本机 Windows、.NET SDK 10.0.402、Node 24、pnpm 10.26、Docker 29.9；数据库使用本轮 Testcontainers 临时 SQL Server/MySQL。仅执行修改代码及必要边界的聚焦验证，没有运行全量测试。

| 验证批次 | 实际结果 | 范围 |
| --- | --- | --- |
| 后端只读自检 | 通过 | `doctor backend --json`，开发工作树环境与 SDK |
| 单元、集成、架构测试项目 Release 构建 | 通过，0 警告、0 错误 | 构建覆盖其引用的实际变更项目；不是 AOT 发布 |
| 首批聚焦单元 | 176/176 通过 | Realtime、DataApproval 应用与结果投影、支付外部副作用、生成回滚、迁移范围/Schema 要求、会话撤销与登录；与后续批次有重叠，不累加为独立测试数 |
| 新增回归与可选模块组合 | 24/24 通过 | 整链规划、Files 释放失败重试、示例提交授权/事件、连接器停用门禁、实时租约、Files 缺装配行为 |
| 最终代码生成及媒体边界回归 | 171/171 通过 | ApplyService、GenerationRollback、FullPlan、IntegrationCommitBoundary、AuthorizationCandidateCompilation、RunSql、OptionalFilesConsumer；包括失败 Apply 恢复及完成摘要匹配检查点 |
| 双库业务集成 | 6/6 通过 | `ReviewRemediationDatabaseTests`、EnterpriseRequest 已发布工作流提交、WorkflowConnectorApiTests，各 SQL Server/MySQL 一例 |
| 新增恢复约束的最终双库验证 | 2/2 通过 | 新建数据库从迁移起点执行到 323；复验 Schema 门禁、支付唯一身份、媒体版本 CAS，以及 running→failed 保留摘要且重复更新影响 0 行 |
| Host/模板共享管线架构验证 | 2/2 通过 | `TrustedProxyBoundaryTests`，含模板与正式入口一致性 |
| SQL 及模板结构检查 | 32/32 通过 | SQL 命名/注释/UUID、脚本归属和预设组合 |
| Vue 类型检查 | 通过 | `pnpm --filter @fullnet/admin typecheck` |
| 客户端生成和离线 OpenAPI | 通过 | `generate-fullnet-client.mjs --check` 零漂移；`snapshot-client-openapi.mjs --check --offline` 通过；不是运行中 API 的 OpenAPI 抓取 |
| 文件与报告检查 | 通过 | `git diff --check`、审查文档本地相对链接存在性、当前分支和状态检查 |

最终聚焦命令保留如下，便于复验：

```powershell
# 已构建的测试程序使用 Microsoft.Testing.Platform；这里只列本轮最终过滤器。
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter 'FullyQualifiedName~CodeGenerationApplyServiceTests|FullyQualifiedName~GenerationRollback|FullyQualifiedName~ModuleIntegrationFullPlanTests|FullyQualifiedName~IntegrationCommitBoundaryTests|FullyQualifiedName~AuthorizationCandidateCompilationTests|FullyQualifiedName~CodeGenerationRunSqlTests|FullyQualifiedName~OptionalFilesConsumerTests' --minimum-expected-tests 170

$env:FULLNET_TESTCONTAINERS_REUSE='0'
$env:FULLNET_TESTCONTAINERS_BOUNDED='0'
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --filter 'FullyQualifiedName~ReviewRemediationDatabaseTests' --minimum-expected-tests 2

node --test tests/naming/sql-naming.test.mjs tests/naming/sql-comments.test.mjs tests/naming/uuid-storage-sql-governance.test.mjs tests/templates/migration-script-modules.test.mjs tests/templates/project-preset-composition.test.mjs
```

测试期间发现并修正了事件名称不满足规范、模板迁移归属遗漏、整链完成摘要与预览摘要混用，以及新增集成测试的内部访问声明；以修正后的上述结果为准。初次容器运行曾因未使用外层控制器却启用 BOUNDED 模式而在初始化失败，随后关闭该模式、禁用容器复用后完成真实双库验证。没有将未执行、失败或跳过的用例计为通过。

### 8.5 仍未验证的能力

未执行 Native AOT 发布/裁剪验收、真实支付渠道调用、浏览器端到端、移动客户端、多节点 SignalR/Redis/Kafka 故障演练或容量压测。保持 `Capacity-not-verified`；本次测试不能证明 1 万同时在途、生产 SLO 或所有故障窗口都已覆盖。未将修复分支与原工作区未提交变更合并，因此也不宣称组合后的版本已验收。

## 9. 提交、合并与原工作区改动保全（2026-10-27）

### 9.1 Git 结果与保全依据

- 修复提交：`77af180c8b057c01c7cd4956f9cfd20ad31bf70d`，96 个文件，包含第 8 节修复与审查文档。
- 原工作区 `E:/wwwroot/github/Full.NET` 的 `main` 从 `a77902afd87af1662af3b43206d85160eeec566b` 快进至该修复提交，同时包含已复审的远程基线 `ce2539cb0fb46bcd3b3801610bf4ba07147887a2`。未推送远程。
- 合并前创建任务快照 `merge-review-remediation-20261027`，并以包含未跟踪文件的 Git stash 保存原工作区。固定备份对象为 `18c62326d6b64a43b765a04aa798c995f3c643c5`，引用为 `refs/codex/safety/merge-review-remediation-20261027`；备份保留，不因合并成功自动删除。
- 合并前原有 692 个实际变更路径（包括目录内文件展开），其中 3 个是原本已删除的文件，继续保留删除状态。按快照逐路径比对，646 个文件内容保持一致，3 个删除保持一致，43 个路径经过语义融合（包括恢复调用参数适配）；未发现非预期变更。未将这些原有功能改动一起提交。新增 HTTP 错误管线适配、对应测试、模板迁移归属和本验证记录作为独立补充修复提交；本地新代码生成恢复调用等融合内容仍随原有未提交功能保留。
- `stash apply` 出现 16 个文本冲突，另外 4 个原未跟踪文件与修复中的新增路径重叠。全部保留了双侧 Git 对象，处理后没有未合并索引项或冲突标记。

### 9.2 冲突处理

| 范围 | 处理结果 |
| --- | --- |
| CodeGeneration | 保留原工作区更完整的冻结计划、工作区所有权、日志恢复、下载归档、Git 发布及测试实现；整链计划和手写原文恢复的修复目标由该模型承担，不退回旧阶段编排。CLI 的统一提交入口保留。 |
| 检查点 | 保留本地只存变化文件、未变文件以摘要验证的机制，避免把旧实现的完整内容数断言或重复读取循环混入新格式。 |
| 运行摘要 | 新模型在插入运行时已保存完整冻结摘要；恢复调用使用冻结记录提供完成 SQL 所需的产物数量和摘要，确保独立修复与本地日志模型的参数边界一致。失败 Apply 继续保留恢复摘要。 |
| Host 管线 | 正式 API 与模板继续共用入口，同时保留原工作区业务 API 空错误响应的 ProblemDetails 行为；普通 API 401/403/404 有错误体，OIDC 协议端点不接管。 |
| Files / Organization / Vite | 分别保留两个释放事件消费者、两类测试友元、VForm3 插件及本地 HTTPS 配置，不能简单二选一。 |
| OpenAPI 与生成客户端 | 按 operationId、路径、Schema 做三方语义合并，保留本地 Document/CodeGeneration/Identity 和远程 Workflow/Messaging 变化，再规范化并重新生成客户端。 |
| SQL 注释与模板 | 合并双侧对象注释覆盖表后重新生成目录；为原有新增 Document 生命周期和 CodeGeneration 归档/来源迁移补齐 Platform 预设归属。 |
| 测试矩阵 | 保留双侧选择器及分片排除条件；相同编号的不同迁移保留各自验证器，不丢弃任何一侧场景。没有执行全量矩阵。 |

### 9.3 组合验证及限制

本节仅对合并后的原工作区输入有效，不能用第 8 节独立工作树的通过结果替代。

- 后端 Unit、Integration、Architecture 项目 Release 构建通过，0 警告、0 错误；Vue 管理端类型检查通过。
- 模板/迁移归属/预设组合及 SQL 注释检查：16/16 通过。
- 规范化后的 OpenAPI 离线快照通过，客户端重新生成后无漂移。
- 聚焦行为验证：82/82 单元测试通过，覆盖 Apply/检查点/回滚、真实 CLI 整链提交与恢复、HTTP 错误契约和可选 Files 组合；Host/模板管线架构测试 2/2 通过。SQL Server/MySQL 的运行终态失败后恢复及审查修复数据库场景共 4/4 通过；恢复参数最终适配后另重跑原 82 项中的 2 项 Apply 场景，2/2 通过，不重复累加。
- SQL 命名检查未全通过：4 项来自原有 `301_IdentityOidcMfaCounter.sql` 的两条动态 SQL，以及 `303_IdentityOidcKeyGovernance.sql` 的两个未显式命名主键。已按 Git 备份核对这两份 MySQL 文件与合并前一致；本次保留原功能改动，不将这些既有问题报告为通过。
- 构建开始曾遇到旧 `obj` 中含 NUL 的生成文件和未恢复的新依赖；只删除已确认损坏的生成文件并恢复依赖后，构建通过。没有通过降低编译规则绕过失败。初次双库运行因 Docker 未启动而在初始化失败；启动本机引擎后，以相同 4 项过滤器重跑全部通过。

未运行全量、Native AOT 发布或容量验证；仍为 `Capacity-not-verified`。本次合并不代表原工作区其他尚未完成的功能已整体验收。

## 10. 剩余 MySQL 迁移命名问题整改（2026-10-27）

用户要求修复第 9 节保留的 4 项 MySQL 迁移命名违规、验证后提交。本轮基线为 `main` 的 `46d9233d705d70c4cf2233911cbda182c1313491`，任务快照为 `mysql-migration-naming-20261027`。

- `301_IdentityOidcMfaCounter.sql`：移除动态 `PREPARE/EXECUTE`，改用无参静态条件过程，只在列不存在时添加 `LastAcceptedCounter`，调用完成后清理过程；重入保留已有 TOTP 消费步长，不新增命名债务豁免。
- `303_IdentityOidcKeyGovernance.sql`：为两张签名治理表显式声明 `PK_fn_identity_oidc_signing_key` 和 `PK_fn_identity_oidc_signing_node`，保留原有 UUID 存储、唯一键及治理状态语义。
- 301/303 此前均为未跟踪迁移，本次将其及已具备规范主键的 SQL Server 对应脚本成对纳入提交；同步提交这三处表/列的中文注释目录和覆盖映射，其他未提交目录内容不混入本次提交。
- 新增 `IdentityMfaSigningMigrationNamingTests`，只按完整名称加载 301/303，使用不记账执行器验证实际 DDL；覆盖首次建列/表、签名首表已存在但第二张表缺失、重复执行保持 TOTP 步长/退役密钥版本/节点策略，以及主键和 KeyId 唯一约束有效、无残留 MySQL 恢复过程。
- 原工作区未提交的 `IdentityOidcRemediationMigrationTests` 同步适配正式执行器及完整文件名，避免对 `DELIMITER` 原文直接执行，也避免同编号模块脚本误选；该既有功能测试继续随原未提交改动保留。测试矩阵仅增加本轮 2 个新集成场景及 301/303 的精确过滤器。

验证：

- `node --test tests/naming/sql-naming.test.mjs tests/naming/sql-comments.test.mjs tests/naming/uuid-storage-sql-governance.test.mjs`：24/24 通过。
- `validateRepositorySqlNaming`：实际扫描结果为 0 项违规。
- Integration Release 构建：通过，0 警告、0 错误。
- 双库聚焦测试：4/4 通过（SQL Server/MySQL 各 2 项），包括新迁移回归与原有 BFF/TOTP/退出/密钥状态重入调用；0 失败、0 跳过。过滤器为 `FullyQualifiedName~IdentityMfaSigningMigrationNamingTests|FullyQualifiedName~IdentityOidcRemediationMigrationTests.Replayed_browser_mfa_and_logout_schema_preserves_security_state`。

本轮只验证这些迁移及直接受影响的重入调用，不代表全量测试或 Native AOT 验证。其余原有未提交改动继续保留，未推送远程。
