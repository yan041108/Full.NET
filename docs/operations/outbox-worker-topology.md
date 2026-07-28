# Outbox Worker 运维说明

## 1. 适用范围

本文档说明 Full.NET 当前 Outbox Worker 的默认部署拓扑、运行参数、死信语义与受控人工重放边界。它只覆盖当前仓库已经实现的能力：

- MessagePack `application/x-msgpack` 载荷；
- `(MessageType, SchemaVersion)` 精确路由；
- 稳定消息上下文与 Handler 幂等策略启动门禁；
- 数据库租约领取；
- 最大尝试次数与死信终态；
- 待处理数量与最老消息年龄指标；
- 指定消息类型与 SchemaVersion 的一次性只读退役扫描；
- SQL Server / MySQL 双库列契约。

本文档不声明尚未实现的能力，例如相邻版本升级链、生产发布平台自动化门禁、Redis Leader Election 或通用一键自动重放工具。

## 2. 默认拓扑

- 默认多副本安全模型依赖数据库租约，而不是默认引入额外选主机制。
- 每个 Worker 周期调用 `AcquireAsync(batchSize, lease, ...)`，只领取 `ProcessedAtUtc IS NULL`、`DeadLetteredAtUtc IS NULL` 且租约已过期/未占用的消息。
- 成功领取时数据库会为消息写入新的 `LockId`、`LockedUntilUtc` 并递增 `Attempts`。
- 批次处理期间按 `LeaseRenewalSeconds`，使用精确批次消息 ID 与共享 `LockId` 主动延长
  仍未进入终态的消息，因而同时保护正在执行的慢 Handler 和尚未开始的批尾消息。
- 处理成功后消息被标记为 `ProcessedAtUtc`，并清理重试/租约/死信辅助字段。
- 临时失败会释放租约、保留错误摘要并设置 `NextAttemptAtUtc`，等待下次重试。
- 永久失败或达到最大尝试次数后，消息进入死信终态，不再被后续领取。

结论：当前生产默认模型是“数据库租约 + 幂等 Handler + 至少一次投递”。在没有真实压力证据前，不要把“必须上 Redis Leader Election”写成唯一解。

### 2.1 消息上下文与幂等门禁

Worker 调用 Handler 时提供 `IntegrationEventContext`，其中 `MessageId`、`MessageType`、
`SchemaVersion`、`TenantId`、`TraceId` 和 `OccurredAtUtc` 均直接来自已领取的持久化
Outbox 记录。重试、租约回收和多副本竞争不会生成新的 `MessageId`。

`IIntegrationEventHandler` 保留 payload-only 重载；旧实现通过默认接口方法继续工作。
新 Handler 若需要去重，应覆盖上下文重载。每个生产 Handler 必须声明以下一种策略，Worker
在启动期拒绝 `Unspecified` 或未知值：

- `NaturallyIdempotent`：重复执行只会收敛到同一业务状态；代码审查必须能指向具体不变量。
- `MessageIdDeduplication`：跨数据库写入或外部副作用在其提交边界持久化 `MessageId`，
  去重记录与副作用应尽可能位于同一事务；仅在内存中记忆 MessageId 不构成可靠去重。

策略声明是启动门禁和审计证据，不会把至少一次投递升级为 Exactly-Once。新增 Handler
不得依赖 `TraceId`、租约 `LockId` 或当前尝试次数作为幂等键。

## 3. Worker 配置

配置节名称：`OutboxWorker`

```json
{
  "OutboxWorker": {
    "BatchSize": 20,
    "MaxConcurrency": 1,
    "LeaseSeconds": 30,
    "LeaseRenewalSeconds": 10,
    "PollMilliseconds": 1000,
    "BacklogSampleSeconds": 30,
    "MaxAttempts": 5
  }
}
```

当前启动期校验边界：

| 配置 | 默认值 | 有效范围 | 说明 |
| --- | ---: | ---: | --- |
| `BatchSize` | 20 | 1..200 | 每轮最多领取的消息数 |
| `MaxConcurrency` | 1 | 1..16，且不超过 `BatchSize` | 单个 Worker 进程内同时处理的最大消息数；默认 1 保持串行 |
| `LeaseSeconds` | 30 | 5..3600 | 单条消息租约持续时间；过期后允许其他 Worker 回收 |
| `LeaseRenewalSeconds` | 10 | 1..1200，且不超过 `LeaseSeconds / 2` | 批次主动续期间隔；使用独立 Scope 和数据库会话 |
| `PollMilliseconds` | 1000 | 100..60000 | 空轮询等待时间 |
| `BacklogSampleSeconds` | 30 | 5..3600 | 积压聚合查询与指标记录周期；采样失败不会阻断消息领取 |
| `MaxAttempts` | 5 | 1..100 | 单条消息允许的最大总尝试次数，包含当前领取 |

