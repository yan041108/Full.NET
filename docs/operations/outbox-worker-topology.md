# Outbox Worker 运维说明

## 1. 适用范围

本文档说明 Full.NET 当前 Outbox Worker 的默认部署拓扑、运行参数、死信语义与受控人工重放边界。它只覆盖当前仓库已经实现的能力：

- MessagePack `application/x-msgpack` 载荷；
- `(MessageType, SchemaVersion)` 精确路由；
- 数据库租约领取；
- 最大尝试次数与死信终态；
- 待处理数量与最老消息年龄指标；
- SQL Server / MySQL 双库列契约。

本文档不声明尚未实现的能力，例如相邻版本升级链、版本退役扫描、Redis Leader Election 或通用一键自动重放工具。

## 2. 默认拓扑

- 默认多副本安全模型依赖数据库租约，而不是默认引入额外选主机制。
- 每个 Worker 周期调用 `AcquireAsync(batchSize, lease, ...)`，只领取 `ProcessedAtUtc IS NULL`、`DeadLetteredAtUtc IS NULL` 且租约已过期/未占用的消息。
- 成功领取时数据库会为消息写入新的 `LockId`、`LockedUntilUtc` 并递增 `Attempts`。
- 处理成功后消息被标记为 `ProcessedAtUtc`，并清理重试/租约/死信辅助字段。
- 临时失败会释放租约、保留错误摘要并设置 `NextAttemptAtUtc`，等待下次重试。
- 永久失败或达到最大尝试次数后，消息进入死信终态，不再被后续领取。

结论：当前生产默认模型是“数据库租约 + 幂等 Handler + 至少一次投递”。在没有真实压力证据前，不要把“必须上 Redis Leader Election”写成唯一解。

## 3. Worker 配置

配置节名称：`OutboxWorker`

```json
{
  "OutboxWorker": {
    "BatchSize": 20,
    "LeaseSeconds": 30,
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
| `LeaseSeconds` | 30 | 5..3600 | 单条消息租约持续时间；过期后允许其他 Worker 回收 |
| `PollMilliseconds` | 1000 | 100..60000 | 空轮询等待时间 |
| `BacklogSampleSeconds` | 30 | 5..3600 | 积压聚合查询与指标记录周期；采样失败不会阻断消息领取 |
| `MaxAttempts` | 5 | 1..100 | 单条消息允许的最大总尝试次数，包含当前领取 |

运维建议：

- 先从默认值启动，不要同时大幅提高 `BatchSize` 和 `LeaseSeconds`。
- 处理器耗时明显高于 `LeaseSeconds` 时，应优先缩小批量、优化处理器或提升租约，而不是直接增加副本数。
- 若生产明确要求单副本运行，必须在部署清单中显式写死 `replicas: 1`，并记录“单副本故障期间 Outbox 会暂停消费”的风险。

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

当前未实现“自动升级链”与“版本退役扫描”。因此运维与发布阶段必须显式检查旧版本消息是否已排空，不能假定系统会自动升格旧载荷。

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

- 相邻版本升级链与版本退役自动扫描；
- 真实多副本压力基准与容量建议；
- 受控人工重放自动化工具；
- 结合 Redis/编排器的更广生产演练。
