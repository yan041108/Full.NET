# Full.NET 全项目架构与代码审查

- 日期：2026-09-07；状态：**审查记录，尚未整改，不代表 Verified 或可发布**。
- 输入：用户授权全项目审查；根 AGENTS、项目规则、总体架构 Spec、ADR-0002 及模块/性能 Skills。
- 源码基线：`63b8dda230d4a9027b59ff37cbc0d2213d885753`，分支 `main`；任务快照 `full-project-review-20260907`。
- 审查期间 HEAD 前进到 `1af03bc0b2cf5ee10b47129ffd59ebc797e73272`；两者已核对仅新增此前的 Admin.NET 对标评估文档，源码未变。
- 授权执行的是审查。本次只新增审查资料，未实施业务修复、迁移、提交或推送。

## 1. 结论与判断依据

**强化型模块化单体的总体方向合理，当前主要问题是边界执行和功能扩展后的整合质量。** 建议保留 API、Worker、Migrator 分角色运行以及模块主项目内的垂直切片，不按菜单、实体、行数机械增加项目，也没有足够证据支持全面微服务化。

本次按根因归并为 **18 组发现**，其中 R08 含 5 个通知子项。优先修复默认装配、租户作用域、支付一致性、HTML 注入和发布构建。测试失败数不是独立缺陷数：例如多个权限测试在构建 Endpoint 元数据时就被同一 DI 错误阻断，不能据此说所有权限均失效。

物理项目依赖整体保持边界：28 个模块共 43 个项目，跨模块 ProjectReference 均指向 Contracts，未发现直接引用另一模块实现项目。但是，引用 Contracts 并不自动保证语义隔离：租户流程调用 Host-only 文件合同、业务事务调用其他模块以及装配依赖漏声明，都发生在接口边界之后。

| 审查维度 | 判断 | 依据与动作 |
| --- | --- | --- |
| 架构选型 | 保留模块化单体 | 当前故障由代码、合同和装配产生，网络拆分不能解决 |
| 运行角色 | 保留 API/Worker/Migrator 分离 | 部分长任务仍在 HTTP 请求内执行，需落实 Worker 恢复语义 |
| 分层 | 结构基本成立，边界执行有缺口 | Reporting 直引数据库提供程序；业务服务持事务做远程调用 |
| 模块划分 | 大部分可保留，重点澄清职责和合同 | Files、Reporting/GoView、Workflow/DataApproval/SerialNumbers 等见覆盖表 |
| 代码规范 | 未达到仓库既定门禁 | 命名、注释、契约目录和新增代码 AOT 路径均有失败 |
| 性能 | 存在明确静态风险，尚无实测瓶颈结论 | 流式字符串重复复制、长事务、批次租约与任务恢复；未做容量压测 |
| 发布质量 | 当前基线不能据本次结果判定可发布 | 架构、单元、Vue、AOT、命名和 OpenAPI 检查均有失败 |

## 2. 范围、方法和覆盖边界

清点了 Git 跟踪的 76 个 `.csproj`、2,997 个 C# 文件；模块范围为 28 个模块、43 个项目、1,490 个 C# 文件。数字是仓库清点，不是逐文件人工审查数量。全局扫描项目引用、模块注册、SQL 作用域、跨模块调用、事务、异步/同步阻塞、危险 HTML 和客户端合同；再对支付、通知、文件、导入导出、报表、AI 等调用链做深读与隔离探针。

- [逐模块覆盖表](G:/wwwroot/github_fork/Full.NET/docs/verification/2026-09-07-full-project-review/coverage.csv)：全部 28 个模块，含具体抽查范围、发现编号、边界建议和未验证项。
- [清点及项目引用数据](G:/wwwroot/github_fork/Full.NET/docs/verification/2026-09-07-full-project-review/inventory.json)。
- [跨模块 Contracts 引用图](G:/wwwroot/github_fork/Full.NET/docs/verification/2026-09-07-full-project-review/contract-dependencies.mmd)：箭头表示项目依赖，**不等于运行时模块装配图**；因此不能把双向 Contracts 关系直接认定为运行时循环依赖。

