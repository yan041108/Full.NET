# Admin.NET.Pro 功能对标路线

- 基线仓库：`G:\wwwroot\github_fork\Admin.NET.Pro`
- 基线分支：`v2.1`
- 基线提交：`3879b035791b4603e734c15e7c316e0aeca32f1b`（2026-07-13）
- 建立日期：2026-07-17
- 目标：Admin.NET.Pro 的适用功能原则上在 Full.NET 中全量对标
- 当前能力总览：[`capability-status.md`](capability-status.md)
- 源码设计复核：[`adminnet-source-design-absorption-review-2026-07-30.md`](../verification/adminnet-source-design-absorption-review-2026-07-30.md)
- 吸收改造计划：[`2026-07-30-adminnet-design-absorption-program.md`](../superpowers/plans/2026-07-30-adminnet-design-absorption-program.md)

## 1. 对标定义

全量对标以业务能力、关键用户流程和项目交付价值为验收对象，不要求复制 Admin.NET.Pro 的源码、表结构、API 路径、Furion/SqlSugar 使用方式或工程拆分。

每项能力必须归入以下一种交付形态：

- `Core`：Full.NET 1.0 默认基础能力；
- `Official Module`：官方维护、按需安装的业务模块；
- `Provider`：第三方平台或基础设施适配器；
- `Compatibility`：迁移旧 Admin.NET 项目所需的兼容层；
- `Sample`：强行业属性的参考实现；
- `Client`：管理端、移动端、桌面端或可视化客户端。

状态定义：

- `Mapped`：已确定 Full.NET 归属，尚未实施；
- `Designing`：正在形成模块规格；
- `Implementing`：正在开发；
- `Implemented`：功能完成，尚未通过完整对标验收；
- `Verified`：功能、关键流程、权限、租户和测试均已验收；
- `Not Applicable`：经设计评审确认不适用，并已记录替代方案。

功能对标完成的唯一标准是状态为 `Verified` 或经批准的 `Not Applicable`。

本矩阵表达长期功能范围，不等于当前可用清单。当前真正落地的业务范围、验证级别和近期缺口以 `capability-status.md` 为唯一总览；任何公开介绍不得把大量 `Mapped` 行合计为“已具备完整后台能力”。

源码设计按四级处理：

- `A / 优先吸收`：产品价值明确，能够按现有 Full.NET 边界形成独立纵向切片；
- `B / 重构吸收`：保留业务语义，但必须先解决安全、事务、租户或模块边界；
- `C / 兼容隔离`：只进入 Compatibility、Provider 或受控工具；
- `D / 拒绝原实现`：不复制会削弱 Full.NET 不变量的机制，只保留等价业务目标。

当前优先顺序：吸收计划 Task 1–10（代码生成、Grid 偏好、流水号、Jobs 调度、Files Provider、字段投影、请求签名、出站审计、只读模块目录）已合入 `main`；其后进入下方「大型插件独立执行队列」。该顺序只表达实施依赖，不改变下表任何能力状态，也不把 `Mapped` 行合计为已交付能力。

## 2. README 内置功能基线