运维建议：

- 先从默认值启动，不要同时大幅提高 `BatchSize` 和 `LeaseSeconds`。
- `MaxConcurrency = 1` 保持单批按领取顺序串行处理；只有受控负载证据证明 Handler、
  数据库和下游依赖仍有容量时，才从 2 或 4 开始逐级提高。
- `MaxConcurrency > 1` 不保证消息全局完成顺序，只允许用于彼此独立且满足幂等要求的
  Handler。若业务依赖聚合内顺序，必须保持 1，或先设计并验证显式顺序键。
- 并发路径为每条消息创建独立 DI Scope、Handler 与数据库会话。连接池和下游并发预算
  必须覆盖每个 Worker 的 `MaxConcurrency` 乘以副本数，禁止把并发上限直接等同于可用吞吐。
- 续租周期必须留出数据库抖动余量。降低 `LeaseSeconds` 时必须同步设置
  `LeaseRenewalSeconds`，否则启动期校验会拒绝不安全配置。
- 每次续租创建独立 DI Scope 和数据库会话，不与 Handler 共享连接；连接池预算须额外
  覆盖每个活跃批次的续租命令。
- 续租更新按批次消息主键集合定位，并同时匹配相同 `LockId`、未处理且非死信状态；不会
  周期性按未索引的 `LockId` 扫描整个积压表。截止时间使用单调更新，宿主时钟回拨不得
  缩短已持有租约；MySQL 连接固定使用 matched-row 计数，避免同值更新误报零行。
- 零行更新或续租数据库故障会取消当前批次的协作式 Handler 并让本轮失败，保留租约到期
  恢复语义；若最后一条消息已先进入成功终态，则保持该成功结果，不误报租约丢失。
- 主动续租缩小长 Handler 与批尾消息的重复消费窗口，但进程崩溃、网络分区和终态确认
  竞态仍可能产生重复；Handler 仍必须幂等，禁止把该能力描述为 Exactly-Once。
- 若生产明确要求单副本运行，必须在部署清单中显式写死 `replicas: 1`，并记录“单副本故障期间 Outbox 会暂停消费”的风险。

### 3.1 消费容量矩阵

