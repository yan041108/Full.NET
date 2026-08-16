---
name: fullnet-module-delivery
description: Use when adding or extending a Full.NET module, CRUD feature, endpoint, command/query, public .NET API, Dapper persistence, SQL Server/MySQL migration, Admin.NET parity capability, or reviewing a future architecture evolution in this repository.
---

# Full.NET 模块交付

## 核心原则

先交付一条可运行、可验证的纵向切片，再扩展横向能力。每个切片同时满足模块边界、租户与授权、Dapper 双数据库、标准 API、中文注释和真实测试要求。

开始前必须读取根目录 `AGENTS.md`、相关 `rules/`、架构规格和功能对标路线，并运行 `git rev-parse HEAD` 记录任务基线。工作区已脏或任务跨窗口时还必须使用 `pnpm test:task:start -- <task-id>` 创建任务快照。涉及数据库对象、公共标识符、API/JSON、稳定机器码、配置键、缓存键或生成产物时，必须读取 `rules/naming-conventions.md`。需要精确路径、参考切片和验证命令时，读取 [交付地图](references/delivery-map.md)。

按任务加载参考，禁止把全部外部知识无条件塞入上下文：

- 设计 .NET 10 类库、公共 API、ASP.NET Core 10 安全、Microsoft.Data.SqlClient 或 SQL Server 数据访问时，读取[微软 .NET 10 与 SQL Server 指导映射](references/microsoft-dotnet-sqlserver-guidance.md)。
- 评估微服务、分片、多数据库、Polyglot Persistence 或其他云架构升级时，读取[未来架构演进参考](references/future-architecture-evolution.md)。其中所有模式都受当前基线与证据门禁约束；事务 Outbox 的 CDC/Kafka 演进仅按已批准 ADR-0006/Spec/计划实施，不得扩大为微服务、分片或多数据库授权。
- 遇到延迟、吞吐、Query Store、执行计划、等待、锁、缓存、Worker 或包体优化时，必须改用 `fullnet-performance-hardening`；本 Skill 只负责模块和契约边界。

## 1. 定义交付契约

1. 将用户需求拆成可验收能力，标明成功、失败、权限、租户、并发和兼容场景。
2. 在 Admin.NET 对标矩阵中找到对应项；按能力和用户流程对标，不复制源码、表结构或耦合方式。
3. 明确归属为 `Core`、Official Module、Provider、Compatibility、Sample 或 Client。
4. 区分本次必须交付与后续能力；没有真实消费者的抽象不要提前创建。
5. 记录会变化的公共契约、数据库结构、事件格式和缓存语义。
6. 从 Naming Profile 确定 OwnerKey、模块键、实体键、列名和稳定协议名称；不要让模板或模块各自实现大小写、截断或摘要算法。

## 2. 设计纵向切片

项目拓扑默认采用“一个主项目＋按证据可选项目”。一个大业务模块先创建一个 `Full.NET.Modules.<Module>` 主项目，CRUD、实体、菜单、Command/Query 与 Endpoint 都作为项目内垂直切片，不得按 CRUD 或目录机械增加 `.csproj`。

- 只有存在真实跨模块消费者或外部编译期消费者，并且需要隔离稳定公开契约时，才创建独立 `*.Contracts` 项目；否则使用主项目内的 `Contracts/` 目录。
- 只有同一核心被非该传输宿主真实复用，并能证明独立传输适配收益，包括依赖、打包或安全隔离收益时，才创建 `*.Http`、`*.Worker` 等适配项目。
- API、Worker、Migrator 的角色分离不是拆项目证据；先使用主项目内显式注册入口和 `FullNetHostProfile`。
- 新增可选项目必须在已批准 Spec 或计划中写明消费者、依赖方向、收益和架构测试；没有证据时保持一个主项目。

按需选择以下组成部分，禁止为目录完整而创建空层：

| 组成 | 使用条件 | 责任 |
| --- | --- | --- |
| `Contracts` | 跨模块或外部消费者需要稳定契约 | DTO、公开接口、集成事件 |
| `Domain` | 存在业务不变量和状态转换 | 实体、值对象、领域规则 |
| `Features/<UseCase>` | 每个命令或查询 | Command/Query、Validator、Handler、Endpoint |
| `Persistence` | 访问业务数据 | Dapper SQL、映射、Resolver/Repository |
| `Serialization` | DTO 或事件进入线格式 | System.Text.Json 源生成、MessagePack Resolver |
| 模块注册 | 组件需要运行 | DI、Validator、Endpoint、序列化与事件处理器注册 |