| Admin.NET.Pro 功能 | Full.NET 归属 | 形态 | 计划 | 状态 |
|---|---|---|---|---|
| 管理端登录、刷新会话、退出与当前用户 | Identity + `ui/admin` + `ui/admin-layui` | Core + Client | M2 | Implemented |
| 最小 RBAC、可信租户上下文与动态权限导航 | Identity + Tenancy + 双管理端 | Core + Client | M2 | Implemented |
| 默认超级管理员、未来权限自动获得与最后一名保护 | Identity + 双管理端 | Core + Client | M2 | Implementing |
| 主控面板、工作台、分析和统计 | `ui/admin` + `ui/admin-layui` + Dashboard Contracts | Client | M3 | **Build-verified**（Host 工作台汇总 API + 双端 Overview 真实指标；[验证记录](../verification/platform-host-dashboard-2026-07-26.md)、[实施计划](../superpowers/plans/2026-07-26-platform-host-dashboard-vertical-slice.md)） |
| 用户管理 | Identity | Core | M2 | Implementing（Host 列表/创建/禁用/启用/重置密码已交付；[验证记录](../verification/identity-user-management-2026-07-21.md)、[重置密码](../verification/identity-host-user-reset-password-2026-07-25.md)、[启用](../verification/identity-host-user-enable-2026-07-25.md)） |
| 租户管理 | Tenancy | Core | M2 | **Build-verified**（Host 列表/开通/更新/禁用；[验证记录](../verification/tenancy-host-tenant-management-2026-07-23.md)） |
| 机构管理 | Organization | Core | M2 | **Build-verified**（租户机构树 CRUD；见[验证记录](../verification/organization-unit-management-2026-07-21.md)） |
| 职位管理 | Organization | Core | M2 | **Build-verified**（租户职位 CRUD、机构与职级绑定/解绑、职级目录 CRUD、双库 Integration、双端 parity 及双库双端真实栈写入；完整 `main` CI 与发布前人工验收待补） |
| 用户职位隶属 | Organization | Core | M2 | **Build-verified**（租户用户-职位分配；正式可分配 Host 用户候选目录支持双库分页、精确写权限与双端按需加载；双管理端、双数据库真实栈分配/设主/取消/API 回读已通过；见[验证记录](../verification/organization-user-position-assignment-2026-07-25.md)） |
| 菜单、页面与按钮权限管理 | Identity | Core | M2 | **Build-verified**（W0–W5 全模块精确动作权限已清零：授权树 API、角色“模块/页面/操作”树、Vue `PermissionGate` 逐按钮门控、双库迁移 054–080、Architecture 拒绝未登记 `.write`/`.manage` 绑定；program affected merge 曾在 185/270 中断，完整复跑前不得提升为 `Verified`；见[设计](../superpowers/specs/2026-08-02-vue-action-authorization-design.md)、[W4–W5 计划](../superpowers/plans/2026-08-03-vue-action-authorization-w4-w5.md)、[收口验证](../verification/vue-action-authorization-w4-w5-closeout-2026-08-03.md)；Layui 不参与验收） |
| 角色与数据授权 | Identity + Organization | Core | M2 | **Build-verified**（Host 角色/权限/数据范围、用户-角色分配与运行时机构过滤；[验证记录](../verification/identity-role-data-authorization-2026-07-26.md)、[收口计划](../superpowers/plans/2026-07-26-identity-role-data-authorization-parity-closure.md)） |
| 字典管理 | Settings | Core | M3 | **Build-verified**（字典类型 + 字典项 Host CRUD 与双端 UI；见[验证记录](../verification/settings-dictionary-2026-07-25.md)、[类型切片](../superpowers/plans/2026-07-25-settings-dictionary-vertical-slice.md)、[项 UI 切片](../superpowers/plans/2026-07-25-settings-dict-items-ui-vertical-slice.md)） |
| 访问日志 | Auditing | Core | M3 | Build-verified |
| 操作与异常日志 | Auditing | Core | M3 | Build-verified |
| 服务监控 | Observability Admin | Official Module | M5+ | Mapped |
| 在线用户与强制下线 | Identity + Notifications | Core | M2 | **Build-verified**（Host 在线会话列表与强制下线；[验证记录](../verification/identity-host-online-sessions-2026-07-26.md)） |
| 公告与 SignalR 通知 | Realtime + Notifications | Core | M2/M3 | **Build-verified**（Host 公告草稿/发布 + `IRealtimePublisher` 广播；[验证记录](../verification/notifications-host-announcement-2026-07-26.md)） |
| 文件与对象存储 | Files + Storage Providers | Core + Provider | M3/M5+ | **Build-verified**（Host 文件元数据上传/列表/下载/删除；稳定 `ProviderKey` 持久化与注册表；下载、补偿及墓碑清理按记录路由；当前仅本地 Provider；[验证记录](../verification/files-storage-provider-boundary-2026-08-01.md)） |
| 任务调度 | Jobs | Core | M3 | **Build-verified**（Host 任务定义 CRUD、手动/一次性/Cron 触发、IANA/Windows 时区规范化、`skip`/`fire_once`、暂停恢复、执行历史关联与 Worker 原子物化；SQL Server/MySQL `040_JobsSchedules` 恢复及双库纵向切片通过；见[计划调度验证](../verification/jobs-schedules-2026-07-31.md)、[基础验证](../verification/jobs-host-definitions-2026-07-26.md)） |
| 系统配置 | Settings | Core | M3 | **Build-verified**（Host 配置项 CRUD 与双端 UI；见[验证记录](../verification/settings-system-config-2026-07-25.md)、[实施计划](../superpowers/plans/2026-07-25-settings-system-config-vertical-slice.md)） |
| 邮件与短信 | Notifications Providers | Provider | M5+ | Mapped |
| Swagger、OpenAPI 和接口文档 | Hosting | Core | M1 | **Build-verified**（OpenAPI 元数据、Bearer JWT、Scalar UI 与双端入口；[验证记录](../verification/platform-openapi-documentation-2026-07-26.md)、[实施计划](../superpowers/plans/2026-07-26-platform-openapi-documentation-vertical-slice.md)） |
| 前后端代码生成 | CodeGeneration | Core | P0 Naming Profile/命名内核；M3 首个纵向样例 | Implementing（统一 `FullNetCrudSchema`、显式 Tenant/Host/Global 作用域、Product 确定性跨栈产物、后端 CRUD 骨架、Vue/Layui 页面模型、成对双库迁移草案、最小双 Provider 集成测试草案及安全预览/应用 CLI 已完成；Decimal precision/scale 已从严格 JSON 与双库元数据贯通到报告和迁移草案；双库基础表目录已可只读扫描并排除视图；严格逐表语义映射、单连接多表导入、合并工作区批量预览和独立显式批量 Apply 已完成双库验证；整批写盘复用同一 Manifest 所有权、冲突零写入、原子提交与 committed tombstone；模块接入已提供严格显式目标驱动的只读影响计划，缺失项目、入口、Composition、路由或客户端适配文件会保守阻塞；后端产物可通过系统临时投影执行真实 Release 编译，也可由独立 `apply-module-integration` 在编译通过后按实体目录原子写盘、保留同模块其他实体所有权并幂等重入；模块级聚合注册桥会按全部受管实体确定性重建；`apply-module-entry-integration`、`apply-composition-integration` 与可选 `apply-client-route-integration` 均保持显式目标、先编译后提交和幂等重入；Host-only 管理模块现已提供严格预览、模板持久化、可信审计、乐观并发与软删除 API，`044_CodeGenerationTemplate` 已完成双库恢复验证；受跟踪预览现以独立 read/execute 权限写入不可变无源码摘要，`045_CodeGenerationRun` 已完成双库恢复，运行目录使用单往返分页；Host 受控 Apply 以独立权限、默认禁用的运维工作区和 `046_CodeGenerationApply` 双库状态机完成 Vue/Layui 真实栈验证，并在工作区修改前持久化不可覆盖、可校验的本地回滚证据，内部 GenerationRollbackWorkspace 可对已验证检查点执行 fail-closed 逆向写盘，见[Host Apply 验证](../verification/codegeneration-host-apply-2026-07-31.md)与[回滚检查点验证](../verification/codegeneration-apply-rollback-checkpoint-2026-08-01.md)、[产品 Rollback 验证](../verification/codegeneration-product-rollback-2026-08-02.md)；产品 Rollback（`codegen.runs.rollback`、051、共享 Apply Gate、Vue/Layui 真实栈 Apply→Rollback 4/4）已交付且保持 `Build-verified`；检查点保留清理、Worker/多实例调度、远程仓库写入及生产默认启用仍开放； |
| 在线表单构建器 | FormBuilder | Official Module | M5+ | Mapped |
| 微信小程序与微信支付 | WeChat + Payments | Official Module + Provider | M5+ | Mapped |
| Excel 导入导出、HTML/PDF 报告 | ImportExport + Reporting | Official Module + Provider | M5+ | Mapped |
| 接口限流 | Hosting | Core | M1 | **Build-verified**（全局限流配置、`hosting.rate_limit.exceeded` 与 Identity 端点策略；[验证记录](../verification/hosting-global-api-rate-limit-2026-07-26.md)、[实施计划](../superpowers/plans/2026-07-26-hosting-global-api-rate-limit-vertical-slice.md)） |
| Elasticsearch 日志 | Elasticsearch Observability | Provider | M5+ | Mapped |
| OAuth 2.0 外部登录 | Identity OAuth Providers | Provider | M5+ | Mapped |
| APIJSON 零代码查询 | APIJSON Compatibility | Compatibility | M5+ | Mapped |
| 数据库视图与实体维护 | DatabaseTools + CodeGeneration | Official Module | M5+ | Mapped |