| 非模块范围 | 实际审查 | 边界 |
| --- | --- | --- |
| BuildingBlocks/Composition/Hosts | SQL ScopeGuard、事务提交/回滚、模块目录、注册与宿主测试 | 未启动生产配置或连接生产服务 |
| Vue 与共享 packages | 类型构建、OpenAPI 合同测试；附件和打印预览链深读 | vue-tsc 失败，Vite 未产出新包；未运行浏览器 E2E |
| uni-app/Flutter | 客户端结构、HTTP/身份会话实现抽查 | 未执行移动端构建、真机、平台权限验收 |
| Layui | 存量目录纳入清点并核对冻结边界 | 未扩展、修改或宣称完成历史页面逐页审查 |
| SQL/生成器 | 双提供程序文件与 UUID/注释/SQL 安全治理；OpenAPI 生成契约 | 未运行 SQL Server/MySQL 实例及历史升级演练 |
| CI/部署/性能治理 | Helm、发布顺序、镜像合同、性能治理静态检查 | 未运行 Kubernetes、Kafka/CDC、Linux Native AOT 或容量认证 |
| 依赖与许可 | 项目依赖边界清点 | 本次未执行新鲜漏洞公告或完整第三方许可证审计 |

证据分为“本地复现”“源码确认”“静态风险”“待运行验证”。没有问题编号的模块只表示本次范围内没有足够证据提出独立发现，不表示无缺陷。

## 3. 发现、影响与整改验收

优先级：P1 为正常功能阻断、安全/资金一致性缺陷或发布构建阻断；P2 为需要整改的正确性、恢复、性能或维护问题。所有修改建议均尚未实施。

### R01 · P1 · 钉钉审批同步路由与条件注册不一致

**证据：本地架构测试复现 + 源码确认。** [NotificationsModule.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Notifications/NotificationsModule.cs:162) 无条件映射审批同步路由；同文件仅在 DingTalk 与 Workflow 开关同时开启时注册 Service/CallbackVerifier。路由参数无法从 DI 推断，产生 `Failure to infer one or more parameters`。多个授权/OpenAPI 测试在路由构建阶段失败。

影响：启用 Notifications、但没有启用该可选提供程序的模块组合会在 Endpoint 元数据生成时失败。具体生产启动/首次路由初始化阶段还取决于 Host 初始化方式，不能把这些失败均归因于权限逻辑。

整改：路由与服务使用一致的能力条件，或者注册明确拒绝未启用能力的处理器；不要为了默认启动强迫配置外部钉钉凭据。验收默认配置、开关组合、禁用模块、授权和 OpenAPI 元数据生成。

### R02 · P1 · 导入与报表任务 SQL 的租户声明缺少绑定和过滤

**证据：架构测试 + 实际 SqlScopeGuard 隔离探针复现。** [ImportExportTaskSql.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.ImportExport/Persistence/ImportExportTaskSql.cs:32) 和 [ReportingExportTaskSql.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Reporting/Persistence/ReportingExportTaskSql.cs:26) 等共 17 条声明使用 `TenantRequired`，却未声明所需的当前租户绑定；查询/更新还存在缺少对应租户谓词的问题。两个 FindById 均抛出 `TenantScopeViolationException`，尚未进入数据库。

影响：正常租户任务的创建、查询、完成等路径被守卫拒绝；ImportExport 的 Worker 还在建立具体租户上下文之前领取 TenantRequired 任务。

整改：同时修正 binding、SQL 参数与 TenantId 过滤；将跨租户调度领取与领取后租户执行设计为明确的两段。禁止改成 Global 或关闭守卫来“恢复可用”。验收所有声明、A/B 租户交叉访问和双库领取/完成流程。

### R03 · P1 · 租户任务复用了 Host-only 文件合同

