# Outbox / CDC / Kafka Worker 轮询与背压修复验证

## 1. 范围与基线

- 基线提交：`432f8088bbc71d739188c0164eb6af09b98f16d1`
- 任务快照：`cursor-review-20260809-round2`
- 范围：旧 Outbox 路由与空闲轮询、CDC/Kafka 所有权切换互斥、Kafka Retry/DLQ/Offset、长时 Handler 心跳与有界背压、Inbox 异步租户上下文。

## 2. 已修复行为

- 未登记事件流和未装配 Messaging 模块的精简宿主安全回退到 `LegacyPolling`；`ShadowCdc` 仍由旧 Worker 处理。
- Outbox 空批次采用有上限的指数退避，满批次立即继续；Backlog 指标按独立周期采样。
- 生产者、Kafka Inbox Consumer 与切流/回退通过同一数据库事务内的流级共享/独占锁序列化，SQL Server 与 MySQL 成对实现。
- 正式切流默认关闭；旧 Outbox 截止位点由 Outbox 数据边界读取，Messaging 模块不再直接查询旧表。
- Kafka 禁用自动提交与自动 Offset Store，限制本地预取；处理期间暂停分区并持续 Poll heartbeat。
- 实际 Worker 流控抽取为可测试边界：处理期间 Pause/Poll，成功后 Commit，失败时仅在仍持有分区时 Seek；关闭时有界等待并持续观察超时后的故障任务。
- Retry Topic 纳入订阅，延迟头和尝试次数使用替换语义；跨 Retry 跳数保留首次来源 Topic/Partition/Offset，Retry/DLQ 发布成功后才提交来源 Offset。
- 未提交消息只在分区仍归当前 Consumer 时 Seek；Rebalance 已撤销时由新持有者从组内未提交 Offset 重投。
- Inbox Dispatcher 等待异步 Handler/事务完成后再清理租户上下文；修复前回归测试观测到 `TenantId = null`，修复后通过。
- 已发布 migration 094 保持字节级不可变；新增 095 以静态、幂等的双库约束收敛并写入试点流的 Legacy 所有权基线。MySQL 仅在执行旧 094 时精确移除其不兼容的 `ADD CONSTRAINT IF NOT EXISTS` 语句，由紧随其后的 095 完成等价收敛。
- 回退采用持久化 generation 的两阶段 fence：第一事务等待既有生产者并禁止新 Outbox 写入，事务外再停 Connector/排空 Broker，最终事务只接受覆盖数据库 producer fence 的 CDC 位点证明；失败按同一 generation 撤销。默认控制面实现始终拒绝回退。

## 3. 新鲜验证结果

| 命令 | 结果 |
|---|---|
| `dotnet build Full.NET.slnx -c Release --no-restore` | 通过，0 警告，0 错误 |
| `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-build --no-restore` | 1241/1241 通过 |
| `dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --no-build --no-restore` | 99/99 通过 |
| `pnpm test:integration:affected -- --snapshot cursor-review-20260809-round2 --phase merge` | 工具链、构建、分片与治理通过；随后 Cursor 未提交的 Document migration 093 双库恢复测试因缺少 `FileName` 等列失败，两个 Provider 对称失败后停止剩余执行；消息专项不在失败项中 |
| 切流、失败关闭回退、两阶段生产者 fence、migration 095 与 Kafka 故障恢复组合过滤 | 24/24 通过 |
| migration 094/095 发布兼容与升级恢复过滤 | 4/4 通过 |
| `node --test tests/deployment/messaging-cdc-contract.test.mjs` | 6/6 通过 |
| `pnpm test:integration:partitions` | 565 项无遗漏、无重复：56 + 56 + 296 + 157 |
| `pnpm test:governance` | 27/27 通过 |
| `pnpm test:naming` | 24/24 通过 |
| Outbox 续租故障先于终态的竞态用例连续复跑 | 5/5 通过 |

## 4. 未验证与发布边界

- 未运行真实 Debezium SQL Server CDC / MySQL Binlog 到 Kafka、Worker、Inbox、业务投影的端到端链路；当前 6 项仅证明部署合同与静态配置边界。
- 未在生产等价环境完成吞吐、尾延迟、Broker 中断、Consumer Rebalance 风暴和数据库锁等待容量认证，状态保持 `Capacity-not-verified`。
- `Messaging:DeliveryCutover:Enabled` 默认保持 `false`；在上述真实链路、排空和回退演练完成前不得打开正式 `CdcKafka` 切流。
- 生产控制面尚未提供 `IEventDeliveryRollbackReadinessReader` 适配器，默认失败关闭实现会拒绝所有正式回退；完成真实 Connector fencing、Broker 排空/隔离与 CDC 位点采集前不得替换该默认实现。
- 工作区中 Cursor 先前的 Document 前端扩展仍有独立红项：`pnpm test:openapi` 为 82/84，缺少 4 个 Vue API 覆盖登记及旧 barrel 导出；`pnpm --filter @fullnet/client-contracts test` 为 126/128，旧分类/标签兼容样例与扩展 DTO 不一致。这些不属于本次消息 Worker 运行时修复，不能据此宣称整个工作区全部门禁通过。

## 5. 结论

本次修复已用单元、架构、Kafka 故障恢复、任务快照影响集及 SQL Server/MySQL 双 Provider 测试证明本地语义闭合。CDC/Kafka 仍处于设计与 Shadow 证据阶段，不具备生产正式切流或容量达标结论。