保持 HTTP Request、内部 Command/Query 和持久化模型分离。业务规则进入 Handler/Domain，Endpoint 只处理传输、授权、映射和结果转换。

设计公共 .NET API 或框架扩展点时，先按 Framework Design Guidelines 检查命名、类型、成员、扩展性、异常和资源释放，再用 .NET 10 的实际目标框架、分析器与兼容性测试确认。只有真实跨模块或外部消费者才扩大可见性；不要把数据库、HTTP 或第三方实现类型泄漏到稳定契约。

### 未来架构演进边界

Full.NET 当前基线保持强化型模块化单体。微服务、分片和多数据库只进入探索性评审：先证明现有架构下的优化已不足，再按可靠性、安全、成本、运维和性能建立量化证据，通过最小实验与回滚验证，最后提交 Spec/ADR 审批。事务 Outbox 的 CDC/Kafka 演进已获单项批准，只能按 ADR-0006 的阶段、单一发布所有权和双库停止条件实施。禁止因瞬时 QPS、技术流行度或目录完整度扩大架构升级范围。

## 3. 先建立 RED 证据

1. 为新行为选择最小但真实的测试层：纯规则用 Unit，依赖方向用 Architecture，Admin.NET 响应用 Compatibility，SQL/迁移/事务/Outbox 用 Integration。
2. 先写一个能表达预期行为的测试并运行，确认它因为能力缺失而失败，不是因为拼写、环境或测试发现错误。
3. 测试名称表达场景与结果；测试注释仅在需要解释历史原因或非显然时序时使用，并使用中文。
4. 实现最小正确切片使测试通过，再补失败、边界、取消、重复和并发场景。

## 4. 实现业务与数据边界

### 租户和授权

- 从受信任上下文取得租户，不信任普通请求提供的 TenantId。
- 在 Endpoint 声明授权，在 Handler 或领域边界再次保护敏感业务不变量。
- SQL、缓存键和实时分组都必须包含租户边界。
- 写入 Outbox 的消息同样必须携带租户边界。
- 管理员跨租户能力必须显式命名、授权和审计。

### Dapper 与双数据库

- 使用 Dapper、参数化 SQL 和现有 SQL Scope；不要引入 EF Core 捷径。
- 通过 Full.NET 自有边界使用 `Microsoft.Data.SqlClient`；生产 SQL Server 连接必须加密并验证证书，使用 `TrustServerCertificate=False`，不得把跳过证书校验当作部署修复。
- 表、索引、约束和稳定协议名称通过 `Full.NET.Data.CodeGeneration` 校验或生成；表名超长必须重新设计，只有索引和约束可使用确定性摘要压缩。
- 新模块主键默认使用 `PrimaryKeyTypeMapping` 的 UUID v7 配置档：C# `Guid`、SQL Server `uniqueidentifier`、MySQL `BINARY(16)`、JSON `string/uuid`；Snowflake 只能由独立 ADR 授权且不得与 UUID 混用。
- 010+ 迁移必须满足 `pnpm test:naming` 中的 UUID SQL 门禁：MySQL 禁止 UUID 列 `char(36)`；SQL Server 新 UUID 主键必须显式 `CLUSTERED`/`NONCLUSTERED`，高写入表（Outbox、Auth Audit）使用非聚集主键并配套时间聚集索引。
- 数据库行为变化时同时实现 SQL Server 与 MySQL 的 SQL、DbUp 迁移、索引和集成测试。
- 非事务或隐式提交的 DDL 必须按结构探测、回填和约束收紧分别收敛；双库集成测试要模拟 DbUp 未记账且迁移部分完成后的重跑恢复。
- 只读端点若不改变结构，不要创建迁移；只为实际查询添加 SQL 和测试。
- 评估排序稳定性、分页、索引和最坏数据量，不凭感觉宣称性能提升。

### 事务、事件与缓存

- 重要业务事件才允许 Outbox：仅把需要事务耦合、可幂等消费的业务 Integration Event 写入 Outbox，并与业务状态同一事务提交；外部调用不得进入数据库事务。
- 跨进程可靠事件使用带版本元数据的 MessagePack；进程内调用不要序列化。
- 只有存在重复读取与明确失效点时才使用 FusionCache；缓存条目必须按 `C0/S0-L2/S1/S2/N0` 分类，细节见交付地图与 ADR-0005、总体 Spec §13。
- 缓存失效不写 Outbox：事务提交后直接删除 L1/L2 并广播 Backplane；不要在提交前删除缓存，删除或通知失败由 TTL、版本与权威源收敛。
- 不可变判断：
  - `Cache invalidation: commit database state -> remove current L1/shared L2 -> publish Backplane.`
  - `Outbox admission: only durable, transaction-coupled business Integration Events with idempotent consumers.`