**证据：源码调用链 + 守卫探针复现。** [ReportingExportTaskManagementService.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Reporting/Features/ManageExportTasks/ReportingExportTaskManagementService.cs:38) 要求租户上下文，但后续调用 `IHostFileUploadWriter` 和 `IHostFileContentReader`。[ImportExportTaskRunner.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.ImportExport/ImportTasks/ImportExportTaskRunner.cs:51) 同样切入租户后读取 Host 文件。合同实现落到 [HostFileSql.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Files/Persistence/HostFileSql.cs:113) 的 HostOnly SQL；探针得到 `HostContextRequiredException`。

影响：即使修好 R02，导入读文件、报表存储或下载文件仍会遇到作用域阻断。现状是失败关闭，不能把它表述为已发生跨租户泄漏。

整改：由 Files 提供明确的租户/所有者资源合同，校验授权、文件归属和引用生命周期；不要在调用点临时切换为 Host 绕过检查。验收租户导入/导出完整链及跨租户拒绝。

### R04 · P1 · 支付订单号和退款号截取 UUID v7 时间部分导致重复

**证据：调用实际私有方法的隔离探针复现。** [PaymentOrderManagementService.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Payments/Features/ManageOrders/PaymentOrderManagementService.cs:257) 和 [PaymentRefundManagementService.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Payments/Features/ManageRefunds/PaymentRefundManagementService.cs:201) 拼接秒级时间及 `Guid.ToString("N")[..8]`。本项目使用 UUID v7，该前缀不能提供所需的随机区分能力。探针对同一时间生成两个不同 UUID，两种业务编号均完全相同。

影响：正常并发创建可能发生唯一键冲突，导致下单或退款失败。此结论不依赖真实支付网关。

整改：采用完整且满足长度约束的唯一标识方案，保证商户请求幂等；数据库保留唯一约束。验收固定时钟、多 ID、同秒并发以及双库冲突行为，不能只测试顺序生成的单笔订单。

### R05 · P1 · 外部支付/退款调用放在本地事务内

**证据：源码确认，未对真实支付系统发请求。** [PaymentOrderManagementService.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Payments/Features/ManageOrders/PaymentOrderManagementService.cs:35) 包裹本地插入与 `InvokeProviderAsync`；退款服务也在事务回调内插入记录并调用提供程序。K3Cloud 创建/重试、OCR 识别有相同的远程工作与事务混合模式。

影响：外部操作成功后本地提交仍可能失败或取消；数据库回滚无法撤销外部操作，重试可能丢失关联或重复产生副作用。网络耗时还延长连接和锁占用。

整改：先持久化稳定的操作意图和幂等键，以短事务推进状态，事务外调用提供程序，持久化结果并处理未知状态、补偿和对账。可靠业务事件按既有 Outbox 规则处理。验收外部成功/本地失败、超时未知、重复请求、进程退出和退款并发；不能通过简单吞异常解决。

### R06 · P1 · 支付回调未绑定订单所属商户

**证据：源码确认。** [PaymentWeChatNotifyService.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Payments/Features/ReceiveWeChatNotify/PaymentWeChatNotifyService.cs:176) 前验证的是 URL 选中商户的通知；按 OutTradeNo 找到订单后只比较金额等字段，没有比较 `order.MerchantConfigId` 与该商户配置，随后更新订单状态。

影响：在多商户配置下，某商户的合法签名通知若带有另一个商户订单号及相同金额，可关联到不属于它的订单。前提是攻击者能够取得对应商户的合法通知，不能描述成任意未签名请求可伪造付款。该外部场景未实测。

整改：回调验签身份、订单商户、订单租户/渠道、金额和币种绑定为同一不变量；状态转换使用允许的单向转换表。当前仅排除 `Succeeded` 的状态更新也需补退款后迟到通知回归。验收双商户交叉回调、迟到/重复通知以及错误收据重试。

### R07 · P1 · 打印布局的脚本过滤不足以阻止存储型 XSS

