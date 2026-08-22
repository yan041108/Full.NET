# Full.NET 能力状态矩阵

> 更新时间：2026-08-16。本文只维护能力状态、稳定证据入口与后续优先级；可变测试数量统一以 [`eng/testing/test-matrix.json`](../../eng/testing/test-matrix.json) 为准。

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
| System.Text.Json、OpenAPI 与 Vue 调用契约覆盖 | Build-verified | Architecture 测试按生产 Endpoint 元数据枚举 JSON 类型；Vue 生产 API 文件必须逐项映射 OpenAPI fixture 与共享 TypeScript 契约，新增漏项失败关闭。OpenAPI 驱动客户端生成三类试点已 `Pilot-passed`，Identity remaining 已全部 `Slice-passed`，Tenancy 首切片 [Host Tenants](../verification/openapi-client-tenancy-host-tenants-2026-08-22.md) 已 `Slice-passed`（现 76 条 `generated`），见 [`openapi-client-generation-pilot-2026-08-21.md`](../verification/openapi-client-generation-pilot-2026-08-21.md)、[`openapi-client-identity-host-roles-2026-08-21.md`](../verification/openapi-client-identity-host-roles-2026-08-21.md)、[`openapi-client-identity-host-menus-2026-08-21.md`](../verification/openapi-client-identity-host-menus-2026-08-21.md)、[`openapi-client-identity-host-api-keys-2026-08-22.md`](../verification/openapi-client-identity-host-api-keys-2026-08-22.md)、[`openapi-client-identity-host-online-sessions-2026-08-22.md`](../verification/openapi-client-identity-host-online-sessions-2026-08-22.md)、[`openapi-client-identity-host-modules-2026-08-22.md`](../verification/openapi-client-identity-host-modules-2026-08-22.md)、[`openapi-client-identity-me-2026-08-22.md`](../verification/openapi-client-identity-me-2026-08-22.md)、[`openapi-client-identity-totp-enrollment-2026-08-22.md`](../verification/openapi-client-identity-totp-enrollment-2026-08-22.md)、[`openapi-client-identity-super-administrators-2026-08-22.md`](../verification/openapi-client-identity-super-administrators-2026-08-22.md)、[`openapi-client-identity-auth-session-2026-08-22.md`](../verification/openapi-client-identity-auth-session-2026-08-22.md) 与 [`openapi-client-tenancy-host-tenants-2026-08-22.md`](../verification/openapi-client-tenancy-host-tenants-2026-08-22.md)；下一默认项为 `tenant-packages.ts`。完整批量 SDK 迁移仍不属于当前完成门槛。 |
| 结构化日志、OpenTelemetry 与低基数指标 | Build-verified | 指标、Trace、日志职责分离；生产采集、告警和保留策略仍需部署环境验收。 |
| 可信代理与转发头边界 | Build-verified | 由主机配置、边界测试与运维基线共同约束。 |
| Identity 会话、刷新令牌、MFA 与 TOTP | Build-verified | 单元、集成及生产配置真实栈 TOTP 浏览器链路已有验证记录；不等于生产环境认证。 |
| Tenancy 生命周期与配额 | Build-verified | 租户创建、状态、解析与缓存失效已完成纵向切片；跨模块读取继续通过 Contracts 或投影演进。 |
| RBAC、菜单、页面与按钮级权限 | Build-verified | Vue 按稳定权限码不创建无权入口，后端 Endpoint 精确权限失败关闭，授权页按模块/页面/操作分层。 |
| Host / Tenant 字典 | Build-verified | 模块内查询、事务、双库和 Vue 管理页已形成完整切片。 |
| Host / Tenant 配置参数 | Build-verified | 权威写入与跨实例生效遵循版本化事件、缓存失效和本地投影标准。 |
| 枚举目录 | Build-verified | 机器码与翻译文本分离，客户端通过共享契约消费。 |
| Grid Preference | Build-verified | 读写和缓存已落地；缓存策略已迁入统一策略注册表，Architecture 手工策略 allowlist 为零（见 [`cache-policy-zero-allowlist-2026-08-08.md`](../verification/cache-policy-zero-allowlist-2026-08-08.md)）。 |
| Host / Tenant 审计与操作日志 | Build-verified | 审计、HTTP Operation Log 与 Outbox 职责已分离。 |
| 审计归档与保留 | Build-verified | 归档、导出、完整性与恢复边界已有验证；生产保留周期由运维配置。 |
| Organization 单位、职位与成员关系 | Build-verified | 模块内关联使用本模块 SQL 与事务；Identity 侧本地投影与 Host 对账端点（keyset、dry-run、apply）已落地，见 [`cursor-post-review-follow-up`](../superpowers/plans/2026-08-08-cursor-post-review-follow-up.md) Task 2–3。 |
| Files 本地存储、Provider 与上传状态机 | Build-verified | ProviderKey、Pending→Publishing→Ready、补偿与对账、双库迁移均已有验证。 |
| Document | Build-verified | 2026-08-16：核心 Verified 切片（限流、版本历史、MVP 预览、统计修复、OpenAPI/权限清单、Integration 双库）已落地；admin-parity WCAG 全绿与 admin-real-stack 双库 E2E 复验仍待 fresh 输出后升档。仍非 Production-verified。见 [`document-parity-2026-08-09.md`](../verification/document-parity-2026-08-09.md)。 |
| API Key、签名请求与模块目录 | Build-verified | 凭据、签名、模块发现和精确授权均有契约与安全测试。 |
| Notifications | Build-verified | Inbox 与 SignalR 分层；发送路径在事务外调用 `IHostUserDirectory` 校验收件人，事务内仅写入 Notifications 表，Architecture 本地事务扫描无登记债务。 |
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
