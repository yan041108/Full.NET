# Full.NET 能力状态矩阵

> 更新时间：2026-08-10。本文只维护能力状态、稳定证据入口与后续优先级；可变测试数量统一以 [`eng/testing/test-matrix.json`](../../eng/testing/test-matrix.json) 为准。

## 状态定义

- `Planned`：已有方向，但尚未形成可执行设计。
- `Designing`：规格、ADR 或实施计划正在收敛。
- `Implemented`：实现已经落盘，尚未完成规定的构建或契约验证。
- `Build-verified`：已通过与当前能力相称的构建、单元、架构或双库验证。
- `Production-verified`：已在生产等价环境完成容量、故障、恢复与运维验收。
- `Frozen`：只允许明确授权的安全修复、迁移或退役工作。
- `Deferred`：经架构决策暂不进入当前版本。

`Build-verified` 不等于 `Production-verified`。容量、备份恢复、滚动升级、外部依赖故障和真实流量行为仍须在生产等价环境单独认证。

## 当前能力矩阵

| 能力 | 状态 | 稳定证据与剩余边界 |
|---|---|---|
| 模块化单体、API/Worker/Migrator 运行角色 | Build-verified | [总体架构规格](../superpowers/specs/2026-07-17-fullnet-architecture-design.md)、[`ADR-0002`](../architecture/adr/ADR-0002-modular-monolith-evolution.md) 与 Architecture 门禁共同约束；尚未触发全面微服务拆分门槛。 |
| 命名、迁移与 CRUD 生成治理 | Build-verified | 统一由 [`rules/naming-conventions.md`](../../rules/naming-conventions.md)、迁移命名测试和 CodeGeneration 契约门禁约束。 |
| Dapper、租户 SQL 与命令事务边界 | Build-verified | 模块内强事务已形成统一边界，`module-local-transaction-debt.json` 已清零；Identity 消费 Organization 投影契约仍形成一项精确的反向模块契约债务，后续按 [Cursor 审查计划](../superpowers/plans/2026-08-08-cursor-post-review-follow-up.md)退役。 |
| UUID v7 逻辑主键与双库物理映射 | Build-verified | SQL Server `uniqueidentifier` 与 MySQL `binary(16)` 已由 008/009 扩展—回填—收缩迁移及恢复测试覆盖；生产维护窗口、备份和 RPO/RTO 演练仍待环境验收。 |
| SQL Server/MySQL 成对迁移 | Build-verified | 迁移命名、顺序、恢复和双 Provider 集成测试已形成门禁。 |
| 事务 Outbox、租约、重试与死信 | Build-verified | 仅承载需要事务原子性的重要 Integration Event；缓存失效、日志、Trace、Metrics 与普通审计禁止进入 Outbox。 |
| CDC Relay / Kafka | Designing / Shadow-only | 2026-08-09 复审降回 Designing / Shadow-only；无生产等价的生产者→CDC→Kafka→Inbox→消费者认证证据，见 docs/verification/cdc-kafka-pilot-2026-08-08.md。2026-08-10 保持 Full.NET 自有主链路并将 Wolverine 固定为参考/性能对标；Cooperative Sticky 离线迁移门禁、Static Membership、Kafka 4 Consumer Protocol、Producer 有界批量、Key 固定槽并行、统一高低水位背压、可配置连续 Offset Commit、双库 Inbox 只读批量预检、一次性范围重放、Handler 编译期注册表及低基数 Activity/Gauge 已完成构建和聚焦集成验证。安全默认仍为 Legacy Range、单槽、分区容量 `1/0` 和 `PerMessage`；API 只注册重放操作且默认关闭，显式启用仍受 1000 条/32 分区/45 秒配置上限、单次原生调用余量与终态审计约束，不启动常驻 Consumer。真实 CDC 全链路、生产等价容量、Soak、N+1 与恢复演练仍未完成，整体继续 `Capacity-not-verified`，不得执行正式切流。实施与剩余门禁见[专项计划](../superpowers/plans/2026-08-10-wolverine-reference-kafka-hardening.md)。 |
| Kafka Capacity Runner | Build-verified | Scope A `kafka_transport`、Scope B `worker_inbox_handler` 与 Scope C `transaction_outbox_cdc` 已实现。Scope B 复用生产分区有界调度、连续 Offset 水位、Envelope、Dapper Inbox 事务、Dispatcher、Handler、Retry/DLQ 处理核心；Scope C 追加业务事务 Outbox、外部 Connect REST、Debezium 容量 Connector（`fullnet.capacity.cdc.*`）与 `outboxCdc` 扩展证据，消费侧复用 Scope B 内核。Scope B/C 在 Kafka I/O 前验证专用数据库身份、schema、Outbox（Scope C）与 CdcKafka 所有权；MySQL + 真实 Kafka/Connect 缩减集成测试已通过，Scope C SQL Server 在 Testcontainers CDC 不可用时 Inconclusive。Scope A 手动工作流保持 A-only；正式生产等价矩阵、Soak、N+1、故障和恢复认证仍未执行，状态继续 `Capacity-not-verified`。使用边界见[运维手册](../operations/kafka-capacity-runner.md)。 |
| FusionCache 多实例缓存治理 | Build-verified | 当前写路径使用提交后本实例 L1/L2 删除、Redis Backplane 与 TTL/版本兜底；Tenancy 与 Grid Preference 已纳入统一策略注册表，Architecture 手工策略 allowlist 为零；旧缓存事件处理器只作兼容排空。 |
| 健康检查与运行角色就绪探针 | Build-verified | API、Worker、Migrator 和关键基础设施有独立就绪语义；生产阈值仍由环境配置验收。 |
| HTTP 状态码、ProblemDetails 与兼容包络 | Build-verified | 标准 API 默认采用状态码与 ProblemDetails；Admin.NET 包络仅允许存在于兼容适配层。 |
| System.Text.Json、OpenAPI 与 Vue 调用契约覆盖 | Build-verified | Architecture 测试按生产 Endpoint 元数据枚举 JSON 类型；Vue 生产 API 文件必须逐项映射 OpenAPI fixture 与共享 TypeScript 契约，新增漏项失败关闭；完整生成式 SDK 不属于当前完成门槛。 |
| 结构化日志、OpenTelemetry 与低基数指标 | Build-verified | 指标、Trace、日志职责分离；生产采集、告警和保留策略仍需部署环境验收。 |
| 可信代理与转发头边界 | Build-verified | 由主机配置、边界测试与运维基线共同约束。 |
| Identity 会话、刷新令牌、MFA 与 TOTP | Build-verified | 单元、集成及生产配置真实栈 TOTP 浏览器链路已有验证记录；不等于生产环境认证。 |
| Tenancy 生命周期与配额 | Build-verified | 租户创建、状态、解析与缓存失效已完成纵向切片；跨模块读取继续通过 Contracts 或投影演进。 |
| RBAC、菜单、页面与按钮级权限 | Build-verified | Vue 按稳定权限码不创建无权入口，后端 Endpoint 精确权限失败关闭，授权页按模块/页面/操作分层。 |
| Host / Tenant 字典 | Build-verified | 模块内查询、事务、双库和 Vue 管理页已形成完整切片。 |
| Host / Tenant 配置参数 | Build-verified | 权威写入与跨实例生效遵循版本化事件、缓存失效和本地投影标准。 |
| 枚举目录 | Build-verified | 机器码与翻译文本分离，客户端通过共享契约消费。 |
| Grid Preference | Build-verified | 读写和缓存已落地；缓存策略仍需从 Architecture allowlist 迁入统一策略注册表。 |
| Host / Tenant 审计与操作日志 | Build-verified | 审计、HTTP Operation Log 与 Outbox 职责已分离。 |
| 审计归档与保留 | Build-verified | 归档、导出、完整性与恢复边界已有验证；生产保留周期由运维配置。 |
| Organization 单位、职位与成员关系 | Build-verified | 模块内关联使用本模块 SQL 与事务；跨 Identity 引用仍需通过投影和对账退役存量本地事务债务。 |
| Files 本地存储、Provider 与上传状态机 | Build-verified | ProviderKey、Pending→Publishing→Ready、补偿与对账、双库迁移均已有验证。 |
| Document | Implementing | 2026-08-09 复审：安全分享协议与 Vue 管理端尚未通过，先失败关闭部分路径，等 Document 收口计划全部门禁通过再升 Build-verified。文档版本与附件关联及 Document→Files claim/reconcile 已落地，跨模块本地事务债务清零；审查已补回 Files 事务内删除保护、条件 Claim/删除和 Released 终态失败关闭，双 Provider 聚焦切片通过，真实高竞争矩阵仍列为 P0。 |
| API Key、签名请求与模块目录 | Build-verified | 凭据、签名、模块发现和精确授权均有契约与安全测试。 |
| Notifications | Build-verified | Inbox 与 SignalR 分层；发送路径仍有一项跨模块本地事务债务待移出事务或改为投影。 |
| Jobs | Build-verified | 调度、重试、容量证据与 Worker 运行边界持续硬化；完整 1/2/4/8 容量矩阵只在专用环境执行。 |
| 在线会话治理 | Build-verified | 会话状态、撤销与多实例协调已有基础能力。 |
| 受保护超级管理员 | Build-verified | 动态投影授权目录权限，仍受租户、会话、Endpoint、审计和最后一名保护约束。 |
| Vue 管理端主交付线 | Build-verified | `ui/admin` 是唯一持续交付的后台产品线；新增功能必须完成页面、按钮权限与真实后端联调。 |
| Vue 图表与富文本能力 | Designing | 仅在真实业务场景需要时选型并形成许可、包体、XSS 与可访问性门禁。 |
| Layui 管理端 | Frozen | `ui/admin-layui` 自 2026-08-02 起停止新功能开发，只接受明确授权的安全修复、迁移或退役任务。 |
| Vue 真实后端浏览器联调 | Build-verified | 核心授权、设置、TOTP、CodeGeneration 等链路已有真实栈记录；新增页面仍需按切片补充，不再要求 Layui 同步实现。 |
| uni-app 管理移动端基础能力 | Build-verified | 只维护经批准的移动场景，不复制完整桌面后台。 |
| Flutter 客户端 | Designing | 需按真实移动/桌面场景、共享契约和发布许可单独立项。 |
| 全栈本地化 | Build-verified | 使用规范 BCP 47 标签和稳定机器码；新增模块按跨端验证更新状态。 |
| Baseline + Overlay 种子体系 | Build-verified | Production 只允许 Baseline，API/Worker 禁止启动播种，Contributor 必须幂等并通过双库验证。 |
| SignalR 实时通知 | Build-verified | 路径、鉴权、Backplane、连接指标和失败关闭已完成持续硬化。 |
| gRPC 服务契约 | Planned | 只有明确的进程间高吞吐或流式需求才进入实现。 |
| AI 能力 | Planned | 必须先确定数据边界、审计、模型供应、成本与降级策略。 |
| Admin.NET 功能吸收 | Build-verified | 已完成首轮设计吸收与多个纵向切片；后续按 [`adminnet-feature-parity.md`](adminnet-feature-parity.md) 逐模块交付，不承诺代码逐行复制。 |
| k6 与生产容量认证 | Implemented | [`eng/load`](../../eng/load/README.md) 已提供工具、阈值和报告能力；生产等价环境认证前统一标记 `Capacity-not-verified`。 |