**证据：实际渲染函数探针 + Vue 消费链。** [PrintingHtmlRenderer.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Printing/Domain/PrintingHtmlRenderer.cs:22) 只移除 script 标签；保存过滤使用同一策略。`<img src=x onerror="...">` 经 Render 后仍保留 onerror。[PrintingPreviewView.vue](G:/wwwroot/github_fork/Full.NET/ui/admin/src/views/PrintingPreviewView.vue:197) 将返回 HTML 插入管理端页面。

影响：有模板编辑/发布权限的人可植入事件属性，其他有预览权限的用户打开布局时存在同源脚本执行路径。只给绑定字段 HTML 编码不能保护布局本体。浏览器实际执行和生产 CSP 配置未在本次运行验证。

整改：使用可审计的 HTML/属性/URL 白名单净化，或隔离且受限的预览 sandbox，并保持上下文正确的字段编码。验收事件属性、SVG、危险 URL、保存后重载与另一个账号预览，不能继续扩充单条正则充当完整净化器。

### R08 · P1/P2 · 通知的凭据、验证码和投递幂等仍存在缺陷

本次源码仍保留此前审查关注的实现模式；以下按当前证据列出，不把历史修复或测试结果当作当前基线已通过。

| 子项 | 证据、触发与影响 | 建议及验收 |
| --- | --- | --- |
| R08a P1 环境凭据引用无登记边界 | [INotificationSecretResolver.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Notifications/Providers/Smtp/INotificationSecretResolver.cs:33) 直接读取引用中的变量名。隔离探针设置自有随机环境变量，未登记的 env:// 引用成功解析；未读取真实秘密。有提供程序配置能力的用户可能把进程级秘密用于其可控制的外发配置 | 建立提供程序专用的明确引用登记/允许集合；不能仅凭前缀认定授权。验收未知引用拒绝及跨提供程序限制 |
| R08b P1 验证失败计数被回滚 | [RecipientEndpointVerificationService.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Notifications/Features/VerifyRecipientEndpoints/RecipientEndpointVerificationService.cs:45) 包装 Verify；错误码路径先 IncrementAttempt，再返回 Failure。[DapperCommandTransaction.cs](G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Data.Dapper/DapperCommandTransaction.cs:33) 对失败结果回滚，因此失败计数和达到上限后的状态不能可靠保留 | 失败计数作为需要提交的安全状态处理；错误响应与事务提交决策分开。用真实事务验证连续错误达到上限 |
| R08c P2 验证码外发处于事务内 | 同服务 SendCodeAsync 的事务回调调用 SMS/邮件 Sender，外部发送与本地提交不能原子完成 | 稳定发送意图和幂等键、事务外发送；验证发送成功后提交失败、重试和冷却限制 |
| R08d P1 批次等待耗尽租约后仍发送 | [NotificationDeliveryBatchProcessor.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Notifications/Execution/NotificationDeliveryBatchProcessor.cs:50) 一次领取整批后顺序执行；发送前没有重新确认/续展当前投递所有权。前项慢调用耗尽后项租约时，另一 Worker 可重领，旧 Worker 仍外发；完成更新的 fencing 不能撤回已发生的发送 | 发送前确认有效所有权，采用可续约执行或匹配预算的小批领取；测试慢首项、双 Worker 和租约过期 |
| R08e P2 模板更新破坏原请求幂等重放 | [NotificationIntentService.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Notifications/Features/CreateNotificationIntents/NotificationIntentService.cs:81) 先使用当前模板准备请求；ReplayOrConflict 比较 prepared.Version.Id 与旧快照。发布新版本后，相同原请求和键可能变冲突；准备阶段失败还可能遮蔽原有结果 | 先定位已持久化意图，按首次接收快照校验重放；测试模板升级/停用后的原请求重放 |

R08b–e 本次证据为当前调用链和事务实现复核，未把这些场景的旧测试当作本次新鲜回归通过记录。整改应补回对应行为测试并确认保留在最终提交中。

### R09 · P1 · AI 租户聊天读取 HostOnly 配置和配额 SQL