独立容量入口使用真实 `OutboxProcessor`、Dapper Store 和隔离双库容器：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- outbox-capacity
```

正式默认矩阵覆盖并发 `1/2/4/8`、Handler 延迟 `0/10/100/1000ms`、副本 `1/2`，
并在参考并发档比较 Batch `20/100` 与 Payload `256/4096`。矩阵刻意压缩为 35 个
代表场景，每档默认三轮；不得扩成全参数笛卡尔积，也不得把它加入日常受影响测试。

开发期应使用 `--providers`、`--concurrency`、`--handler-delay-ms`、`--replicas`、
`--batch-sizes`、`--payload-sizes`、`--repetitions 1` 和短采样显式收窄。报告必须同时
检查吞吐、Handler P95/P99、重复投递、续租命令、连接池、锁等待、日志写入、GC/分配、
数据库容器资源和期末 backlog。任何正确性门禁失败、数据库证据缺失或 backlog 被意外
排空，都不能作为提高默认并发的证据。

默认还会在每个 Provider 执行遗弃租约恢复：先由真实 Store 领取单条消息并模拟 Handler
已经开始产生副作用，随后不写成功、失败或死信终态，让真实 `OutboxProcessor` 在租约
到期后接管。报告要求恢复前后复用同一 `MessageId`、`Attempts = 2`、重复窗口为 1、
恢复时间不早于租约边界且不超过 `LeaseSeconds + RecoveryGraceSeconds`。开发期可用
`--recovery false` 跳过这段等待，但正式容量证据不得关闭；`--recovery-grace-seconds`
只控制测量超时余量，不改变生产 Worker 租约。

容量入口默认启用 `--resume true`。每完成一个普通场景或恢复轮次，就以临时文件加原子
替换更新 `report.json` 和 `summary.md`；同一 `--output` 可在后续任务窗口继续。恢复时会
校验程序集源版本、Provider、矩阵、预热、时长、种子、租约和恢复参数，任一漂移都会拒绝
合并。报告进度必须达到 `COMPLETE`，`PARTIAL` 只能作为断点，不得用于默认并发或索引决策。
需要故意覆盖同名目录重新采样时显式使用 `--resume false`，并保留旧工件后再执行。

长矩阵可设置 `--max-new-samples <n>`，在本次新增并持久化 N 个普通场景或恢复样本后
正常退出并释放容器。该参数必须与 `--resume true` 一起使用；`0` 表示不限制。旧
checkpoint 中已完成而被跳过的键不消耗本次预算，因此不同任务窗口可以使用不同的 N，
但仍必须复用同一构建版本、矩阵参数和输出目录。

## 4. 积压指标

Worker 将 `Full.NET.Outbox` Meter 接入 OpenTelemetry，并按
`OutboxWorker:BacklogSampleSeconds` 读取一次只读快照：

| 指标 | 单位 | 语义 |
| --- | --- | --- |
| `fullnet.outbox.backlog.messages` | `{message}` | `ProcessedAtUtc IS NULL AND DeadLetteredAtUtc IS NULL` 的消息总数 |
| `fullnet.outbox.backlog.oldest_age` | `s` | 最老待处理消息从 `OccurredAtUtc` 到采样时刻的非负年龄；空队列为 0 |

两项指标都没有标签，禁止追加租户、消息类型、异常文本或消息标识。backlog 包含等待
`NextAttemptAtUtc`、仍持有租约和当前可领取的全部未处理消息；本轮 `AcquireAsync` 返回数量
不能替代该值。

采样先推进下一个允许时间，再执行 SQL。数据库查询、映射或指标消费者失败时，Worker 记录
一次稳定 Warning 并继续领取消息，避免可观测旁路反转可靠处理结果；收到宿主取消时仍立即
传播取消。现有 Pending 索引以 `ProcessedAtUtc` 开头，查询可先收窄到未处理索引范围；
生产大 backlog 的实际执行计划和 IO 仍须在容量测试中确认。

告警必须同时看“数量”和“年龄”：

- 数量瞬时增长但年龄持续回落，通常表示突发流量正在被正常追平；
- 数量不高但最老年龄持续增长，通常表示单条重试、租约或处理器卡住；
- 两项连续至少两个采样周期增长时应进入排障，具体阈值必须由消息 SLA 和生产基线确定；
- 多 Worker 副本读取的是同一数据库 backlog，跨 `service.instance.id` 汇总时使用 `max`，
  不要把各副本 Gauge 相加；
- 指标长时间无新样本时先检查 Worker/OTLP 导出链路，不能把旧样本当成当前健康状态。

本仓库只证明 Meter、双库查询和采样旁路；生产 OTLP 后端、仪表盘与告警规则尚未实跑，
因此不能据此把 Outbox 标记为 `Verified`。

## 5. 死信语义

死信消息在数据库中保留：

- 原始 `Payload`；
- `Attempts`；
- `Error` 失败摘要；
- `DeadLetteredAtUtc`；
- `DeadLetterReasonCode`。

当前稳定原因码如下：

| 原因码 | 触发条件 |
| --- | --- |
| `outbox.unsupported_content_type` | `ContentType` 不是当前 Worker 支持的线格式 |
| `outbox.handler_not_found` | 找不到当前 `MessageType + SchemaVersion` 的唯一处理器 |
| `outbox.ambiguous_handler` | 同一路由存在多个处理器 |
| `outbox.invalid_payload` | MessagePack/格式损坏，继续重试无法恢复 |
| `outbox.max_attempts_exceeded` | 瞬时失败累计达到 `OutboxWorker:MaxAttempts` 上限 |

当前不会进入死信的场景：

- 普通瞬时业务异常，且 `Attempts` 仍小于 `MaxAttempts`；
- 可通过后续重试自然恢复的依赖性故障。

## 6. 版本发布顺序

当前版本共存策略是“并行 Handler + 精确版本匹配”：

- 先部署消费者：新版本处理器先上线，并保留旧版本处理器。
- 再部署生产者：确认全部消费端已能处理新 `SchemaVersion` 后，生产者再开始写新版本消息。
- 最后退役旧版本：确认库内旧版本消息已排空、死信已处理且不再有旧生产者写入后，才允许移除旧 Handler。

旧 Handler 仍在当前发布物中时，可从仓库根目录运行一次性只读扫描：

```powershell
dotnet run --project src/Hosts/Full.NET.Host.Worker -c Release --no-build -- `
  --outbox-version-retirement-message-type fullnet.tenancy.tenant.provisioned `
  --outbox-version-retirement-schema-version 1