## 3. Core 中额外发现的能力

| Admin.NET.Pro 能力 | Full.NET 归属 | 形态 | 计划 | 状态 |
|---|---|---|---|---|
| API Key 认证 | Identity | Core | M2 | **Build-verified**（Host 创建/列表/禁用/轮换与认证、最后使用展示与列表刷新、Vue/Layui 双管理端、Mock parity 2/2、真实栈浏览器 6/6；见[验证记录](../verification/identity-api-key-2026-07-26.md)） |
| 请求签名认证 | Identity Signature Auth | Official Module | M5+ | **Build-verified**（HMAC、Nonce、Access Key、失败审计；2026-08-02 双库真实栈 Integration **2/2**；OpenAccess 产品化仍属 `Mapped`；见[验收记录](../verification/adminnet-tasks-8-10-realstack-2026-08-02.md)） |
| 出站调用审计 | Auditing Outbound Calls | Core | M3 | **Build-verified**（opt-in 写入、脱敏、Host 查询、043 恢复、Outbound 保留；2026-08-02 双库 **6/6** 聚焦；见[验收记录](../verification/adminnet-tasks-8-10-realstack-2026-08-02.md)） |
| 缓存管理 | Caching Admin | Official Module | M5+ | Mapped |
| 列显示个性化 | Settings + Client Preferences | Core | M3 | **Build-verified**（当前用户 Grid 偏好 API、双库 038、可信 Grid/Column 目录、SchemaVersion/Version、FusionCache、Vue/Layui 适配器；首个目录 `identity.users`，可视化列编辑器与真实浏览器 E2E 待具体 Grid 消费者接入；见[验证记录](../verification/settings-grid-preferences-2026-07-30.md)） |
| 全栈多语言、时区与用户语言偏好 | Localization + Identity + Tenancy + Clients | Core + Client | M2-M5+ | Implementing |
| 模块化开发/演示种子数据与执行审计 | Seeding + Migrator + Module Contributors | Core | M2 | Build-verified（双库契约见[验证记录](../verification/seed-dual-database-contract-2026-07-21.md)） |
| 数据库管理 | DatabaseTools | Official Module | M5+ | Mapped |
| 枚举、常量查询 | Settings Metadata | Core | M3 | **Build-verified**（只读枚举目录 API 与双端 UI；见[验证记录](../verification/settings-enum-catalog-2026-07-25.md)、[实施计划](../superpowers/plans/2026-07-25-settings-enum-catalog-vertical-slice.md)） |
| 消息中心 | Notifications | Core | M3 | **Build-verified**（Host 发信 + 个人收件箱/未读/已读；[验证记录](../verification/notifications-inbox-message-2026-07-26.md)） |
| MQTT | MQTT Provider | Provider | M5+ | Mapped |
| 开放接口访问 | OpenAccess | Official Module | M5+ | Mapped |
| 插件管理 | Modularity Admin | Official Module | M5+ | Implementing（只读官方模块清单 API 与双端 UI；Architecture 禁止 Roslyn/ApplicationPart；2026-08-02 双库 Integration **2/2**；见[验收记录](../verification/adminnet-tasks-8-10-realstack-2026-08-02.md)） |
| 打印 | Printing | Official Module + Client | M5+ | Mapped |
| 行政区域 | Regions | Official Module | M5+ | Mapped |
| 报表配置 | Reporting | Official Module | M5+ | Mapped |
| 流水号规则 | SerialNumbers | Official Module | M5+ | **Build-verified**（Host 规则 API、纯预览、Host/租户计数器、UTC 日/月/年重置、幂等分配、SQL Server/MySQL 039 与恢复测试、Vue 页面及精确操作权限已交付；分页筛选、规则表单体验和独立真实栈 E2E 仍待后续切片；见[验证记录](../verification/serial-numbers-2026-07-30.md)） |
| 系统升级 | Upgrade Management | Official Module | M5+ | Mapped |
| 支付宝 | Payments.Alipay | Provider | M5+ | Mapped |
| 微信生态 | WeChat | Official Module + Provider | M5+ | Mapped |
| Kafka / CDC Relay / EventBus 事件集成 | EventDelivery Provider | Provider | 提前实施计划 | Designing（采用事务追加式 Outbox + SQL Server CDC/MySQL Binlog + Debezium + Kafka + Inbox，不引入 CAP/MassTransit；当前仅完成 ADR/Spec/计划，见 [`ADR-0006`](../architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md)与[专门计划](../superpowers/plans/2026-08-08-transactional-outbox-cdc-kafka.md)） |
| 国密 SM2/SM3/SM4 | Cryptography.GM | Provider | M5+ | Mapped |
| 数据导入导出工具 | ImportExport | Official Module | M5+ | Mapped |
| 服务器硬件与运行时信息 | Observability Admin | Official Module | M5+ | Mapped |
| System.Text.Json 源生成与序列化基准 | Serialization | Core | M0-M1 | **Build-verified**（Architecture 门禁从生产 Endpoint 元数据枚举请求、响应、分页项与 ProblemDetails 类型并验证模块源生成上下文覆盖；生成式客户端 SDK 属于独立演进项） |
| MessagePack 可靠事件载荷 | Messaging + Outbox | Core | M1 | Implemented |
| gRPC/Protobuf 跨进程同步通信 | ServiceCommunication.Grpc | Provider/Template | 首次服务拆分时 | Mapped |
| SignalR、MessagePack Hub 和 Redis Backplane | Realtime | Core + Provider | M2 | **Build-verified**（`IRealtimePublisher` + Hub + JWT 分组；[验证记录](../verification/realtime-signalr-foundation-2026-07-26.md)） |
| 模型供应商中立 AI 抽象 | AI.Abstractions | Official Module | M5+ | Mapped |
| Agent、MCP 与 Agentic Web | Agents + AgenticWeb | Official Module + Protocol Adapter | M5+ | Mapped |