**证据：源码 + 守卫探针。** 会话创建在租户作用域下读取 [AiModelConfigSql.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Ai/Persistence/AiModelConfigSql.cs:39)；流式服务调用 [AiChatQuotaGuard.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Ai/Streaming/AiChatQuotaGuard.cs:24)。两类 SQL 均标记 HostOnly；探针在租户上下文调用分别抛出 `HostContextRequiredException`。

影响：合法租户聊天在调用模型前就被拒绝。整改需区分 Host 配置管理与租户可读的最小投影，并维护租户配额隔离；不应把整条租户请求切换到 Host。验收 Host 管理、租户使用、停用配置和隔离。

### R10 · P2 · AI 配额不是原子门禁，Token 限额未执行

**证据：源码确认，受 R09 阻断的完整租户流程尚未实测。** [AiChatQuotaGuard.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Ai/Streaming/AiChatQuotaGuard.cs:52) 只检查请求额度，未检查已配置的 MonthlyTokenLimit；开始时读计数，完成后才累计，多个并发请求可同时通过剩余额度检查。取消或异常完成路径也不执行正常完成的用量记录。

整改：明确请求数、Token 和取消请求的计费语义，使用原子预留/结算并处理跨月与未知用量。验收并发争抢最后额度、Token 限额、取消和跨月，不应仅靠最终累加 SQL 原子性声称门禁正确。

### R11 · P2 · AI 生成状态和取消依赖进程本地状态

**证据：源码确认。** [AiChatStreamService.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Ai/Features/ManageChatSessions/AiChatStreamService.cs:112) 在 try/finally 之前设置 IsGenerating 并启动响应；失败可能留下生成中状态。先读取再写入生成标记缺少原子的抢占条件。[AiChatGenerationRegistry.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Ai/Streaming/AiChatGenerationRegistry.cs:17) 只在当前进程保存取消源，旧请求 finally 按 sessionId 移除注册项还可能移除新请求的注册；取消接口忽略 TryCancel 返回值。

影响：同会话并发、请求准备失败、进程重启或多实例负载均衡下存在卡住、错误取消和虚假取消成功风险。

整改：按 generationId 进行数据库 CAS/租约和有所有权的清理；跨实例取消采用明确路由或共享控制状态。验收准备阶段异常、两次生成交错结束、进程重启及取消请求落到其他实例。

### R12 · P2 · MQTT 幂等比较只核对消息长度

**证据：实际私有比较方法探针复现。** [MqttMessagePublishService.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Mqtt/Features/ManageControlPlane/MqttMessagePublishService.cs:204) 比较 Topic、QoS、ClientId 和 PayloadSizeBytes，未比较正文或摘要。固定相同键，原消息 `on` 与新消息 `no` 被判定为同一次重放。

整改：持久化覆盖正文和语义参数的规范化摘要，在唯一幂等键约束下比较；相同键不同内容返回冲突。验收同长度不同内容、Unicode 字节长度、重试和并发，不必连接真实 Broker 即可建立核心回归。

### R13 · P1 · 新增 MySQL UUID 列偏离 BINARY(16) 存储合同

**证据：新鲜命名/UUID 治理失败 + SQL 声明。** [165_DocumentVersionDeletionAudit.sql](G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/165_DocumentVersionDeletionAudit.sql:5) 及新增 Printing、Ai、Payments、GoView、K3Cloud、Ocr 等 MySQL 迁移仍出现 UUID char(36)，与既定 010+ BINARY(16) 合同和 Guid 转换边界不一致。

影响：双库迁移和读写兼容性未满足正式约束。当前只确认声明与治理失败；尚未在 MySQL 实例复现具体插入异常、索引或字节序后果。

整改：逐列核对 ID、引用、参数和物化边界，并区分已应用迁移与未发布迁移；对已应用历史设计向前迁移，不能盲目改写历史。验收双库空库安装、升级、Guid 往返和关联数据。

### R14 · P1 · Host.Api AOT 分析构建失败

**证据：`pnpm test:aot:analyzers` 失败，构建输出 214 个错误、0 个警告。** 包括 Calendar 等物化器使用不可访问的 AotDataReaderExtensions、Identity 参数集合调用不支持的 AddWithValue，以及 Identity OIDC、AI SSE/模型响应中的动态 JSON 路径触发 IL2026/IL3050。

