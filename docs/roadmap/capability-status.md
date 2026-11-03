# Full.NET 能力状态矩阵

> 2026-10-30 最新续办：默认预取1及显式预取8/低水位0/单槽1的Native双API/双Worker每库60分钟长稳均双库2/2、各3600条完整对账通过；正式/首个重试Topic真实自动TTL双库各2/2，窗口过期后保留原资源/fence并拒绝恢复。预取8的5/10次HTTP每秒短时测量及最终单帧10档复验保留独立输入，默认1的10档失败保留，输入速率不等于持续端到端容量。此前完整Native控制/CDC故障及分片发现保留原输入，未跑全量。生产容量、独立物理故障域及全产品Native仍未认证，保持Pilot / Capacity-not-verified，见[最新记录](../verification/2026-10-28-messaging-reliability-continuation.md#2026-10-30预取8长稳与自动ttl续办)。

> 2026-10-29 续办：时间观测已有条件性真实来源，未知/错误继续失败关闭；迁移 342 保存切流时不可变 Kafka 资源基线，拒绝同名 Topic 重建、原窗口裁剪及重试阶段减少。授权独立静态复核完成，最终双库故障2/2、重放2/2、策略/真实ACL2/2、原十分钟告警1/1及最新Native双库HTTP2/2均通过；最新API/Worker AOT分析零警告错误。精确结果与历史失败见[本机续办记录](../verification/2026-10-28-messaging-reliability-continuation.md#2026-10-29时间观测与不可变回滚基线续办)。下方日期记录保留当时范围，正式切流默认关闭。

> 2026-10-28 Messaging 续办：同步后改动已恢复，补齐真实CDC平台/源窗口采集、未知与陈旧状态、Connector身份及恢复源窗口保护，修复选项重复绑定与Connect配置更新。最新双API/双Worker故障双库2/2、平台2/2、原阈值告警1/1、源窗口告警2/2、组件57项、三Broker单节点恢复1/1、独享短测2/2、双库各60分钟Soak 2/2及Native配置/认证/运维只读片段2/2均实际通过。MySQL时间lag/Kafka时间保留比保持unknown，独立审查未完成。保持Pilot / `Capacity-not-verified`，不启用正式切流。精确范围见[本机记录](../verification/2026-10-28-messaging-reliability-continuation.md)，旧机器与同步前结果不替代当前验收。

> 2026-10-23 Workflow 专项修订：同步现有节点、两项已复现待修缺陷、完整 Workflow-Vue3 节点整改与跨端缺口；明确排除 LogicFlow。本次为文档修订，未重新认证其他能力或执行新的构建/运行时验收。

> 更新时间：2026-10-05（本轮同步 AI 知识库显式启用的文本 Worker 消费切片，其他能力不重新认证）。本文只维护能力状态、稳定证据入口与后续优先级；可变测试数量统一以 [`eng/testing/test-matrix.json`](../../eng/testing/test-matrix.json) 为准。

> 2026-09-16 规划增补：已纳入[八项底座完善能力及七类收口重点](adminnet-feature-parity.md#8-企业应用与-saas-底座完善队列2026-09-16)，执行见[唯一总计划](../superpowers/plans/2026-09-16-foundation-productization.md)。本次未重新认证下表的实现状态；部分历史条目落后于专项记录，F00 将逐项核对。工作区其他改动不作为能力完成证据。

## 状态定义

2026-09-30 项目所有者将全部验收改为本地测试实际通过即可；执行位置以 [开发质量 §11](../../rules/development-quality.md#11-测试与验证)为准。本表历史“待 CI/专用环境”代表所需测试未完成，可改在本地执行；本次不自动升级任何历史能力状态。

- `Planned`：已有方向，但尚未形成可执行设计。
- `Designing`：规格、ADR 或实施计划正在收敛。
- `Implemented`：实现已经落盘，尚未完成规定的构建或契约验证。
- `Build-verified`：已通过与当前能力相称的构建、单元、架构或双库验证。
- `Production-verified`：已通过对应生产配置、故障、恢复与运维验收，本地证据即可；保留此历史状态名，但必须注明测试环境与容量范围，不表示已经在真实生产运行。
- `Frozen`：只允许明确授权的安全修复、迁移或退役工作。
- `Deferred`：经架构决策暂不进入当前版本。

`Build-verified` 不等于 `Production-verified`。容量、备份恢复、滚动升级、外部依赖故障和真实流量行为须按对应范围实际测试，可在本地验收；功能通过不自动认证未测规模或故障域。

## 当前能力矩阵

| 能力 | 状态 | 稳定证据与剩余边界 |
|---|---|---|
| 认证事件日志管理（2026-09-25） | Partial | 复用 Identity `fn_identity_auth_audit`；双库上下文字段、Host 权限查询 API、Vue 页面、OIDC 中心登录/应用会话/主动退出事件、默认关闭的 Worker 保留及受控 CSV 导出已形成纵向切片。完整事件矩阵、故障/并发门禁、游标分页和真实运维证据仍待开发；见[唯一开发计划 AE01—AE06](../superpowers/plans/2026-09-25-authentication-event-logs.md)。不得与 B2 API 访问日志混同。 |
| 模块化单体、API/Worker/Migrator 运行角色 | Build-verified | [总体架构规格](../superpowers/specs/2026-07-17-fullnet-architecture-design.md)、[`ADR-0002`](../architecture/adr/ADR-0002-modular-monolith-evolution.md) 与 Architecture 门禁共同约束；尚未触发全面微服务拆分门槛。 |
| Migrator / LogConsumer 独立 Native 角色 | Aot-published（本机定向范围） | 2026-10-31真实linux-x64发布及运行：Migrator双库迁移、Baseline/Development幂等、Production Overlay拒绝、OIDC播种及迁移失败门禁4/4；LogConsumer真实TLS、确认/重放/DLQ与停机4/4，均零跳过。默认部署仍为JIT，按[ADR-0015](../architecture/adr/ADR-0015-migrator-log-consumer-native-aot.md)和[本机证据](../verification/2026-10-31-native-remaining-hosts.md)声明，不外推SASL全部机制、满批容量或生产故障域；保持Capacity-not-verified。 |
| 命名、迁移与 CRUD 生成治理 | Build-verified（本地聚焦行为已验证） | 2026-10-27 已完成[四批整改及当前工作区复验](../superpowers/plans/2026-10-24-codegeneration-remediation.md)：完整项目依赖重新构建通过，组织测试正常保留；CodeGeneration HTTP/迁移 14 项及组织列表双库 2 项一次执行 16/16 通过，新增协议 HTTP 回归 3/3 通过，均零跳过。当前验收不依赖旧冻结 DLL 或 Compile 排除；历史 12＋2 项不再承担当前结论。已修复生成、归档/来源约束、错误响应及本轮实验 API 编译问题，正式数据库需迁移 290/292；33 个旧临时目录已清理。生成服务、浏览器及原生探针证据分别使用，不升级整个模块为 Verified/Aot-published，真实远程发布、生产部署、全量测试及容量未在本轮验证。 |
| 独立应用创建与框架源码升级（F01/F15 子集） | Build-verified | 四预设独立 API 构建、Minimal 双库真实字典 CRUD、源码升级的包校验/定制冲突/中断恢复与固定闭包已取得实现提交 `2c10a5d2` 的 CI 证据，见[首轮交付报告](../verification/2026-09-26-f01-created-app-real-stack-closeout.md)。直接 `dotnet new` 安装、完整业务生成、数据库/对象文件/密钥灾难恢复、混合版本运行及独立应用 Native 发布仍未验收；不关闭 F01/F15/F16 整项，保持 `Capacity-not-verified`。 |
| Dapper、租户 SQL 与命令事务边界 | Build-verified | 模块内强事务已形成统一边界；2026-09-22：`AcceptTenantInvitation`/`TenantMemberProvision` 席位预留与确认已移出 Identity 本地事务并带补偿，[`module-local-transaction-debt.json`](../../contracts/architecture/module-local-transaction-debt.json) 目录为空；2026-09-19 RegisterAccount 权威读取已移出事务。table-access 登记 `SessionBindingKinds` 对 Identity 会话表名的同模块契约引用（`identity-oidc-session-foundation` 里程碑清理）。cross-foreign-key 与 `AllowedReverseContractDependencies` 保持空目录。Organization 单位投影采用消费方拥有的 `Identity.Contracts` Port + Organization 侧适配器，模块依赖 DAG 无登记例外。 |
| UUID v7 逻辑主键与双库物理映射 | Build-verified | SQL Server `uniqueidentifier` 与 MySQL `binary(16)` 已由 008/009 扩展—回填—收缩迁移及恢复测试覆盖；生产维护窗口、备份和 RPO/RTO 演练仍待环境验收。 |
| SQL Server/MySQL 成对迁移 | Build-verified | 迁移命名、顺序、恢复和双 Provider 集成测试已形成门禁。 |
| 事务 Outbox、租约、重试与死信 | Build-verified | 仅承载需要事务原子性的重要 Integration Event；缓存失效、日志、Trace、Metrics 与普通审计禁止进入 Outbox。 |
| Messaging 消息运维闭环 | 前三批本地定向验收完成；完整真实双库切流/回退已通过 | Legacy/Kafka 死信、可信操作者/理由、持久回退恢复、切流证明、持久范围重放、按流总览与受控独立影子采集已实现。2026-10-25 以真实业务 HTTP、MySQL Binlog/独享 SQL Server Agent、Debezium/Kafka、生产 Worker 组件及 Inbox 验证完整切流和回退，2 通过、0 失败、0 跳过；另 46 项单元、2 项双库控制面与 18 项部署/矩阵检查通过。覆盖真实暂停后审计失败恢复、503 业务回滚、成功回退、终态幂等及 Broker/投影/审计对账。前三批及影子验收保留原证据；最新范围见[完整链路记录](../superpowers/plans/2026-10-25-messaging-real-cutover-rollback.md)和[运行时拓扑](../operations/messaging-runtime-topology.md)。同进程运行 Worker 组件，多进程、断网、Soak、容量及独立复核未验收；默认关闭，不提升生产/容量认证。 |
| CDC Relay / Kafka **（Delivery 路径）** | Build-verified / Pilot（本地完整真实链路） | 历史[试点记录](../verification/cdc-kafka-pilot-2026-08-08.md)的范围保持；2026-10-25 当前消息运维实现已另行通过双库完整真实切流/回退，补齐 SQL Server Agent 现场链路，见[完整记录](../superpowers/plans/2026-10-25-messaging-real-cutover-rollback.md)。原始字节与元数据转换、MySQL 心跳位点推进和 SQL Server 时间类型守卫均已修复并回归。多进程、断网、Soak、告警矩阵与容量不在本轮通过范围；正式部署 `DeliveryCutover:Enabled` 默认关闭，保持 `Capacity-not-verified`，不提升 Production-verified。 |
| Kafka Capacity Runner **（测量工具，非 Delivery）** | Build-verified | **工具可用** ≠ Delivery 可切流。Scope A/B/C 已实现；Scope B/C 复用生产 Inbox/Dispatcher 核心，Scope C 追加 Outbox+Connect+CDC。MySQL 缩减集成测试已通过；SQL Server CDC 在 Testcontainers 上 Inconclusive。正式生产等价矩阵、Soak、N+1、故障恢复 **未执行**，测量结果继续 `Capacity-not-verified`。见 [`kafka-capacity-runner.md`](../operations/kafka-capacity-runner.md) 与 [`messaging-runtime-topology.md`](../operations/messaging-runtime-topology.md)。 |
| FusionCache 多实例缓存治理 | Build-verified | 当前写路径使用提交后本实例 L1/L2 删除、Redis Backplane 与 TTL/版本兜底；Tenancy 与 Grid Preference 已纳入统一策略注册表，Architecture 手工策略 allowlist 为零；旧缓存事件处理器只作兼容排空。 |
| 健康检查与运行角色就绪探针 | Build-verified | API、Worker、Migrator 和关键基础设施有独立就绪语义；生产阈值仍由环境配置验收。 |
| HTTP 状态码、ProblemDetails 与兼容包络 | Build-verified | 标准 API 默认采用状态码与 ProblemDetails；Admin.NET 包络仅允许存在于兼容适配层。 |
| System.Text.Json、OpenAPI 与 Vue 调用契约覆盖 | Build-verified | Architecture 测试按生产 Endpoint 元数据枚举 JSON 类型；Vue 生产 API 文件必须逐项映射 OpenAPI fixture 与共享 TypeScript 契约，新增漏项失败关闭。OpenAPI 驱动客户端生成三类试点已 `Pilot-passed`；**Vue 生产 API 全量迁移已收官**（现 230 条 `generated`，45 个 API 模块），见 [`openapi-client-generation-pilot-2026-08-21.md`](../verification/openapi-client-generation-pilot-2026-08-21.md) 与 [`openapi-client-generation-migration-complete-2026-08-23.md`](../verification/openapi-client-generation-migration-complete-2026-08-23.md)。完整公开 npm SDK 发布仍不属于当前完成门槛。 |
| 结构化日志、OpenTelemetry 与低基数指标 | Build-verified | 指标、Trace、日志职责分离；生产采集、告警和保留策略仍需部署环境验收。 |
| 可配置 B2 日志入口（2026-10-02） | Production-verified | Collector/ApplicationKafka 的生产入口配置按本地 Host/Helm、TLS/SASL/ACL、原生与恢复证据准入；默认 Legacy，不自动切流。独立 Kafka 消费、逐项 ES Bulk、可靠 DLQ 与连续 Offset 已有本地闭环，消费者部署维持显式实验开关。安全边界、P99 比较与本期收尾见[验收记录](../verification/2026-10-02-logging-module-closeout.md)；不外推生产最大容量，保留 `Capacity-not-verified`。 |
| 操作日志受限详情与 Vue 日志页面（2026-10-02） | Build-verified | 本期开发收尾：双库 B1 详情授权/到期、受控请求/返回摘要，Vue 三页签、服务端分页、撤权后新会话/403、三页低视口布局已按[真实浏览器与回归](../verification/2026-10-02-logging-module-closeout.md)通过。大规模容量、旧兼容 ES 包迁移、归档及专用安全事件入口作为后续扩展，未执行部分不标已验证。 |
| 可信代理与转发头边界 | Build-verified | 由主机配置、边界测试与运维基线共同约束。 |
| Identity 会话、刷新令牌、MFA 与 TOTP | Build-verified | 单元、集成及生产配置真实栈 TOTP 浏览器链路已有验证记录；不等于生产环境认证。 |
| Identity 用户管理与档案 | Build-verified | Host 用户列表、创建、编辑、启停、重置密码、JSON 兼容接口、固定结构 Excel 模板/导入/导出和批量启停已落地；工作簿限制为 1 MiB/1,000 行并拒绝公式、外部关系和未知表头。手机号、邮箱、工号及证件组合现由服务端规范化/校验，并以 Host 目录全局唯一索引关闭双库并发竞态；失败资料写入会回滚整个用户事务。生产真实栈浏览器与 Linux 原生进程尚未认证，完成前不升 `Verified`。见[资料权威校验](../verification/2026-08-30-identity-authoritative-profile-validation.md)与[Excel/日志切片](../verification/2026-08-30-identity-excel-observability-log-control-plane.md)。 |
| Tenancy 生命周期与配额 | Build-verified | 租户创建、状态、解析与缓存失效已完成纵向切片；跨模块读取继续通过 Contracts 或投影演进。 |
| Enterprise 业务样板（F09–F11 子集） | Build-verified | `Presets.Enterprise`、demo CRUD、提交审批 + Workflow Sink、静态导入 Schema；证据见 [enterprise-business-integration-closeout](../verification/2026-09-17-enterprise-business-integration-closeout.md)。API 集成含 CRUD 与租户内定义发布后提交；明细行 API、附件、Reporting/Printing、Worker 审批回写 E2E 待补。 |
| SaaS 预设与订阅运营（F12 子集） | Build-verified | `Presets.Saas`（Platform + Payments + Webhooks）、Host 试用/取消订阅与 `TenantPackageId` 联动、Enforced 下 reactivate 商业化门禁、权益阶段 Host UI；证据见 [saas-preset-closeout](../verification/2026-09-17-saas-preset-closeout.md)。API 集成分片待 Docker/Testcontainers 可用；真实支付渠道 **External-auth-not-verified**。 |
| RBAC、菜单、页面与按钮级权限 | Build-verified | Vue 按稳定权限码不创建无权入口，后端 Endpoint 精确权限失败关闭，授权页按模块/页面/操作分层。 |
| Host / Tenant 字典 | Build-verified | 模块内查询、事务、双库和 Vue 管理页已形成完整切片。Host.Api Native AOT 双库 Settings HTTP/JSON 证据见 [`api-native-aot-settings-jobs-2026-08-25.md`](../verification/api-native-aot-settings-jobs-2026-08-25.md)。 |
| Host / Tenant 配置参数 | Build-verified | 权威写入与跨实例生效遵循版本化事件、缓存失效和本地投影标准。Host.Api Native AOT 双库 Settings HTTP/JSON 证据见 [`api-native-aot-settings-jobs-2026-08-25.md`](../verification/api-native-aot-settings-jobs-2026-08-25.md)。 |
| 枚举目录 | Build-verified | 机器码与翻译文本分离，客户端通过共享契约消费。 |
| Grid Preference | Build-verified | 读写和缓存已落地；缓存策略已迁入统一策略注册表，Architecture 手工策略 allowlist 为零（见 [`cache-policy-zero-allowlist-2026-08-08.md`](../verification/cache-policy-zero-allowlist-2026-08-08.md)）。 |
| Host / Tenant 审计与操作日志 | Build-verified | 审计、HTTP Operation Log 与 Outbox 职责已分离。 |
| 审计归档与保留 | Build-verified | 归档、导出、完整性与恢复边界已有验证；生产保留周期由运维配置。 |
| Organization 单位、职位与成员关系 | Build-verified | 模块内关联使用本模块 SQL 与事务；Identity 侧本地投影与 Host 对账端点（keyset、dry-run、apply）已落地，见 [`cursor-post-review-follow-up`](../superpowers/plans/2026-08-08-cursor-post-review-follow-up.md) Task 2–3。 |
| Files 本地存储、Provider 与上传状态机 | Build-verified | ProviderKey、Pending→Publishing→Ready、补偿与对账、双库迁移均已有验证。 |
| Document | Build-verified | 保留此前整模块状态。2026-10-24 第一批已实现并通过本地聚焦验收：完整 PUT/字段快照、独立状态命令、显式 ACL 与治理审计、持久化版本会话、版本高水位和幂等结果、事务 Outbox 引用释放、有界永久删除。通过本批 Unit、SQL Server/MySQL 业务及迁移恢复、运行时 OpenAPI/客户端生成、Vue 与受控 HTTP 的 Edge 桌面/移动交互、受影响架构和 AOT 静态分析；具体命令及边界见[第一批计划](../superpowers/plans/2026-10-24-document-batch-one.md)。第二批检索分页、当前/历史大小和 UTC 逻辑事件统计、分类并发防环与引用竞争、元数据清空及移动布局也已通过聚焦验收，见[第二批计划](../superpowers/plans/2026-10-24-document-batch-two.md)。第三批预览固定引用、同键上传恢复、最多3次领取、取消/人工重试、租约与清理竞争、API/Worker 共用 Provider 装配和独立最小删除事实90天保留均已实现并通过聚焦验收，见[第三批计划](../superpowers/plans/2026-10-24-document-batch-three.md)。不把分批验收升级为整模块 Verified；未执行全量或原生发布，保持 Capacity-not-verified、非 Production-verified。历史证据见 [`document-parity-2026-08-09.md`](../verification/document-parity-2026-08-09.md)、[`2026-09-22-document-verified-gates.md`](../verification/2026-09-22-document-verified-gates.md)。 |
| API Key、签名请求与模块目录 | Build-verified | 凭据、签名、模块发现和精确授权均有契约与安全测试。 |
| Notifications | Build-verified（功能与双库/Vue 已验；默认 Linux 原生发布/双库及 JS 预算已验；本机需稳定 CPU 条件） | **2026-10-23，`main@5093b6f4` 加工作区实现：** N01–N08 已接入实际 Intent/Worker 链路：首选激活与有界 Failover、唯一 Match 及条件编辑、回执补偿/CAS 复判与空闲后台恢复、持久化预算/Unknown；受控 Producer/Scene、最终同意复查、静默/过期、四维额度台账、小程序一次性本地许可、收件人语言/内容快照；归档、撤回与外发竞争及未使用预留释放、资源深链与缓存目标重新授权、统一工作台、看板和独立权限人工对账。最新双库纵向用例 2/2（含并发及 283/284 部分失败恢复），真实双库 OpenAPI 各 1/1；通知单测 195/195，架构 87 项覆盖通过（86 项及修正后 1 项），Vue 16 文件 99 项覆盖通过；真实基础页面各库 4/4，Match SQL Server 桌面 1/1、MySQL 桌面/手机/键盘/控制台 1/1。最新类型检查、生产构建及 Host.Api AOT 分析通过；此前 OOM/139/134 与两阶段对照保留。最新在用户授权稳定核心约束下，相同输入及 Linux SDK/ILC 10.0.402/10.0.13 完成完整编译、链接、publish（0 退出），142,770,016 字节新 ELF 的 Notifications SQL Server/MySQL HTTP/JSON/SignalR 2/2、零跳过，4 分 32.140 秒；未复用两阶段对象。前次 ILC 对照未确认编译器缺陷；后续发现并修复 Worker 权益端口注册缺失，新 Worker 默认 Linux 发布和双库治理/Workflow/PDF 六项 6/6 通过，真实通知告警触发/恢复 1/1 通过。共享组件修复后的 API 重发曾因 WSL VM 整体退出中断，默认链路旧输入重跑因同步远端而主动结束，未计通过；合并后完整闭包仍未原生验收。原进程及 92 线程亲和性、共享双库与 Redis 已恢复，旧产物证据不外推新闭包。首页静态 JS 经认证后按需加载 SignalR 后为 1,467,584/gzip 382,260 字节，既定预算通过；SignalR 延迟包 55,116/gzip 14,040、Chart 和 VForm 独立预算均通过。逐轮实际命令、失败及恢复见[实施计划](../superpowers/plans/2026-08-30-notifications-platform-extension.md#2026-10-23-本次实施与最终验收记录)。保持至少一次；单 Intent/Binding 为单渠道，不宣称跨渠道编排。五种现有适配器不等于真实送达认证，当前无厂商凭据，保留[SMTP 历史 External-auth-not-verified](../verification/2026-09-01-notifications-smtp-provider.md)；SMTP/企微/小程序无可信送达回执，容量 **Capacity-not-verified**。 |
| Jobs | Build-verified | 调度、重试、容量证据与 Worker 运行边界持续硬化；完整 1/2/4/8 容量矩阵可在本地按计划分批执行，以实际结果验收。Host.Api Native AOT 已在 SQL Server/MySQL 上通过定义、手动触发 ping、执行/计划/健康 HTTP JSON 外部进程验证，见 [`api-native-aot-settings-jobs-2026-08-25.md`](../verification/api-native-aot-settings-jobs-2026-08-25.md)；不外推 Worker 托管轮询或容量。 |
| SerialNumbers | Build-verified | Host/租户规则、纯预览、UTC 周期重置、幂等原子分配、分页筛选、稳定排序、Vue 表单边界与精确权限已落地；双库 Integration 与 admin-real-stack 仍需 fresh 全绿后才能升 `Verified`。见 [`serial-numbers-verified-20260820.md`](../verification/serial-numbers-verified-20260820.md)。 |
| Observability Admin 控制面 | Build-verified | 独立官方模块已交付固定日志根目录、顶层 `.log` 清单、稳定 SHA-256 文件 ID、有界尾读和流式下载；读/下载使用独立 Host 权限，Vue 不创建未授权下载入口，活动文件使用共享读取且客户端不能提交路径。实例/运行时硬件信息仍为 `Mapped`，Linux 原生进程 E2E 尚待 CI；见[本切片验证](../verification/2026-08-30-identity-excel-observability-log-control-plane.md)。 |
| 在线会话治理 | Build-verified | 会话状态、撤销与多实例协调已有基础能力。 |
| 受保护超级管理员 | Build-verified | 动态投影授权目录权限，仍受租户、会话、Endpoint、审计和最后一名保护约束。 |
| Vue 管理端主交付线 | Build-verified | `ui/admin` 是唯一持续交付的后台产品线；新增功能必须完成页面、按钮权限与真实后端联调。 |
| Vue 图表与富文本能力 | Designing | 仅在真实业务场景需要时选型并形成许可、包体、XSS 与可访问性门禁。 |
| Layui 管理端 | Frozen | `ui/admin-layui` 自 2026-08-02 起停止新功能开发，只接受明确授权的安全修复、迁移或退役任务。 |
| Vue 真实后端浏览器联调 | Build-verified | 核心授权、设置、TOTP、CodeGeneration 等链路已有真实栈记录；新增页面仍需按切片补充，不再要求 Layui 同步实现。 |
| uni-app 管理移动端基础能力 | Build-verified | 只维护经批准的移动场景，不复制完整桌面后台。 |
| Flutter 客户端 | Build-verified（Windows Release 与定向控件/HTTP/真实业务） | SDK 3.44.0 的 Windows 工程、正式本地化生成、密码登录 Origin 与 Workflow 表单/附件/明细已实现；本机 Release 构建、生成本地化直接边界17/17、登录及附件/签名HTTP 8/8通过。Windows Release对实际Linux原生API/Worker的业务1/1、零跳过、外层0：真实登录/本人待办、系统保存及打开对话框、附件复用/上传、表单/明细审批、真实字节和Worker续办结果通过，客户端正常退出0。Android/iOS/macOS/Linux不据此升级，容量及其它业务组合保留边界。证据与剩余项见[Workflow交接清单](../superpowers/plans/2026-10-28-workflow-cross-machine-handoff.md)。 |
| 全栈本地化 | Build-verified | 使用规范 BCP 47 标签和稳定机器码；新增模块按跨端验证更新状态。 |
| Baseline + Overlay 种子体系 | Build-verified | Production 只允许 Baseline，API/Worker 禁止启动播种，Contributor 必须幂等并通过双库验证。 |
| SignalR 实时通知 | Build-verified | 路径、鉴权、Backplane、连接指标和失败关闭已完成持续硬化；Host.Api Native AOT 已通过 SQL Server/MySQL + Redis 的 JSON Hub 原生进程收发，仍不代表多节点 Native Backplane 投递认证。见 [`api-native-aot-publish-2026-08-23.md`](../verification/api-native-aot-publish-2026-08-23.md)。 |
| Host.Api Native AOT | Build-verified | **本次默认 Linux SDK/ILC `Aot-published`（受控 CPU，Notifications 范围）**：2026-10-23 最新 Host.Api AOT 分析 0 警告/0 错误；相同 484 输入与同版本 Linux ILC 完整编译、链接及 publish 成功，9 条既有告警准入，142,770,016 字节新 ELF 的 Notifications 双库 HTTP/JSON/SignalR 2/2、零跳过及启动停止通过（4 分 32.140 秒）。未使用两阶段对象替代门禁，进程及 92 线程已恢复原亲和性。旧 OOM/139/134 和两阶段对照保留；本机仍需稳定 CPU 条件，未发现需修产品代码或升级编译器的证据，见[本次记录](../superpowers/plans/2026-08-30-notifications-platform-extension.md#2026-10-23-本次实施与最终验收记录)。历史完整 net10.0 API 闭包曾通过 Linux 原生 publish、启动、双库关键 HTTP、双库 SignalR JSON 与架构门禁；Notifications 双库原生证据为 [run 32849677783](https://github.com/yan041108/Full.NET/actions/runs/32849677783)；Settings/Jobs 双库原生证据为 [run 32872774812](https://github.com/yan041108/Full.NET/actions/runs/32872774812)。后续 Worker 默认 Linux 发布及双库通知治理/Workflow/PDF 六项已验；共享 Tenancy 修复后的 API 重发因同步远端主动结束，合并后完整原生闭包尚待验收，不能把旧 API 二进制证据当作当前闭包。Migrator 原生与生产容量仍未验证。见 [`ADR-0008`](../architecture/adr/ADR-0008-api-native-aot-runtime-boundary.md)、[`api-native-aot-publish-2026-08-23.md`](../verification/api-native-aot-publish-2026-08-23.md)、[`api-native-aot-notifications-2026-08-25.md`](../verification/api-native-aot-notifications-2026-08-25.md) 与 [`api-native-aot-settings-jobs-2026-08-25.md`](../verification/api-native-aot-settings-jobs-2026-08-25.md)。 |
| Worker Native AOT | Build-verified | **合并前冻结输入默认 Linux 发布及选定双库行为已验（未覆盖合并后完整 Worker）**：2026-10-23 修复后台权益端口注册后，Linux SDK/ILC 10.0.402/10.0.13 完整 publish 退出 0，7 条既有告警准入；ELF SHA-256 `2546357779c45291f22dfb6f1b10f2d8d11d3d548f2c624becd848f9e155813c`。当前通知治理/预算恢复、Workflow 通知投影、PDF 恢复双库 6/6、零跳过，16 分 58.732 秒，正常停止；首轮完整 Worker 28/29、MySQL PDF 失败及后续补跑保留，不能宣称完整闭包全绿。详情见[原实施计划](../superpowers/plans/2026-08-30-notifications-platform-extension.md)。历史边界：2026-09-19 查询确认 [run 35164362025](https://github.com/yan041108/Full.NET/actions/runs/35164362025) 在 `a6d3d02e` 成功；不可外推到 `4ad8f039`。AOT/Trim 与静态 SQL、JSON、物化器门禁通过；Phase 1–7 已建立隔离 publish、双库一次性扫描、常驻空载/Jobs 心跳/SIGTERM、Legacy Outbox 成功/损坏载荷死信、Jobs Ping 自动领取成功终态，以及 Files 本地 Pending 对账、已删除 Blob Cleanup 和 Reference Claim 晋升/释放外部进程 CI，历史对应闭包可引用上述 Linux CI，当前新增路径仍须当前提交原生证据，不能仅凭本机分析升级状态。见 [`ADR-0010`](../architecture/adr/ADR-0010-worker-native-aot-analysis-boundary.md)、[Phase 0](../verification/2026-08-29-worker-native-aot-phase0.md)、[Phase 1](../verification/2026-08-29-worker-native-aot-phase1.md)、[Phase 2](../verification/2026-08-29-worker-native-aot-phase2.md)、[Phase 3](../verification/2026-08-29-worker-native-aot-phase3.md)、[Phase 4](../verification/2026-08-29-worker-native-aot-phase4.md)、[Phase 5](../verification/2026-08-29-worker-native-aot-phase5.md)、[Phase 6](../verification/2026-08-29-worker-native-aot-phase6.md) 与 [Phase 7](../verification/2026-08-29-worker-native-aot-phase7.md) 验证记录。 |
| Host.Api Native AOT S3 Provider | Build-verified | **`Native-provider-verified: s3`**：Linux Native Host.Api + SQL Server/MySQL 文件元数据 + 真实 MinIO S3 HTTP 上传/下载/删除已通过；AWS Workload Identity、实例角色与 Web Identity 未验证。见 [`ADR-0009`](../architecture/adr/ADR-0009-host-api-native-aot-provider-runtime-boundary.md) 与 [Phase 3 记录](../verification/api-native-aot-phase3-providers-2026-08-24.md)。 |
| Host.Api Native AOT Kafka Replay | Build-verified | **`Native-provider-verified: kafka-replay`**：Linux Native Host.Api + 真实 Kafka 范围重放在 SQL Server/MySQL 下已通过。仅覆盖 API Replay；不覆盖 Worker Producer/Consumer、CDC Relay、DLQ 或 Lag Observer。见 [`ADR-0009`](../architecture/adr/ADR-0009-host-api-native-aot-provider-runtime-boundary.md) 与 [Phase 3 记录](../verification/api-native-aot-phase3-providers-2026-08-24.md)。 |
| gRPC 服务契约 | Planned | 只有明确的进程间高吞吐或流式需求才进入实现。 |
| AI 模型、聊天与 Agent 运行基础 | Build-verified（已交付范围，非整体 Verified） | 2026-10-19 核对：Provider、预算、持久 Run/租约/检查点/审批、回执对账、精确输入批准及阶段观测已有实现与分范围证据；最近关联批次已提交 main `cad1998fd`。真实供应商、并行编排、跨模块接收方测试、剩余恢复及容量按[唯一待办](../superpowers/plans/2026-09-08-ai-agentic-web-alignment.md) AI-D01/D02、AI-A01/A06/A07 推进。 |
| MCP Server 与 Agent Tools | Build-verified（受控范围，非整体 Verified） | 静态工具目录、逐次授权、审批/审计、标准 MCP SDK 双库互操作和受控远端调用已有记录；最终 Native 目录/元数据探针也已覆盖，不再重复标为待发布。AG-UI 外部客户端与 MCP 未核证故障矩阵归[活动计划](../superpowers/plans/2026-09-08-ai-agentic-web-alignment.md) AI-A04；不扩大生产写工具暴露。 |
| AI 知识库与带引用问答 | Build-verified（现有格式）；多格式及产品完善 Planned | 目录和两层授权、文本/PDF/DOCX 有界解析与索引、收费账本、按权限检索/来源依赖传播、版本切换、删除清理、独立重建、增量意图/恢复/元数据事件及显式收费授权已交付。双库与 Native 证据按[最终验收](../verification/2026-10-19-ai-final-development-acceptance.md)所列范围适用；真实模型质量、完整原生成功联动、真实页面及容量分别归 AI-A01/A02/A03/A05/A07，不能整体声称 Verified。2026-10-19 新增 doc、Excel、PPT、CSV、HTML、多格式图片/扫描 PDF OCR，按 AI-F01–F05 待开发/验收，音视频暂不纳入。产品设计 AI-K01–K06 补充目录/发布治理、解析校对、可解释检索、FAQ、质量运营和内部业务助手，均未实现；外部连接器/Wiki/视觉检索/GraphRAG 仅为候选，详见[规格 §3.7](../superpowers/specs/2026-09-08-ai-agentic-web-security-runtime-design.md#37-知识库产品完善与-weknora-能力吸收2026-10-19产品设计--planned)。 |
| AI 质量评估、Prompt 发布与受控路由 | Build-verified（离线/受控范围，真实质量未验） | 冻结语料/离线评分器、不可变 Prompt/Schema/工具与检索策略绑定、反馈审核和评估准入、节点恢复、受控路由及观测均已实现；不再列为 Planned。真实检索/模型端到端基线仍 `not_measured`，基础/融合/重排只按同输入有效证据激活，质量与页面余项见[活动计划](../superpowers/plans/2026-09-08-ai-agentic-web-alignment.md) AI-A02/A03。 |
| Workflow | In progress（定向修复和验收持续收口） | WF-R1–R3及WF-R4子流程、多实例、动态路由、增强退回、双边协作授权/执行/结果/取消/Files已按关联增量取得定向双库与客户端证据；公共类型20已开放。WF-R5固定版本对比、复用真实规则的发布前仿真、停留/SLA/积压及恢复导航已实现并取得定向双库、Vue和AOT证据。API/Worker实际Linux ELF已发布，R5原生API双库2/2、协作API/Worker原生双库2/2分别通过。随后发现的分页AOT映射已修复并重新发布；新鲜SQL Server/Windows真实业务1/1、MySQL原生API分页1/1（Host/Tenant实际待办及已办）通过。完整协作分页/取消/撤权组合最新真实双库2/2、零跳过通过，冻结闭包及资源回收均已核对，不外推其它组合、平台或容量。2026-10-30授权的冻结范围独立审查及唯一P2修复静态复审已完成；草稿仿真缺少发起人选人的问题已修复，真实双库2/2、关联Vue20/20、双库协议生成/零漂移、Vue与SDK类型及真实Edge1/1通过。随后保留严格输入守卫，复用修复完整OpenAPI元数据后的新API/Worker ELF；选人仿真Native双库2/2、真实MinIO/S3协作消费者双库2/2通过，7项本批TRX及资源回收均已核对。S3范围为API实际字节与Worker协作链路，不声明Worker直接Blob I/O；2026-10-31的303冻结范围已补证物理Kafka基本Native双库2/2、本地Files/两层子代/目标双实例组合Native双库2/2及真实四流告警触发/恢复，均零跳过；当前多流迟到REST纠偏另行实现，40项定向单测及当前工具版本的Native双库2/2、零跳过通过，覆盖双API失锁、迟到pause/resume和跨流失败隔离。新S3/Kafka、348、发布/资源策略及本次纠偏不在旧839文件独立审查范围；新增量只读独立审查发现切流失锁提交、同表事件/版本路由隔离、消费组统计完整性和发布工具闭包等缺口；切流及回退/恢复事务绑定锁、指标口径和过滤器已修复，回退/恢复失锁JIT双库各7/7及含类型/版本探针的JIT物理Kafka双库定向通过，回退/恢复本批修改之前的Worker Native发布通过，API Native在原12 GiB预算下失败，调整至16 GiB后旧输入发布通过但因源码变化未用于运行；449重新发布当前输入并通过输入及工具守卫，其自动续行的Native API R5在SQL Server、MySQL各1/1通过、零跳过，外层退出0；该结果不覆盖Native物理Kafka；提交成功响应丢失已取得JIT双库各1/1证据；SQL Server真实同事务部分位点及完整回退1/1通过，实际心跳在无新增Outbox时被观察到；452物理测试另证目标Outbox/影子捕获流空闲而独立CDC捕获实例有事务时，真实心跳后Connector持久LSN推进并覆盖该事务，1/1通过；业务捕获流完全空闲时，SQL Server 由受控单行心跳事务驱动独立CDC捕获实例，固定动作及实例绑定错误失败关闭；当前输入的SQL Server Native自驱心跳与MySQL Native配对迁移/直接链路各1/1通过、零跳过，源码与资源守卫及外层退出0；该机制主动产生基础设施捕获事务，不能宣称零捕获事务也会生成新LSN；跨流旧Connector迟到REST的真实双库物理定向各1/1通过；本批修复的只读独立审查已重点抽查，发现固定Topic正则与附加字段映射两项缺口，均已修复并完成静态复核，相关定向测试12/12通过、零跳过；修复后重新发布API与Worker Native，当前冻结输入的基础物理Kafka在SQL Server、MySQL各1/1通过、零跳过，运行前后守卫及外层退出0；本地Files/两层子代/并行目标组合Native双库各1/1及目标流空闲、独立CDC实例有事务时的SQL Server Native心跳位点推进1/1通过，首轮MySQL资源守卫失败原因不明并保留；不外推全部组合或生产等价环境；新心跳代码已完成定向只读独立审查，发现SQL Server心跳说明遗漏Id列SELECT权限，已修正文档；SQL Server Native中仅UPDATE拒绝、增加Id SELECT后更新成功，非受限Connector完整端到端；最新切流失锁修复的JIT双库断连与锁生命周期已通过，当前输入API/Worker Native已重新发布，物理Kafka SQL Server自主心跳及MySQL切流各1/1、零跳过，前后守卫及批次退出0；随后新增Native物理Kafka持锁会话故障注入，SQL Server/MySQL各1/1、零跳过：确认未提交所有权及审计后KILL实际持锁连接，验证旧请求失败、事务回滚、唯一成功重试及后续业务链路，前后守卫和资源回收通过；只覆盖切流，不外推其他控制操作；既有定向结果为对应冻结范围的历史事实，不能外推审查后修改。生产容量/SLO及未声明平台继续保留。整体不声明Workflow Verified或WF-R4/WF-R5完成。实际快照、命令及历史失败见[交接清单](../superpowers/plans/2026-10-28-workflow-cross-machine-handoff.md)和[实施计划](../superpowers/plans/2026-10-24-workflow-node-completion.md)。 |
| VForm 3 完整源码与设计资产 | Build-verified（定向范围验收通过） | 固定来源 338 文件、27 个添加入口及全部原版属性/布局/工具栏/Renderer 已接入隔离宿主，无损资产 API/双库与实际页面保存发布已验。组件身份/路径/codec/绑定修订、Web 目标、实际产物摘要及运行不匹配拒绝已接入；23 字段属性编辑、跨列拖动/复制删除/合并回显、生产包 12 项及新增抽屉回归通过。最终管理端 29 项、服务端 19 项、双库 2/2、真实页面审批/数据库回读 1/1、Host.Api 完整 AOT 分析通过，见[记录](../verification/2026-10-24-vform3-full-source-migration.md)。此前对应输入已完成 Linux Native AOT 发布与 VForm 原生双库 2/2，并修复校验错误序列化故障（托管回归 17/17）。独立只读审查及修复复审已完成，时间精度、挂载初值与客户端对象边界已修复。协调冻结后的当前输入已通过 Linux Native AOT 发布、VForm 原生双库 2/2、托管双库 2/2、严格 CSP 真实栈 1/1；最新管理端 104/104、Todo 契约 27/27 与生产构建通过，早前数量保留为历史范围，证据见同一记录。未宣称全部生产绑定或其它原生路径，不整体升级 Workflow Verified。 |
| Admin.NET 功能吸收 | Build-verified | 已完成首轮设计吸收与多个纵向切片；2026-08-30 将 Admin.NET.Pro `v2.1` 基线更新至 `09d38bd8`，自 `3879b035` 累计审计 59 个提交。Identity Excel、Host 用户资料权威校验与 Observability Admin 日志控制面已按 Full.NET 安全边界交付；当前明确缺口包括 Notifications 强类型扩展元数据和 MCP 安全/AOT 设计。后续按 [`adminnet-feature-parity.md`](adminnet-feature-parity.md) 逐模块交付，不承诺代码逐行复制。见[资料权威校验](../verification/2026-08-30-identity-authoritative-profile-validation.md)。 |
| k6 与生产容量认证 | Implemented | [`eng/load`](../../eng/load/README.md) 已提供工具、阈值和报告能力；生产等价环境认证前统一标记 `Capacity-not-verified`。 |

## 2026-09-16 当前建设优先级

1. 先收口所选交付预设的安全/隔离阻塞和已有模块证据，保持 SSO、AI、全项目修复等原专项的唯一任务所有权。
2. P0 补齐项目创建/升级工具链、企业成员生命周期、套餐权益/配额、账号自助恢复；已有套餐目录、注册政策或局部配额不等于这些完整流程已交付。
3. P1 交付订阅运营、开放集成、业务样板及发布恢复包；ImportExport/Reporting/Printing、Workflow、Notifications 均复用现有实现，先核对再补缺口。
4. 以基础管理、企业应用、SaaS 预设分别验收，不要求所有可选模块同时进入核心 1.0；每个预设仍覆盖所含模块的完整依赖、安全和恢复门禁。

任务映射、实际路径、依赖、失败场景和阶段出口统一在[开发计划](../superpowers/plans/2026-09-16-foundation-productization.md)维护，新增增量当前为规划状态；发布与容量认证仍按独立证据判断。

## 2026-08-08 后续优先级（历史队列，保留未关闭证据）

### P0：引用一致性与并发证明

1. ~~为 Files Claim 与文件删除建立 SQL Server/MySQL 真实并发矩阵，统一文件行锁顺序，证明结果只能是“Claim 成功、删除冲突”或“删除成功、Claim 失败”。~~ **已完成**（2026-08-16；`DocumentFilesReferenceClaim_race_is_atomic_*`、无门闩竞争与 HTTP 删除变体）。
2. 保持已落地的 NuGet/npm 漏洞失败关闭、权威 Markdown UTF-8 和内部链接门禁，不以本轮完成结论移除持续检查。

### P1：模块边界与一致性

1. ~~将 Identity 的 Organization 单位投影事件/回填 Port 收敛为消费方最小契约~~ **已完成**（2026-08-08 后续计划 Task 2；Architecture 反向契约目录为空）。
2. ~~把 Identity 机构投影回填升级为 keyset、断点、dry-run、apply 与差异对账的有界 Host 运维能力~~ **已完成**（Task 3；`/api/v1/identity/organization-unit-projections/reconcile`）。
3. ~~将 Layui 从活动客户端测试、构建、E2E 与包体门禁移出~~ **已完成**（2026-08-08；见 [`layui-active-gate-retirement-2026-08-08.md`](../verification/layui-active-gate-retirement-2026-08-08.md)）。

### P2：契约与演进

1. 按 ADR-0006/专门计划建立 Event Envelope V2、追加式 Outbox、双库 Inbox、Kafka Provider 和双库 CDC Shadow；Organization 试点流故障矩阵（MySQL + SQL Server 对称 DataRow）已部分完成；正式切流前仍需 Soak 与 nightly SQL Server 绿。
2. ~~为 Vue API 模块建立 OpenAPI、共享 TypeScript 契约与调用点之间的覆盖门禁~~ **已完成**（2026-08-16；`validate-vue-api-call-site-coverage.mjs` + `vue-client-coverage-v1.json` consumerModules/infrastructureModules + `OverviewView` 改用 `api/me.ts`）。
3. 保留 Integration Event 并行版本、精确路由、consumer-first 和退役扫描；只有出现首个真实非加法升级时才实现相邻版本 upgrader，并以真实事件完成 v1→v2 演练。
4. 在生产等价环境完成 MySQL UUID 迁移维护窗口、备份恢复和 RPO/RTO 演练。

## 当前发布表述

Full.NET 当前是建设中的 .NET 10 强化型模块化单体平台。核心后台能力已有较广的 `Build-verified` 覆盖，但项目仍不是完整 Admin.NET 复刻，也尚未取得统一的 `Production-verified` 容量与灾备认证。任何对外发布说明都必须保留这一边界。