## 4. 插件能力基线

| Admin.NET.Pro 插件 | 关键能力 | Full.NET 归属 | 形态 | 状态 |
|---|---|---|---|---|
| `Admin.NET.Plugin.Ai` | AI 模型配置、对话、Agent、工具调用、MCP 与 Agentic Web | AI + Agents + AgenticWeb | Official Module + Provider + Protocol Adapter | Mapped |
| `Admin.NET.Plugin.DataApproval` | 数据变更审批 | DataApproval | Official Module | Mapped |
| `Admin.NET.Plugin.DingTalk` | 钉钉组织、消息和接口 | DingTalk | Provider | Mapped |
| `Admin.NET.Plugin.Document` | 文档、分类、标签、权限、分享、预览、版本、回收站和统计 | Document | Official Module | **Implementing**（Gate G4 已批准，Host 文档/分类/标签/版本基础切片、Vue 与双库验证已交付；分享、预览、持久化回收站、文档级 ACL、版本历史与统计仍未实现，不能把基础切片等同于插件全量对标） |
| `Admin.NET.Plugin.GoView` | 可视化大屏 | GoView | Official Module + Client | Mapped |
| `Admin.NET.Plugin.K3Cloud` | 金蝶云星空接口集成 | K3Cloud | Provider + Sample | Mapped |
| `Admin.NET.Plugin.PaddleOCR` | OCR 识别 | OCR | Provider | Mapped |
| `Admin.NET.Plugin.ReZero` | 线上建表、动态接口、授权和超级 API | DynamicApi | Compatibility + Official Module | Mapped |
| `Admin.NET.Plugin.WorkFlow` | 流程设计、发布、实例、审批、待办、抄送和业务联动 | Workflow | Official Module | Mapped |
| `Admin.NET.Plugin.WorkWeixin` | 企业微信接口集成 | WorkWeixin | Provider | Mapped |