影响：普通 Release 编译通过不能证明 Native AOT 可达闭包可编译。不能以忽略告警或宽泛动态依赖声明代替实现修正。

整改：统一受支持的 AOT 物化/参数 API，补闭合 JSON 元数据与静态绑定；先通过分析构建，再核对 Linux publish 与原生进程必需门禁。本次未执行后两项。失败脚本遗留的条件依赖图已用普通 Host.Api restore 恢复。

### R15 · P1 · Vue 构建失败，工作流附件调用与实际 HTTP 客户端不匹配

**证据：`pnpm --filter @fullnet/admin build` 失败，日志有 440 条 TS 错误诊断。** 不全是生产逻辑错误，也包括陈旧测试 DTO/夹具；但 [workflow-form-attachments.ts](G:/wwwroot/github_fork/Full.NET/ui/admin/src/workflow/workflow-form-attachments.ts:9) 确实把 AbortSignal 传入 folderId 参数，下载还把 Axios 风格对象传给要求 path 的 request，并读取不存在的 response.data。

影响：类型构建阻断；工作流附件功能也存在独立运行合同错误。Vite 未执行，没有新鲜包体数据。

整改：正确传递 upload 参数并调用共享 requestBlob；逐类修正生产代码、测试夹具、生成模型/manifest 的漂移。验收上传取消、实例上下文附件下载、权限拒绝和 Vue 构建，避免通过 any 或排除测试文件掩盖错误。

### R16 · P2 · AI SSE 累积响应重复复制，导出内存限制晚于分配

**证据：静态性能风险，未实测吞吐/分配。** [AiChatStreamService.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Ai/Features/ManageChatSessions/AiChatStreamService.cs:164) 对每个增量重新连接完整字符串，而 [AiChatCompletionStreamer.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Ai/Streaming/AiChatCompletionStreamer.cs:100) 同时保留另一份累计内容。细碎长响应的累计复制量随总长度近似二次增长；在读取流循环处未发现总响应字符/字节上限。

报表导出先收集行、渲染为完整 workbookBytes，再检查最大字节数。已有行数限制能限制一部分风险，但输出大小判断不能阻止此前峰值分配。

整改：单一可增长缓冲与明确输出预算，流式或分段导出，并在昂贵分配前限制输入/输出规模。验收不同总长度和分片大小的分配、CPU、峰值内存、取消延迟；本次不提供未经测量的 QPS/P99/容量收益。

### R17 · P2 · 导入/报表任务缺少完整的崩溃恢复所有权

**证据：源码确认；运行故障注入未执行，且路径先受 R02/R03 阻断。** [ImportExportTaskSql.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.ImportExport/Persistence/ImportExportTaskSql.cs:91) 领取 queued 后进入执行状态，未见租约过期或 Worker 崩溃后的重新领取条件。[ReportingExportTaskManagementService.cs](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Reporting/Features/ManageExportTasks/ReportingExportTaskManagementService.cs:88) 持久化 Processing 后仍在请求内收集、渲染、上传，显式业务失败会记录失败，但异常、取消、进程退出没有等效持久化恢复协议。

整改：明确 queued/running/succeeded/failed/unknown 状态、owner、generation、lease、checkpoint 与幂等；长导出由可恢复 Worker 执行。验收领取后崩溃、部分批次成功、上传成功后退出、用户取消与重复完成。

### R18 · P2 · 模块依赖、分层和治理清单未随扩展同步

**证据：项目扫描、架构/治理/OpenAPI 测试。** [Full.NET.Modules.Reporting.csproj](G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Reporting/Full.NET.Modules.Reporting.csproj:10) 将数据库提供程序引入业务模块，违背自有数据访问边界；架构扫描还命中 Document、OCR、Payments、AI 的跨模块事务调用。装配声明缺少 Identity→Files、Organization→ImportExport、Tenancy→Files 等依赖，预设组合与扩展后的 Notifications 依赖不一致。

