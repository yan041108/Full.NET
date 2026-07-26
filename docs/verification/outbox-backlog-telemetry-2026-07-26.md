# Outbox Backlog 指标验证记录

- 日期：2026-07-26（Asia/Shanghai）
- 分支：`codex/outbox-backlog-telemetry`
- 基线：`790754e` 之后的隔离工作树
- 状态：实现与聚焦验证完成；完整主线验证待并行日志任务提交后执行

## 范围与合同

本切片补齐架构硬化 Task 7 明确缺失的 Outbox backlog 指标：

- 新增只读 `IOutboxBacklogReader`，不修改既有 `IOutboxStore` 租约合同；
- Dapper 同一 scoped 实例同时实现读取与租约接口；
- backlog 精确定义为 `ProcessedAtUtc IS NULL AND DeadLetteredAtUtc IS NULL`；
- `Full.NET.Outbox` Meter 暴露无标签待处理数量和最老消息年龄；
- `OutboxWorker:BacklogSampleSeconds` 默认 30 秒，启动期限制为 5～3600 秒；
- 采样和指标消费者属于旁路，失败不得阻断消息领取与处理。

不在本切片范围：数据库结构变更、死信重放工具、生产 OTLP 后端、仪表盘、告警阈值和
Outbox 容量基准。

## RED / GREEN

| 阶段 | 证据 |
| --- | --- |
| 指标合同 RED | Unit 构建仅因缺少 `ReadBacklogAsync`、`OutboxBacklogSnapshot`、`OutboxBacklogTelemetry` 失败 |
| 指标合同 GREEN | `OutboxProcessorTests` **9/9**，数量 2、年龄 90 秒，采样异常不阻断成功处理 |
| 采样压力 RED | 新测试仅因 `BacklogSampleSeconds` 尚不存在而编译失败 |
| 采样压力 GREEN | `OutboxProcessorTests` **11/11**；同一 60 秒窗口只查询一次，配置低于 5 秒被拒绝 |
| 兼容性复核 | backlog 从 `IOutboxStore` 拆为独立 `IOutboxBacklogReader`，保留原公开租约接口 |

## 双库结果

`OutboxRecoveryTests` 为 SQL Server/MySQL 各增加一项真实数据库快照测试：

1. 先终结一条更早的死信，证明它既不计入 backlog 也不影响最老时间；
2. 两条待处理消息发生时间相差两分钟，初始快照为 `2 / 第一条时间`；
3. 完成第一条后为 `1 / 第二条时间`；
4. 完成第二条后为 `0 / null`。

补强死信排除断言后的最终聚焦运行结果为 **8/8**，失败 **0**、跳过 **0**，耗时
**2m50s**，stderr 为 0。Integration Release 构建同时为 0 warning/0 error。该结果证明
SQL Server `datetimeoffset` 与 MySQL `datetime(6)` 显式 UTC 映射一致。

## 指标合同

| Meter / Instrument | 单位 | 标签 |
| --- | --- | --- |
| `Full.NET.Outbox` / `fullnet.outbox.backlog.messages` | `{message}` | 无 |
| `Full.NET.Outbox` / `fullnet.outbox.backlog.oldest_age` | `s` | 无 |

空队列同时记录 `0` 条和 `0` 秒。年龄由 Worker 的 `IClock.UtcNow` 与数据库最老
`OccurredAtUtc` 计算并钳制为非负值；不记录租户、消息类型、消息 ID 或异常文本。

## 已完成的附加门禁

| 门禁 | 结果 |
| --- | --- |
| Unit 聚焦 | **11/11** |
| Architecture | **49/49** |
| Naming | **23/23** |
| `git diff --check` | 通过 |

## 规则与 Skills 复盘

- 规则：实现中曾把只读快照直接加入公开 `IOutboxStore`，复核后已拆为增量
  `IOutboxBacklogReader`。该问题已由现有“不得静默更改公共 API”规则覆盖，属于本次单次
  缺陷，没有达到新增规则或候选经验的门槛。
- Skills：本切片是既有 Outbox 基础设施的指标补齐，没有形成第二个业务模块的可靠事件交付
  工作流，也没有暴露 `fullnet-module-delivery` 的新缺口；本次无 Skills 变化。

## 待最终回填

并行高优先级日志任务仍占用主工作区并运行完整 Integration。本分支将在其提交后合入最新
`main`，统一 canonical 测试门槛，再执行 Release 构建、Unit、Compatibility、
Architecture、Integration 全量与全部治理门禁。本节在获得新鲜最终输出后更新，当前不能
把上述待执行项描述为通过。