插件的详细功能必须在各自实施前建立独立设计规格。核心模块不得为了插件反向增加业务耦合。

### 4.1 大型插件独立执行队列

下列能力在 Gate G4（1.0 核心能力与生产硬化不再被阻塞）批准前保持 `Mapped`，禁止创建空项目、通用 Repository、投机性 `*.Contracts` 程序集或未批准的模块规格。每一项都必须在启动实施前单独建立带日期的 Spec，并经安全/租户/双库门禁审查。

| 顺序 | 模块 | 前置依赖 | 所有权与契约边界 | 退出门禁 |
| --- | --- | --- | --- | --- |
| 1 | Document | Files Provider 稳定；字段投影可用 | 通过显式契约消费 Files；自有分类/标签/版本/分享/权限/日志数据，禁止把文件字节存入业务表 | 双库迁移与恢复、标准 API、逐页面/逐操作权限、租户/数据范围、Outbox（如需）、Vue、E2E、运维文档与许可证据；**2026-08-02 规格草案**见[Document 设计](../superpowers/specs/2026-08-02-document-module-design.md)（待 Gate G4 批准） |
| 2 | Workflow | Notifications 与 Jobs 恢复路径可用 | 拥有不可变定义版本、实例、步骤、待办、抄送、执行日志与恢复；禁止业务模块直连流程表 | 同上，且必须覆盖实例恢复与幂等推进 |
| 3 | DataApproval | Workflow 可用 | 通过显式用例契约集成，禁止任意 HTTP 中间件拦截改写业务写路径 | 同上，且必须覆盖审批拒绝/撤回与审计 |
| 4 | ImportExport / Reporting | 字段投影稳定 | 导入导出与报表配置分模块；禁止动态 SQL 拼接与未授权列泄露 | 同上，且必须覆盖大文件/批处理背压与失败续跑 |
| 5 | AI / Agents | 权限、配额、审计基线可用 | 供应商中立抽象；显式 Tool 权限与审计；预览协议只进适配器 | 同上，且必须覆盖配额、人工确认高影响 Tool 与租户隔离 |