现有官方程序集、授权、物化器和模块图清单存在过期项；ADR-0002 部分模块枚举与旧 Tenancy.Http 拓扑没有同步到当前布局。多个新增 API 适配器缺少 manifest，错误码/权限命名以及 XML param 注释也有漂移。纯格式和注释问题不与资金/租户问题等量排序。

整改：以实际装配目录和经确认的合同为依据更新最小依赖；把提供程序执行适配放回自有基础设施边界；修正具体跨模块事务，按真实边界更新生成清单和测试，保留门禁。Jobs 两项测试的 UnexpectedCommandExecutor 未接受新增 finalize 命令是夹具缺口，不据此认定 Worker 生产逻辑错误。SQL 扫描中 `importexport`/`import_export`、`payments`/`payment` 的所有权名称差异也应先核对映射，不能直接宣称跨模块偷读表。

## 4. 模块边界的具体调整建议

| 边界 | 建议 | 拆分条件 |
| --- | --- | --- |
| Identity / Tenancy / Organization | 分别拥有身份授权、租户生命周期、组织关系；补最小读取合同和显式依赖 | 当前没有证据要求进一步拆项目 |
| Files / Document / Printing | Files 管文件归属与引用；Document 管文档版本；Printing 管布局及渲染 | Host 与租户文件合同应显式区分；避免共享表或跨模块本地事务 |
| Workflow / DataApproval / SerialNumbers | Workflow 管流程执行，DataApproval 管审批应用编排，SerialNumbers 管业务序号及结果应用 | 先修清端口方向与消费者，不据双向 Contracts 图直接拆服务 |
| Notifications | 通知意图、模板、收件人及投递状态留在模块；钉钉审批同步视为适配能力 | 只有依赖体积、AOT 可达性或独立交付证据充分时增加传输项目 |
| ImportExport / Reporting / GoView | 导入导出管任务编排；Reporting 管受控报表；GoView 管交互大屏 | 数据读取走拥有者合同/授权数据源；业务规则不迁入通用任务引擎 |
| Ai / Payments / K3Cloud / Ocr | 保留各自外部系统语义、幂等和业务状态；提供程序与核心用例分离 | 先落实事务外副作用、超时未知和恢复，无需引入网络边界 |
| Platform / ObservabilityAdmin / Jobs / Messaging | 平台控制、运维查询、作业调度、可靠事件各自负责 | 禁止把新功能都塞进 Platform，或把普通审计/缓存失效变成 Outbox |
| Calendar / Regions / Settings / CodeGeneration / Mqtt / Cryptography / Auditing | 当前功能边界可保留 | 依靠真实变化耦合、所有权和性能证据调整；不以模块小为由自动合并 |

## 5. 新鲜验证结果

测试在本地非容器环境运行。原始日志保留于 [本地审查证据目录](G:/wwwroot/github_fork/Full.NET/artifacts/reviews/full-project-20260907)。该目录受 Git 忽略，交接时需单独附带或由 CI 重新产生，不能假定提交报告会包含原始日志。

| 命令 | 结果 | 解释 |
| --- | --- | --- |
| `pnpm test:task:start -- full-project-review-20260907` | 成功 | 建立脏工作区基线快照 |
| `pnpm test:dotnet:architecture` | **160 通过 / 44 失败 / 204 总计** | Release 构建 0 警告 0 错误；测试未通过 |
| `pnpm test:dotnet:unit` | **2,295 通过 / 5 失败 / 1 跳过 / 2,301 总计** | Linux FIFO 条件用例跳过；构建 0 警告 0 错误 |
| `pnpm --filter @fullnet/admin build` | **失败，440 条 TS 错误诊断** | vue-tsc 阶段失败；不是 440 个独立产品缺陷 |
| `pnpm test:aot:analyzers` | **失败，214 个错误** | 尚未进入 Linux 原生发布验证 |
| `pnpm test:governance` | **50 通过 / 2 失败** | 含模块图等清单漂移 |
| `pnpm test:naming` | **26 通过 / 4 失败** | UUID/数据库对象注释等治理失败 |
| `pnpm test:openapi` | **142 通过 / 10 失败** | 生成模型、适配器、manifest 和夹具漂移 |
| `pnpm test:sql-safety` | **5 通过 / 0 失败** | 仅脚本安全规则，不能覆盖运行时租户隔离 |
| `pnpm test:helm` | **13 通过 / 0 失败** | 部署合同静态检查，不等于部署验收 |
| `pnpm test:performance-governance` | **14 通过 / 0 失败** | 性能治理规则，不等于性能达标 |
| `dotnet run --project <临时探针>/Review.csproj -c Release` | 退出码 0，见下面的缺陷输出 | 退出成功表示探针运行完毕，不表示被测行为正确 |
| `dotnet restore src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj --nologo` | 退出码 0 | 恢复 AOT 分析后的普通依赖图；不是重新构建通过 |