- 明确幂等、重试、租约、毒消息、取消和失败恢复，不把至少一次交付写成恰好一次。

### 跨模块数据访问与一致性检查单

每个跨模块用例在实现前必须书面回答下列问题，并在 Architecture/Integration 测试中留下可重复证据；临时例外只能登记到 `contracts/architecture/` 债务目录，不得静默扩展。

| 检查项 | 要求 |
| --- | --- |
| 数据归属 | 生产 SQL 只读写当前模块拥有的 `fn_<module>_*` 表；禁止跨模块外键、视图、同义词或隐藏 JOIN。已知债务登记 `module-cross-foreign-key-debt.json`。 |
| 读取模式 | 立即读取使用消费方最小 Port；列表/分页对同一所有者数据一次批量读取（参考 `IHostUserDisplayDirectory.FindHostUsersAsync`），禁止逐行回退查询。 |
| 事务归属 | 本地事务只写入本模块表；事务内禁止同步调用其他模块 Contract。已知债务登记 `module-local-transaction-debt.json`。 |
| 失败语义 | 写入后可能返回 `Result.Failure` 的路径使用 `ICommandTransaction.ExecuteResultAsync`，不得让失败 Result 仍提交。 |
| 一致性 SLA | 声明最终一致或强一致类别；强不变量由唯一模块拥有，不依赖跨模块本地事务快照。 |
| 幂等键 | 事件消费、Outbox Handler 和投影写入必须可幂等重放。 |
| 投影重建 | 消费方本地投影需有版本/顺序、死信或重放与全量重建路径。 |
| 远程适配 | 同进程内优先 Contract；只有真实跨进程消费者且经 ADR 批准才引入 HTTP/gRPC/Broker。 |
| 领域参数 | 业务不变量不得存入通用 Settings `ConfigEntry`；其他模块禁止引用 ConfigEntry CRUD 契约或查询 `fn_settings_config_entry`。平台策略在 Settings 内强类型实现（如 DiagnosticPolicy）。 |

相关门禁：`ModuleLocalTransactionBoundaryTests`、`ModuleCrossForeignKeyBoundaryTests`、`OrganizationHostUserBatchReadBoundaryTests`、`DomainParameterOwnershipTests`。计划见 `docs/superpowers/plans/2026-08-07-module-data-access-and-consistency-hardening.md`。

## 5. 保持 API 与兼容边界

1. 默认 HTTP Endpoint 使用真实状态码、强类型成功响应和 ProblemDetails 错误。
2. 应用层返回 `Result<T>`/`PagedResult<T>`，传输层负责映射；不要让业务层依赖 HTTP。
3. 只有明确需要旧客户端兼容时才通过 Compatibility 层启用 Admin.NET 包络，且保留真实状态码。
4. 文件、流、SignalR、Webhook、健康检查和 `204 No Content` 不进入统一包络。
5. JSON 使用 System.Text.Json；公开热路径 DTO 加入模块源生成上下文。
6. 权限码、错误码、消息类型和 Statement ID 必须通过同一 Naming Profile；业务逻辑依赖稳定机器码，不依赖显示文本。

## 6. 接入运行时

1. 在模块入口显式注册 Command/Query Handler、FluentValidation Validator、事件处理器、序列化上下文和持久化组件。
2. 新增或改变官方模块时，必须更新 `Full.NET.Composition` 的共享目录并选择明确的 `FullNetHostProfile`；Api/Migrator 可装配完整模块，Worker 只能使用模块公开的最小后台入口，禁止在三个宿主中复制注册清单。
3. 审查 DI 生命周期，禁止 Singleton 捕获 Scoped 服务或请求级租户上下文。
4. 映射 Endpoint 与中间件顺序，确认验证位于事务之前，异常进入统一处理管道。
5. 为数据库、缓存和必要依赖注册真实就绪检查；空检查成功不能作为证据。
6. 使用结构化日志和稳定错误码，不记录敏感数据；高频日志遵循项目源生成与有界异步策略。
7. 新增或修改的所有手写源代码注释和 XML 文档注释必须使用清晰中文，并解释意图、不变量和风险。

## 7. 完成验证