队列外的 Provider/Sample（钉钉、企微、OCR、K3Cloud、GoView 等）不得插队占用上述依赖链；需要时各自开独立 Spec，并证明不反向耦合核心模块。

吸收计划 Task 11 仅冻结本队列与门禁，不创建任何大型模块规格或代码骨架。见[执行队列记录](../verification/adminnet-large-module-execution-queue-2026-08-01.md)。

AI 对标不止复制模型配置和聊天页面。Full.NET 的验收范围还包括 `Microsoft.Extensions.AI` 供应商中立抽象、模型/Token/费用配额、显式 Tool 权限、Agent 会话与步骤、人工审批、MCP Client/Server、AG-UI 或等价标准 Web 协议、租户隔离和可靠审计。预览协议包必须封装在独立适配器中，不能成为核心稳定 API。

Realtime 对标分两阶段：M2 先交付 `IRealtimePublisher`、SignalR、MessagePack Hub Protocol、连接鉴权、租户分组和 Redis Backplane；M3 的 Notifications 再消费该抽象实现公告、站内信、未读数和多渠道通知。业务模块不得直接持有 `IHubContext`。

## 5. 客户端与交付形态

| Admin.NET.Pro 资产/交付需求 | Full.NET 对标 | 形态 | 计划 | 状态 |
|---|---|---|---|---|
| `Web` Vue3 管理端 | `ui/admin`：Vue 3 + TypeScript + Vite + Element Plus；Art Design Pro 壳层已迁入（见[验证](../verification/admin-art-design-pro.md)） | Client | M2-M4 | Build-verified |
| JS/HTML 存量管理端 | `ui/admin-layui`：历史 Layui 2 + HTML/CSS/原生 JavaScript 实现；2026-08-02 起停止新增功能 | Frozen Client | 仅安全/许可/迁移/退役例外 | Frozen |
| `App` H5/小程序资产 | `clients/uniapp`：H5、微信小程序、支付宝小程序；默认 uni-ui | Client | M3-M4 | Implementing |
| 原生移动端 | `clients/flutter`：Flutter 3.44 Material 3 + Cupertino；Android、iOS | Client | M5+ | Designing |
| `Web_Desktop`/PC 桌面需求 | `clients/flutter`：Flutter 3.44 Material 3 + Cupertino；Windows、macOS、Linux | Client | M5+ | Designing |
| .NET MAUI 交付 | `clients/maui-template`：命中 C#/Windows 企业项目门禁后按需建立 | Provider/Template | M5+ 按需 | Mapped |
| `Web_Artd` | Vue 设计令牌与可替换主题能力，不再维护第二套完整后台产品线 | Client | M4/M5+ | Mapped |
| `GoView` | 可视化大屏客户端 | Client | M5+ | Mapped |