```

扫描契约如下：

- `message-type` 可填写 Handler 当前声明的规范路由或历史别名；工具先解析到唯一 Handler，再同时检查该 Handler 的规范路由与全部历史别名。
- `schema-version` 必须是正整数，并只检查该精确版本。
- 只统计 `ProcessedAtUtc IS NULL` 的记录；普通待处理和死信分别计数，任一非零都会阻止退役。
- 报告只包含稳定结果码、输入路由、精确版本、实际扫描路由、两个计数与最老未处理时间，不输出载荷、租户或消息标识，也不会修改、领取或重放消息。
- 安全排空返回 `0` 和 `outbox.version_retirement.safe`；仍有阻塞返回 `2` 和 `outbox.version_retirement.blocked`；命令或路由错误返回 `1` 和对应稳定错误码。

该结果是单一时间点证据。生产者冻结、完整发布窗口内不再写入旧版本、死信处置和观察期仍须由发布流程保证；当前也未实现自动升级链，不能假定系统会自动升格旧载荷。

## 7. 查询死信

SQL Server：

```sql
SELECT Id,
       MessageType,
       SchemaVersion,
       Attempts,
       DeadLetterReasonCode,
       DeadLetteredAtUtc,
       Error
FROM dbo.fn_outbox_message
WHERE DeadLetteredAtUtc IS NOT NULL
ORDER BY DeadLetteredAtUtc DESC;
```

MySQL：

```sql
SELECT Id,
       MessageType,
       SchemaVersion,
       Attempts,
       DeadLetterReasonCode,
       DeadLetteredAtUtc,
       Error
FROM fn_outbox_message
WHERE DeadLetteredAtUtc IS NOT NULL
ORDER BY DeadLetteredAtUtc DESC;
```

建议按以下维度汇总：

- `DeadLetterReasonCode` 分布；
- 最老死信时间；
- 同一 `MessageType + SchemaVersion` 的死信集中度；
- 是否存在持续增长的 `outbox.max_attempts_exceeded`。

## 8. 受控人工重放

当前仓库没有提供通用重放 API 或一键脚本。人工重放必须满足以下前提：

1. 已确认根因，例如缺失 Handler、坏载荷生产者缺陷、依赖故障或错误配置。
2. 根因修复已经部署，并完成最小回归验证。
3. 已锁定待重放的精确消息集合，禁止批量扫表式“全量清死信”。
4. 已记录操作人、工单、时间窗与预期影响。

推荐流程：

1. 先用只读 SQL 导出目标消息的 `Id`、`MessageType`、`SchemaVersion`、`Attempts`、`DeadLetterReasonCode` 与 `Error`。
2. 评估是否需要先停对应生产者或临时缩小 Worker 副本，避免修复前再次制造同类死信。
3. 在变更窗口内，仅对明确目标消息执行受控回队。
4. 回队后观察 `ProcessedAtUtc`、新的 `Attempts`、应用日志和死信计数是否符合预期。
5. 若再次死信，停止批量操作，按新的原因重新诊断。

受控回队示例只允许按精确 `Id` 执行，且必须保留 `ProcessedAtUtc IS NULL` 防护：

SQL Server：

```sql
UPDATE dbo.fn_outbox_message
SET DeadLetteredAtUtc = NULL,
    DeadLetterReasonCode = NULL,
    NextAttemptAtUtc = SYSDATETIMEOFFSET(),
    LockId = NULL,
    LockedUntilUtc = NULL,
    Error = NULL
WHERE Id = @Id
  AND ProcessedAtUtc IS NULL
  AND DeadLetteredAtUtc IS NOT NULL;
```

MySQL：

```sql
UPDATE fn_outbox_message
SET DeadLetteredAtUtc = NULL,
    DeadLetterReasonCode = NULL,
    NextAttemptAtUtc = UTC_TIMESTAMP(6),
    LockId = NULL,
    LockedUntilUtc = NULL,
    Error = NULL
WHERE Id = @Id
  AND ProcessedAtUtc IS NULL
  AND DeadLetteredAtUtc IS NOT NULL;
```

说明：

- 当前示例不会重置 `Attempts`；因此同一消息若已经达到 `MaxAttempts`，回队后会在下一次失败时再次进入死信。
- 若确有业务需要重置 `Attempts`，必须在独立工单中说明理由、风险与审计方式，不得在日常排障中默认执行。
- 坏载荷或错误 schema 的消息，在根因未修复前禁止回队。

## 9. 当前缺口

以下事项仍是后续工作，不应被当前文档误读为已完成：

- 相邻版本升级链与生产发布平台中的持续退役门禁；
- 真实多副本压力基准与容量建议；
- 受控人工重放自动化工具；
- 结合 Redis/编排器的更广生产演练。