1. 按 `inner`、`slice`、`merge` 阶段运行受影响测试和 `pnpm test:naming`，再运行 Release 构建；inner 使用 `pnpm test:inner`，禁止用 `test:e2e:real`、完整 `test:e2e:admin` 或 `test:integration:full` 代替。先执行 `test:integration:affected:plan` 审查影响集，再执行 affected。工作区已脏时使用任务快照，干净单窗口任务可使用任务基线。本地任务只运行受影响测试；完整集合只保留给 `main` CI 的互斥并行分片。
2. 使用测试矩阵生成的最低发现数防止零测试假通过；增删测试只更新 `eng/testing/test-matrix.json`，README、开发文档、CI 与 Skill 不复制数字。
3. 数据变更必须实际运行 SQL Server/MySQL 集成测试。依赖不可用时报告未验证项，不得写成通过。
4. 检查 `git diff --check`、架构依赖、UTF-8、许可证和工作区状态。
5. 更新功能对标状态时严格区分 Mapped、Implementing、Implemented 与 Verified。
6. 检查是否命中规则演进触发条件；只有已有 Skill 出现真实缺口或处于里程碑集中复盘时才执行 Skills 复盘，普通任务只输出一行未触发结论。
7. 存量不兼容名称只可在 `contracts/naming/naming-debt.json` 按类型、值和文件精确登记，并给出移除里程碑；禁止通配、目录豁免或让新生成代码继承债务。

本地 Integration 只运行影响集：普通模块、认证与租户走对应 SQL Server/MySQL 聚焦测试；后台消息队列基础设施与 FusionCache 基础设施各自使用登记过的聚焦集；共享宿主与 Composition 执行 Smoke；测试矩阵已登记恢复集的迁移执行对应双库恢复测试和受影响模块测试，未登记迁移安全降级到 migrations 分片并追加可识别的受影响模块；迁移 Runner 执行 migrations 分片，Integration 工具执行 tooling。完整集合只保留给 `main` CI，不在本地任务中运行。

## 按需决策速查

| 变化 | 必须加入 | 不要加入 |
| --- | --- | --- |
| 新业务状态与写入 | Domain、Command、Validator、Handler、双库 SQL/测试 | 与当前用例无关的通用仓储 |
| 新数据库结构 | SQL Server/MySQL DbUp 迁移与回归测试 | 单库迁移或运行时自动建表 |
| 新数据库/API/消息命名 | Naming Profile、`Full.NET.Data.CodeGeneration`、`pnpm test:naming` | 自行截断、通配债务或复制存量旧名称 |
| 跨模块可靠通知 | Contract 事件、MessagePack、事务 Outbox、Handler | 事务提交前直接推送；缓存/Audit/日志 Outbox |
| 跨模块立即读取 | 消费方 Port、批量目录接口、Architecture 批量读取门禁 | 事务内 Contract、逐行 `FindActiveHostUserAsync`、跨模块 SQL |
| 跨模块写入后失败 | `ExecuteResultAsync` 与事务回滚测试 | `ExecuteAsync` 返回失败 `Result` 仍提交 |
| 领域参数 | 所有者模块强类型策略/表 + Outbox（经计划批准） | Settings `ConfigEntry` CRUD 或其他模块直查 `fn_settings_config_entry` |
| 高频读且有失效点 | FusionCache 键、分类策略、提交后直接删 L1/L2 与 Backplane、多实例验证 | 没有失效策略的永久缓存；缓存 Outbox |
| 标准 Web API | 授权、验证、Result 映射、ProblemDetails | 默认 Admin.NET 包络 |
| 旧管理端迁移 | Compatibility 适配与兼容测试 | 让兼容模型反向进入核心 |
| 无结构变化的只读查询 | Query、Handler、参数化 SQL、API/单元测试 | 不需要的迁移；也不要为只读路径强行加 Outbox |

## 常见错误

- 把每个 CRUD、菜单或 Endpoint 建成独立项目，或把 `Contracts`、`.Http` 当作每个模块的固定模板。
- 只完成 Endpoint 和表，遗漏注册、权限、租户、序列化或测试。
- SQL Server 通过后假定 MySQL 等价，未运行真实集成测试。
- 把 FluentValidation 当授权系统，或信任客户端传入的租户标识。
- 在事务内调用外部服务，提交前发布事件/删除缓存，或用 Outbox 承载缓存失效。
- 把所有结果包装成 HTTP 200，破坏 ProblemDetails 和客户端语义。
- 更新代码后忘记更新中文注释、路线图状态、许可证通知或测试数量。
- 创建没有真实消费者的 Provider、SignalR、gRPC 或 AI 抽象。
- 把微软云架构通用模式当成当前项目决策，未经证据门禁和 ADR 就引入微服务、分片或多数据库。