Vue 是后台管理唯一持续交付线。后台功能只有在服务端双库、页面/逐操作权限、租户、状态反馈、错误处理、可访问性、Vue 关键流程和真实栈 E2E 全部通过后才能标记为 `Verified`。Layui 历史实现保留为冻结证据，不再要求功能对等，也不得阻塞 Vue 主线。

登录/会话与最小 RBAC 的历史双端证据继续有效，但后续状态只根据服务端、共享契约和 Vue 的新鲜证据更新；Cookie/CORS/CSRF/并发刷新、权限撤销和直接 API 403 仍必须由真实栈验证。

uni-app 与 Flutter 不复制完整后台管理能力：uni-app 负责 H5/微信/支付宝业务客户端，Flutter 负责原生移动和 PC 桌面。详细阶段和依赖见 [`client-delivery-roadmap.md`](client-delivery-roadmap.md)。

多语言不以管理端语言选择器作为完成标准。ASP.NET Core、Vue 组件库、uni-app、Flutter、用户/租户偏好以及服务端通知/报表必须分别达到对应阶段的退出条件；冻结 Layui 不新增本地化功能。稳定错误码、权限码、审计 code 和 Agent Tool Schema 不本地化。详细边界见[全栈多语言设计](../superpowers/specs/2026-07-17-full-stack-localization-design.md)。

## 6. 验收规则

每一项从 `Implemented` 进入 `Verified` 前必须满足：

1. 已明确 Core、Module、Provider、Compatibility、Sample 或 Client 归属；
2. 后端权限、租户隔离和审计规则已覆盖；
3. 关键业务流程与 Admin.NET.Pro 基线逐项比较；
4. 差异是有意设计并记录，不是遗漏；
5. SQL Server 和 MySQL 的适用测试通过；
6. API 契约、Vue 前端交互、逐页面/逐操作权限、错误处理、可访问性和真实栈 E2E 通过；Layui 不再参与新增功能验收；
7. 来源、许可证和直接复用情况已经登记；
8. 文档和升级说明已经完成。

## 7. 维护规则

- Admin.NET.Pro 基线升级或本地授权版本新增功能时，必须更新本矩阵；
- 每个 Full.NET 里程碑结束时复核一次矩阵状态；
- 新功能默认先判断是否为 Core，不能因为对标要求而直接放入核心；
- `Not Applicable` 必须经设计评审，并给出替代能力或不实现的技术理由；
- 功能对标不能突破 MIT 发布和第三方授权边界。