## 2026-08-08 后续优先级

### P0：引用一致性与并发证明

1. 为 Files Claim 与文件删除建立 SQL Server/MySQL 真实并发矩阵，统一文件行锁顺序，证明结果只能是“Claim 成功、删除冲突”或“删除成功、Claim 失败”。
2. 保持已落地的 NuGet/npm 漏洞失败关闭、权威 Markdown UTF-8 和内部链接门禁，不以本轮完成结论移除持续检查。

### P1：模块边界与一致性

1. 将 Identity 的 Organization 单位投影事件/回填 Port 收敛为消费方最小契约，移除当前精确反向契约债务，恢复模块依赖 DAG 无例外。
2. 把 Identity 机构投影回填从仅供测试调用的内部循环升级为 keyset、断点、dry-run、apply 与差异对账的有界 Host 运维能力。
3. 将 Layui 从活动客户端测试、构建、E2E 与包体门禁移出，只保留冻结目录失败关闭和显式维护例外。

### P2：契约与演进

1. 按 ADR-0006/专门计划建立 Event Envelope V2、追加式 Outbox、双库 Inbox、Kafka Provider 和双库 CDC Shadow；完成故障矩阵前不启用正式业务 Consumer。
2. 为 Vue API 模块建立 OpenAPI、共享 TypeScript 契约与调用点之间的覆盖门禁；在出现真实多客户端或外部 SDK 需求前，不机械引入完整生成式 SDK。
3. 保留 Integration Event 并行版本、精确路由、consumer-first 和退役扫描；只有出现首个真实非加法升级时才实现相邻版本 upgrader，并以真实事件完成 v1→v2 演练。
4. 在生产等价环境完成 MySQL UUID 迁移维护窗口、备份恢复和 RPO/RTO 演练。

## 当前发布表述

Full.NET 当前是建设中的 .NET 10 强化型模块化单体平台。核心后台能力已有较广的 `Build-verified` 覆盖，但项目仍不是完整 Admin.NET 复刻，也尚未取得统一的 `Production-verified` 容量与灾备认证。任何对外发布说明都必须保留这一边界。