探针使用架构测试 Release 输出中的实际模块程序集，未连接数据库、发送邮件、调用支付/模型服务，也未读取真实环境秘密。测试脚本位于 [Program.cs](G:/wwwroot/github_fork/Full.NET/artifacts/reviews/full-project-20260907/Program.cs)，项目中的程序集引用路径依赖本机仓库和 Release 构建输出；在另一工作区复现时需调整路径并先构建基线。

```text
BuildOutTradeNo: different UUIDs=True, duplicate numbers=True
BuildOutRefundNo: different UUIDs=True, duplicate numbers=True
Printing Render retains executable onerror attribute=True
Unregistered environment reference resolves synthetic marker=True
ImportExportTaskSql.FindById: TenantScopeViolationException
ReportingExportTaskSql.FindById: TenantScopeViolationException
AiTenantQuotaSql.FindByTenantId: HostContextRequiredException
AiModelConfigSql.FindById: HostContextRequiredException
HostFileSql.FindActiveById: HostContextRequiredException
MQTT original 'on' versus requested 'no' accepted as replay=True
```

命名测试中的 generate-object-comments 用例会写入真实注释目录。本次已留存生成后的副本与 diff，并恢复该测试产生的修改；未把它当作审查修复提交。工作区已有和并行产生的无关文件不纳入本报告整改成果。

## 6. 整改顺序与关闭条件

1. **恢复可验证基线**：R01、R14、R15 和 R18 中的装配/陈旧夹具；按根因修复，不放宽门禁。恢复默认 API 元数据、Vue 类型构建、AOT 分析和受影响基础测试。
2. **同步处理安全与资金问题**：R04–R08，建立固定时钟、失败提交、双商户、模板注入和验证码失败次数等可失败回归，再做最小修复。这些不能等待页面验收结束才处理。
3. **打通租户纵向链**：R02、R03、R09，先确认文件/配置合同再实现；验证真实租户导入、报表、AI 使用以及 A/B 租户隔离。
4. **补并发与恢复**：R10–R12、R17，测试进程退出、租约失效、重复请求、部分成功和跨实例取消；R13 的迁移策略与双库验证同步完成。
5. **测量再优化**：R16 及批处理/长事务热点，记录代表性数据规模、SQL 往返、连接等待、分配、峰值内存和 P95/P99；优化前后保持相同语义、数据和环境。
6. **核对发布证据**：源头门禁通过后，按授权将双库 Integration、Linux Native AOT、真实浏览器、Kafka/CDC 和恢复场景交给目标提交的 GitHub Actions；容量仍须专用生产等价环境认证。

这是整改建议顺序，不是已批准的实施计划或新的架构 ADR。没有执行双库、浏览器、移动端、外部提供程序、Linux 原生或容量验证，相关能力保持未验证；项目容量状态仍为 `Capacity-not-verified`。

规则复盘：本次安全和重复模式已命中复盘条件，但主要属于现有租户、事务、幂等、AOT 与验证规则覆盖的实现缺陷。优先补具体回归和实际门禁覆盖，不新增近义规则或机械修改 Skill；本次未修改规则/Skill。
