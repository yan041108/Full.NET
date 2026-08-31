# Full.NET 能力状态矩阵

> 更新时间：2026-08-31。本文只维护能力状态、稳定证据入口与后续优先级；可变测试数量统一以 [`eng/testing/test-matrix.json`](../../eng/testing/test-matrix.json) 为准。

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
| Dapper、租户 SQL 与命令事务边界 | Build-verified | 模块内强事务已形成统一边界；三份 architecture 债务目录（local-transaction / table-access / cross-foreign-key）与 `AllowedReverseContractDependencies` 均已清零。Organization 单位投影采用消费方拥有的 `Identity.Contracts` Port + Organization 侧适配器，模块依赖 DAG 无登记例外。 |
| UUID v7 逻辑主键与双库物理映射 | Build-verified | SQL Server `uniqueidentifier` 与 MySQL `binary(16)` 已由 008/009 扩展—回填—收缩迁移及恢复测试覆盖；生产维护窗口、备份和 RPO/RTO 演练仍待环境验收。 |
| SQL Server/MySQL 成对迁移 | Build-verified | 迁移命名、顺序、恢复和双 Provider 集成测试已形成门禁。 |
| 事务 Outbox、租约、重试与死信 | Build-verified | 仅承载需要事务原子性的重要 Integration Event；缓存失效、日志、Trace、Metrics 与普通审计禁止进入 Outbox。 |
| CDC Relay / Kafka **（Delivery 路径）** | Build-verified / Pilot | Organization 真实 API 写路径 + `OrganizationUnitCdcKafkaEndToEndTests` / 故障矩阵已落地；MySQL 本地 E2E 在 outbox 路由修复后可达 Inbox 前段，Debezium 发布仍依赖 Docker 环境稳定性；SQL Server 对称证据需 `FULLNET_TEST_SQLSERVER_CDC_CONNECTION_STRING` + Agent。**仍禁止** `DeliveryCutover:Enabled=true` 与 Production-verified。见 [`cdc-kafka-pilot-2026-08-08.md`](../verification/cdc-kafka-pilot-2026-08-08.md)。 |
| Kafka Capacity Runner **（测量工具，非 Delivery）** | Build-verified | **工具可用** ≠ Delivery 可切流。Scope A/B/C 已实现；Scope B/C 复用生产 Inbox/Dispatcher 核心，Scope C 追加 Outbox+Connect+CDC。MySQL 缩减集成测试已通过；SQL Server CDC 在 Testcontainers 上 Inconclusive。正式生产等价矩阵、Soak、N+1、故障恢复 **未执行**，测量结果继续 `Capacity-not-verified`。见 [`kafka-capacity-runner.md`](../operations/kafka-capacity-runner.md) 与 [`messaging-runtime-topology.md`](../operations/messaging-runtime-topology.md)。 |
| FusionCache 多实例缓存治理 | Build-verified | 当前写路径使用提交后本实例 L1/L2 删除、Redis Backplane 与 TTL/版本兜底；Tenancy 与 Grid Preference 已纳入统一策略注册表，Architecture 手工策略 allowlist 为零；旧缓存事件处理器只作兼容排空。 |
| 健康检查与运行角色就绪探针 | Build-verified | API、Worker、Migrator 和关键基础设施有独立就绪语义；生产阈值仍由环境配置验收。 |
| HTTP 状态码、ProblemDetails 与兼容包络 | Build-verified | 标准 API 默认采用状态码与 ProblemDetails；Admin.NET 包络仅允许存在于兼容适配层。 |
| System.Text.Json、OpenAPI 与 Vue 调用契约覆盖 | Build-verified | Architecture 测试按生产 Endpoint 元数据枚举 JSON 类型；Vue 生产 API 文件必须逐项映射 OpenAPI fixture 与共享 TypeScript 契约，新增漏项失败关闭。OpenAPI 驱动客户端生成三类试点已 `Pilot-passed`；**Vue 生产 API 全量迁移已收官**（现 230 条 `generated`，45 个 API 模块），见 [`openapi-client-generation-pilot-2026-08-21.md`](../verification/openapi-client-generation-pilot-2026-08-21.md) 与 [`openapi-client-generation-migration-complete-2026-08-23.md`](../verification/openapi-client-generation-migration-complete-2026-08-23.md)。完整公开 npm SDK 发布仍不属于当前完成门槛。 |
| 结构化日志、OpenTelemetry 与低基数指标 | Build-verified | 指标、Trace、日志职责分离；生产采集、告警和保留策略仍需部署环境验收。 |
| 可信代理与转发头边界 | Build-verified | 由主机配置、边界测试与运维基线共同约束。 |
| Identity 会话、刷新令牌、MFA 与 TOTP | Build-verified | 单元、集成及生产配置真实栈 TOTP 浏览器链路已有验证记录；不等于生产环境认证。 |
| Identity 用户管理与档案 | Build-verified | Host 用户列表、创建、编辑、启停、重置密码、JSON 兼容接口、固定结构 Excel 模板/导入/导出和批量启停已落地；工作簿限制为 1 MiB/1,000 行并拒绝公式、外部关系和未知表头。手机号、邮箱、工号及证件组合现由服务端规范化/校验，并以 Host 目录全局唯一索引关闭双库并发竞态；失败资料写入会回滚整个用户事务。生产真实栈浏览器与 Linux 原生进程尚未认证，完成前不升 `Verified`。见[资料权威校验](../verification/2026-08-30-identity-authoritative-profile-validation.md)与[Excel/日志切片](../verification/2026-08-30-identity-excel-observability-log-control-plane.md)。 |
| Tenancy 生命周期与配额 | Build-verified | 租户创建、状态、解析与缓存失效已完成纵向切片；跨模块读取继续通过 Contracts 或投影演进。 |
| RBAC、菜单、页面与按钮级权限 | Build-verified | Vue 按稳定权限码不创建无权入口，后端 Endpoint 精确权限失败关闭，授权页按模块/页面/操作分层。 |
| Host / Tenant 字典 | Build-verified | 模块内查询、事务、双库和 Vue 管理页已形成完整切片。Host.Api Native AOT 双库 Settings HTTP/JSON 证据见 [`api-native-aot-settings-jobs-2026-08-25.md`](../verification/api-native-aot-settings-jobs-2026-08-25.md)。 |
| Host / Tenant 配置参数 | Build-verified | 权威写入与跨实例生效遵循版本化事件、缓存失效和本地投影标准。Host.Api Native AOT 双库 Settings HTTP/JSON 证据见 [`api-native-aot-settings-jobs-2026-08-25.md`](../verification/api-native-aot-settings-jobs-2026-08-25.md)。 |
| 枚举目录 | Build-verified | 机器码与翻译文本分离，客户端通过共享契约消费。 |
| Grid Preference | Build-verified | 读写和缓存已落地；缓存策略已迁入统一策略注册表，Architecture 手工策略 allowlist 为零（见 [`cache-policy-zero-allowlist-2026-08-08.md`](../verification/cache-policy-zero-allowlist-2026-08-08.md)）。 |
| Host / Tenant 审计与操作日志 | Build-verified | 审计、HTTP Operation Log 与 Outbox 职责已分离。 |
| 审计归档与保留 | Build-verified | 归档、导出、完整性与恢复边界已有验证；生产保留周期由运维配置。 |
| Organization 单位、职位与成员关系 | Build-verified | 模块内关联使用本模块 SQL 与事务；Identity 侧本地投影与 Host 对账端点（keyset、dry-run、apply）已落地，见 [`cursor-post-review-follow-up`](../superpowers/plans/2026-08-08-cursor-post-review-follow-up.md) Task 2–3。 |
| Files 本地存储、Provider 与上传状态机 | Build-verified | ProviderKey、Pending→Publishing→Ready、补偿与对账、双库迁移均已有验证。 |
| Document | Build-verified | 2026-08-16：核心功能切片（限流、版本历史、MVP 预览、统计修复、OpenAPI/权限清单、Integration 双库）已落地；admin-parity WCAG 与 admin-real-stack 双库 E2E 仍待 fresh 全绿后升 `Verified`。仍非 Production-verified。见 [`document-parity-2026-08-09.md`](../verification/document-parity-2026-08-09.md)。 |
| API Key、签名请求与模块目录 | Build-verified | 凭据、签名、模块发现和精确授权均有契约与安全测试。 |
| Notifications | Build-verified | 现有 Host 公告、站内信、未读/已读、SignalR 与 Host.Api 双库 Native AOT 范围保持 Build-verified。平台内核（强制/交易/普通/营销政策、Single/FanOut/Failover/Match 路由、投递状态机、独立权限码、成对迁移 104 的 14 张平台表）已为 **Build-verified**，见[内核验证](../verification/2026-08-31-notifications-platform-kernel.md)。Tenant Inbox 与权威未读数（成对迁移 105、受信 Scope、跨租户 404、Intent 幂等 Inbox、RecipientEndpoint 掩码隔离）已为 **Build-verified**，见[Tenant Inbox 验证](../verification/2026-08-31-notifications-tenant-inbox.md)。模板/Intent API（仅 inbox 渠道、版本冻结、幂等扇出）已为 **Build-verified**，见[Template/Intent 验证](../verification/2026-08-31-notifications-template-intent.md)。多 Profile/Binding 控制面（空目录、密钥不回显、Host 不共享、Intent 固定 BindingVersion）已为 **Build-verified**，见[Profile/Binding 验证](../verification/2026-08-31-notifications-profile-binding.md)。Delivery Worker（租约领取、事务外 Adapter、Attempt/Receipt、人工重试、Test Provider 幂等）已为 **Build-verified**，见[Delivery Worker 验证](../verification/2026-08-31-notifications-delivery-worker.md)。Vue 管理控制面（模板/Profile/Binding/Delivery 精确权限、空目录、密钥不回显、FanOut 明示、Unknown 非成功色、偏好诚实占位）已为 **Build-verified**，见[Vue 控制面验证](../verification/2026-08-31-notifications-vue-control-plane.md)。平台扩展 Task 1–8 已按[收口验证](../verification/2026-08-31-notifications-platform-closeout.md)关闭，新扩展最多 **Build-verified**，不得升 `Verified`。真实栈 E2E 尚未在本机跑通。真实外部 Provider 仍为 Planned；邮件/短信/企微/公众号/钉钉均未实现。容量 **Capacity-not-verified**。本机非 Linux 不把本切片新路径的 Native AOT 标为 `Aot-published`。既有 Host Inbox/Announcement 的 Linux 原生证据见 [`api-native-aot-notifications-2026-08-25.md`](../verification/api-native-aot-notifications-2026-08-25.md)。 |
| Jobs | Build-verified | 调度、重试、容量证据与 Worker 运行边界持续硬化；完整 1/2/4/8 容量矩阵只在专用环境执行。Host.Api Native AOT 已在 SQL Server/MySQL 上通过定义、手动触发 ping、执行/计划/健康 HTTP JSON 外部进程验证，见 [`api-native-aot-settings-jobs-2026-08-25.md`](../verification/api-native-aot-settings-jobs-2026-08-25.md)；不外推 Worker 托管轮询或容量。 |
| SerialNumbers | Build-verified | Host/租户规则、纯预览、UTC 周期重置、幂等原子分配、分页筛选、稳定排序、Vue 表单边界与精确权限已落地；双库 Integration 与 admin-real-stack 仍需 fresh 全绿后才能升 `Verified`。见 [`serial-numbers-verified-20260820.md`](../verification/serial-numbers-verified-20260820.md)。 |
| Observability Admin 控制面 | Build-verified | 独立官方模块已交付固定日志根目录、顶层 `.log` 清单、稳定 SHA-256 文件 ID、有界尾读和流式下载；读/下载使用独立 Host 权限，Vue 不创建未授权下载入口，活动文件使用共享读取且客户端不能提交路径。实例/运行时硬件信息仍为 `Mapped`，Linux 原生进程 E2E 尚待 CI；见[本切片验证](../verification/2026-08-30-identity-excel-observability-log-control-plane.md)。 |
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
| SignalR 实时通知 | Build-verified | 路径、鉴权、Backplane、连接指标和失败关闭已完成持续硬化；Host.Api Native AOT 已通过 SQL Server/MySQL + Redis 的 JSON Hub 原生进程收发，仍不代表多节点 Native Backplane 投递认证。见 [`api-native-aot-publish-2026-08-23.md`](../verification/api-native-aot-publish-2026-08-23.md)。 |
| Host.Api Native AOT | Build-verified | **`Aot-published`**：完整 net10.0 API 闭包已通过 Linux 原生 publish、启动、双库关键 HTTP、双库 SignalR JSON 与架构门禁；Notifications 双库原生证据为 [run 32849677783](https://github.com/yan041108/Full.NET/actions/runs/32849677783)；Settings/Jobs 双库原生证据为 [run 32872774812](https://github.com/yan041108/Full.NET/actions/runs/32872774812)。Worker/Migrator 与生产容量仍未验证。见 [`ADR-0008`](../architecture/adr/ADR-0008-api-native-aot-runtime-boundary.md)、[`api-native-aot-publish-2026-08-23.md`](../verification/api-native-aot-publish-2026-08-23.md)、[`api-native-aot-notifications-2026-08-25.md`](../verification/api-native-aot-notifications-2026-08-25.md) 与 [`api-native-aot-settings-jobs-2026-08-25.md`](../verification/api-native-aot-settings-jobs-2026-08-25.md)。 |
| Worker Native AOT | Build-verified | **Analysis-only / `Worker Aot-analysis-clean`**：AOT/Trim 与静态 SQL、JSON、物化器门禁通过；Phase 1–7 已建立隔离 publish、双库一次性扫描、常驻空载/Jobs 心跳/SIGTERM、Legacy Outbox 成功/损坏载荷死信、Jobs Ping 自动领取成功终态，以及 Files 本地 Pending 对账、已删除 Blob Cleanup 和 Reference Claim 晋升/释放外部进程 CI，但本机未生成 Linux 产物，CI 成功前不得标记为 `Aot-published`。见 [`ADR-0010`](../architecture/adr/ADR-0010-worker-native-aot-analysis-boundary.md)、[Phase 0](../verification/2026-08-29-worker-native-aot-phase0.md)、[Phase 1](../verification/2026-08-29-worker-native-aot-phase1.md)、[Phase 2](../verification/2026-08-29-worker-native-aot-phase2.md)、[Phase 3](../verification/2026-08-29-worker-native-aot-phase3.md)、[Phase 4](../verification/2026-08-29-worker-native-aot-phase4.md)、[Phase 5](../verification/2026-08-29-worker-native-aot-phase5.md)、[Phase 6](../verification/2026-08-29-worker-native-aot-phase6.md) 与 [Phase 7](../verification/2026-08-29-worker-native-aot-phase7.md) 验证记录。 |
| Host.Api Native AOT S3 Provider | Build-verified | **`Native-provider-verified: s3`**：Linux Native Host.Api + SQL Server/MySQL 文件元数据 + 真实 MinIO S3 HTTP 上传/下载/删除已通过；AWS Workload Identity、实例角色与 Web Identity 未验证。见 [`ADR-0009`](../architecture/adr/ADR-0009-host-api-native-aot-provider-runtime-boundary.md) 与 [Phase 3 记录](../verification/api-native-aot-phase3-providers-2026-08-24.md)。 |
| Host.Api Native AOT Kafka Replay | Build-verified | **`Native-provider-verified: kafka-replay`**：Linux Native Host.Api + 真实 Kafka 范围重放在 SQL Server/MySQL 下已通过。仅覆盖 API Replay；不覆盖 Worker Producer/Consumer、CDC Relay、DLQ 或 Lag Observer。见 [`ADR-0009`](../architecture/adr/ADR-0009-host-api-native-aot-provider-runtime-boundary.md) 与 [Phase 3 记录](../verification/api-native-aot-phase3-providers-2026-08-24.md)。 |
| gRPC 服务契约 | Planned | 只有明确的进程间高吞吐或流式需求才进入实现。 |
| AI 能力 | Planned | 必须先确定数据边界、审计、模型供应、成本与降级策略。 |
| MCP Server 与 Agent Tools | Planned | 路线图已登记 M5+，生产代码尚无 MCP 工具暴露。实施前必须建立静态 Tool 目录、源生成参数/结果、逐工具权限、租户隔离、人审高影响操作、限流与审计；禁止本机 HTTP 回环转发调用方身份。见[刷新后审计](../verification/2026-08-30-adminnet-refresh-incremental-audit.md)。 |
| Workflow | Build-verified | 已交付自有审批内核、不可变定义/表单版本、原生静态表单设计器、Workflow-Vue3 设计器适配、Vue 与 uni-app/H5 运行时，以及可推进的多级线性 `human.approval` 审批/驳回闭环；SQL Server/MySQL 集成、真实栈表单设计、Host/Tenant Vue 管理端审批矩阵（同意/拒绝、403/409/422）和 Linux Native AOT 双库外部进程已有新鲜证据。`notify.cc`、`gateway.exclusive` 仍保持不可发布/不可执行，Worker 恢复和生产容量尚未关闭，因此不得标为 `Verified`。见[核心计划](../superpowers/plans/2026-08-20-workflow-first-vertical-slice.md)、[首切片收口验证](../verification/2026-08-31-workflow-first-slice-closeout.md)、[管理端审批验证](../verification/2026-08-30-workflow-admin-approval-real-stack.md)、[原生表单设计器验证](../verification/2026-08-30-workflow-native-form-designer.md)与[Native AOT 验证](../verification/2026-08-30-workflow-native-aot.md)。 |
| Admin.NET 功能吸收 | Build-verified | 已完成首轮设计吸收与多个纵向切片；2026-08-30 将 Admin.NET.Pro `v2.1` 基线更新至 `09d38bd8`，自 `3879b035` 累计审计 59 个提交。Identity Excel、Host 用户资料权威校验与 Observability Admin 日志控制面已按 Full.NET 安全边界交付；当前明确缺口包括 Notifications 强类型扩展元数据和 MCP 安全/AOT 设计。后续按 [`adminnet-feature-parity.md`](adminnet-feature-parity.md) 逐模块交付，不承诺代码逐行复制。见[资料权威校验](../verification/2026-08-30-identity-authoritative-profile-validation.md)。 |
| k6 与生产容量认证 | Implemented | [`eng/load`](../../eng/load/README.md) 已提供工具、阈值和报告能力；生产等价环境认证前统一标记 `Capacity-not-verified`。 |

## 2026-08-08 后续优先级

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
