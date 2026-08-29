# ADR-0010：Worker Native AOT 分析边界

- 状态：Phase 0 已实施；Phase 1/2/3/4 publish 与外部进程门禁已批准实施，等待 Linux CI 证据
- 决策日期：2026-08-29
- 适用范围：`Full.NET.Host.Worker` 的 Native AOT 静态分析闭包
- 关联决策：[`ADR-0006`](ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md)、[`ADR-0008`](ADR-0008-api-native-aot-runtime-boundary.md)、[`ADR-0009`](ADR-0009-host-api-native-aot-provider-runtime-boundary.md)

## 1. 上下文

Host.Api 已完成 linux-x64 Native AOT 发布与双库外部进程验证，但 Worker 使用独立的最小模块装配图，只调用模块的 `AddBackgroundServices`。因此，Host.Api 中注册的 Dapper 参数绑定器、行物化器和 JSON 元数据不能自动证明 Worker 闭包成立。

Worker 是长生命周期后台进程，Native AOT 的启动或内存收益目前没有生产等价证据。Phase 0 的目的不是宣称性能提升，而是建立可重复的分析入口并关闭静态不可达风险，为后续是否发布原生产物提供证据。

## 2. 候选方案与取舍

| 方案 | 收益 | 代价与风险 | 结论 |
| --- | --- | --- | --- |
| 保持 Worker 完全 JIT，不增加 AOT 门禁 | 零改造成本 | Worker 独立装配图中的 SQL、JSON 和物化器缺口继续不可见，未来原生化需要集中偿债 | 不选；无法建立可持续证据 |
| Phase 0 只启用 AOT/Trim 分析，不发布原生产物 | 以较低风险关闭静态闭包，并保持默认 JIT 部署与业务语义 | 增加条件编译注册和约一分钟构建成本；不能证明 Provider 与运行时行为 | **采用** |
| 直接发布 Worker Native AOT 并切换部署 | 一次完成构建与运行时验证 | 当前缺少双库后台、Kafka、优雅停止与容量证据，失败面过大且回退复杂 | 暂不选；留给独立 Phase |

选择 Phase 0 是因为它把“静态闭包是否成立”与“原生 Worker 是否值得部署”拆成两个可独立否决的门禁；分析通过不构成性能或生产发布论据。

## 3. 决策

### 3.1 Phase 0 范围

1. 为 Worker 增加独立的 AOT/Trim/配置绑定分析命令，使用与 Host.Api 相同的 `FULLNET_AOT_COMPILE` 静态执行机制。
2. Worker 自身及其后台模块不得向 Full.NET 泛型 SQL 执行器传递匿名参数；所有非标量结果必须在首次数据库请求前同步注册物化器。
3. Worker 控制台机器输出必须使用 System.Text.Json 源生成元数据，不允许反射式 `JsonSerializerOptions` fallback。
4. API 与 Worker 继续使用同一业务 SQL、Outbox 状态机、租户、重试和双库语义；条件编译只替换执行机制。
5. 本阶段不启用正式 `PublishAot`，不创建 Linux Worker 产物，也不声明 Worker Provider 已验证。

### 3.2 后续 Phase 的准入条件

只有 Phase 0 达到 analysis clean，且没有通过通配 root、`NoWarn=IL*` 或无依据 suppression 换绿，才允许规划 linux-x64 publish。正式发布阶段必须另外覆盖：

- SQL Server/MySQL 上的 Jobs 自动领取与终态；
- Legacy Outbox 发布、确认、重试和 Retention；
- Files 清理与引用对账；
- Kafka Producer/Consumer、Inbox 幂等和优雅停止；
- 连接准入超时、取消和 Worker 退避。

上述证据完成前，不得使用 `Aot-published`、`Native-provider-verified` 或生产容量表述。

### 3.3 排除范围

- `Full.NET.Host.Migrator`、DbUp 与 Seeding Native AOT；
- CDC/Kafka Delivery 生产切流与容量认证；
- Typed Command Factory 全框架切换；
- 任何数据库结构、API、消息契约或可靠性语义变化。

## 4. 后果

- 正向：Worker 新增 SQL 结果或控制台 JSON 类型时，静态门禁与 analyzer 会在合并前暴露缺失注册；默认 JIT 发布方式保持不变。
- 负向：AOT 分析与默认 JIT 共享构建目录，脚本必须在结束时强制重建 JIT 产物，增加本地验证时间。
- 限制：静态分析无法证明数据库 Provider、Kafka native binding、取消、恢复与吞吐行为；这些仍需后续 Linux 外部进程和双库 E2E。
- 维护：模块新增后台 SQL 物化器时，Contributor 必须从 `AddBackgroundServices` 的 AOT 分支同步注册，Architecture 门禁从实际 Contributor 文件自动发现范围。

## 5. 状态声明

| 状态 | 含义 |
| --- | --- |
| `Worker Aot-analysis-clean` | Worker 完整引用闭包的 AOT/Trim 分析无未处理告警，Architecture 静态 SQL/JSON/物化器门禁通过 |
| `Worker Aot-published` | 后续独立 Phase 的 Linux 原生 publish、启动和双库外部进程 E2E 全部通过 |

Phase 0 只能产生第一种状态。

Phase 1 采用 Worker 既有的一次性 Outbox 版本退役扫描作为最小外部进程闭包：JIT Migrator 负责双库 schema，原生 Worker 负责启动、Dapper AOT backlog 读取、源生成 JSON 和确定性退出。该切片通过后仍不覆盖常驻轮询、Kafka/CDC、Jobs 自动领取、Files 后台任务或容量；只有对应后续 Phase 完成后才能扩大状态声明。

Phase 2 在同一双库门禁中启动正常 `LegacyPolling` 常驻进程，通过 `/health/live` 确认宿主完成启动，并等待 `fn_jobs_worker_instance` 心跳证明 Jobs 后台循环至少执行一轮；随后发送 SIGTERM，要求进程以代码 0 退出且日志不存在 Outbox、Jobs、Files 迭代故障或 AOT 致命标记。该切片只证明空载后台闭包与优雅停止，不证明 Outbox 业务消息处理、Jobs 领取终态、Files 启用态、Kafka/CDC 或容量。

Phase 3 在隔离双库中写入一条合法 Notifications MemoryPack 事件和一条同路由损坏载荷，要求原生 `LegacyPolling` Worker 对合法消息首次领取即写入成功终态，对损坏消息首次领取即写入 `outbox.invalid_payload` 死信，并释放两条租约。该门禁证明 Legacy Outbox 的领取、AOT 反序列化、Handler 调度与两类确定终态，不证明租约续期、瞬时重试、崩溃恢复、多 Worker 竞争、Realtime 网络投递或容量。

Phase 4 在隔离双库中写入一个 Host 级启用的内置 Ping Job 定义和一条 Pending 手动执行记录，要求原生 Worker 自动领取并在首次尝试写入 `Succeeded`，同时形成开始/结束时间并清空错误、租约和重试字段。该门禁证明 Jobs 的 AOT 领取、定义物化、HandlerKind 解析、执行与成功终态，不证明失败重试、租约续期、崩溃恢复、多 Worker 竞争、计划调度或容量。

## 6. 回退

Phase 0 不改变默认 JIT 发布。若分析闭包无法在不放宽安全门禁的条件下关闭，保留现有 Worker JIT 运行方式，并在验证记录中列出具体程序集、告警和未验证路径。
