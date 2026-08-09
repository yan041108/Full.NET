# Cursor 后续开发任务：消息链路生产化与 Document 契约收口

**执行方式：** 按 Task 1 → 4 顺序逐项完成；每个 Task 单独建立任务快照、单独验证、单独提交，不要把多个 Task 混成一个提交。

**当前边界：** Outbox/CDC/Kafka 应用侧可靠性硬化已完成，但真实 Debezium 双库端到端、生产等价容量认证和告警接入尚未完成；在这些门禁关闭前，`Messaging:DeliveryCutover:Enabled` 必须保持 `false`，并继续标记 `Capacity-not-verified`。

## Task 1（P0）：收口 Document 前端契约与现有红色门禁

### 目标

把当前工作区中未纳入消息链路提交的 Document 改动整理成独立纵向切片，消除 OpenAPI/TypeScript 契约漂移。

### 实施要求

1. 对齐后端 `HostDocument*Contracts`、公开分享访问 Endpoint 与 `packages/client-contracts/src/document-*.ts`。
2. 明确并统一公开分享访问语义：受密码保护的分享不得使用会泄漏密码或与服务端不一致的 GET；以服务端真实 Endpoint 和 ProblemDetails 为准更新客户端。
3. 清理或正式退役重复的 `host-document-*` / `document-*` DTO 与 barrel export，禁止保留两个可漂移的公共模型。
4. 将 Document 的 Vue API 模块登记到 `contracts/openapi/vue-client-coverage-v1.json`，补齐必要 OpenAPI fixture；不要通过降低覆盖门槛绕过。
5. 将 `eng/testing/test-matrix.json` 中 API/full 最小发现数更新为完成该切片后的真实值。

### 验收

```powershell
pnpm test:openapi
pnpm --filter @fullnet/client-contracts test
pnpm --filter @fullnet/admin test
pnpm --filter @fullnet/admin build
pnpm test:integration:affected:plan -- --snapshot <task-id> --phase slice
pnpm test:integration:affected -- --snapshot <task-id> --phase slice
git diff --check
```

## Task 2（P0）：建立 SQL Server/MySQL CDC → Debezium → Kafka → Inbox 真实 E2E

### 目标

关闭 ADR-0006 当前最大的未验证项，证明两种 Provider 的已提交 Outbox 行能够从真实 CDC/Binlog 经 Debezium 和 Kafka 进入 Inbox，并保持至少一次与幂等语义。

### 实施要求

1. 使用固定版本 Testcontainers：SQL Server、MySQL、Kafka 4.1.2、Debezium 3.4.3.Final；测试镜像不得直接升级为生产发布结论。
2. 分别验证 SQL Server CDC 与 MySQL ROW Binlog/FULL row image；Connector 只捕获 `fn_messaging_outbox_event` 的 INSERT。
3. 覆盖正常交付、重复事件、Consumer 重启、Connector 重启、Broker 短暂中断、Offset 未提交重投、Retry/DLQ、Rebalance、切流与回退边界。
4. 断言业务状态、Outbox、Inbox 和 Handler 写入处于正确本地事务；不得宣称 Exactly-Once。
5. 测试环境不可用时明确报告未验证，禁止把 mock/直接 Produce Kafka 的测试当 CDC E2E。
6. 实现生产 `IEventDeliveryRollbackReadinessReader` 控制面适配器：绑定已持久化的 `RollbackGeneration`，读取 SQL Server CDC LSN 或 MySQL binlog producer-fence position，停止并 fence Connector/Consumer，证明 Broker 已排空或隔离，并验证 Connector source position 已覆盖 producer fence；证明必须在所有权事务提交前持续有效，失败或超时一律拒绝回退，`AbortAsync` 必须幂等恢复同一 generation。

### 验收

- SQL Server 与 MySQL 各有独立、可重复的真实全链路测试。
- Connector 位点与 Schema History 使用持久化 Topic，重启后不跳过事件。
- 失败场景无丢消息、无越 Offset、重复业务副作用为零。
- 正式回退只有在真实控制面证明满足 Connector fencing、Broker 排空/隔离、位点与 Outbox 边界一致时成功；撤销或过期证明必须失败关闭。
- 更新 `docs/verification/`，保留镜像摘要、命令、结果和未验证项。

## Task 3（P1）：补齐消息链路可观测性、告警和运维恢复

### 目标

让运维能在切流前判断安全条件，并在 CDC、Broker、Consumer 或数据库保留窗口异常时失败关闭。

### 实施要求

1. 接入低基数指标：Legacy 空轮询退避、Outbox 最老年龄、commit-to-capture、Connector lag/error、Consumer lag、Inbox duplicate、Retry/DLQ、ownership wait/transition、未提交重试次数。
2. 为 SQL Server CDC Capture/Cleanup Job 停止、MySQL Binlog 保留不足、Connector Offset 不可恢复、Kafka lag 接近保留期建立告警。
3. 在现有 runbook 中补充切流、停止、位点记录、排空/隔离、回退、DLQ 重放与对账步骤；每一步给出停止条件和回滚条件。
4. Secret、原始 SQL、Payload、Tenant/User、异常消息不得进入指标标签或日志。

### 验收

- 单元/集成测试验证指标标签白名单和关键状态转换。
- 至少一次故障演练能按 runbook 恢复且无数据跳跃。
- 未接入告警或恢复演练失败时，切流开关保持关闭。

## Task 4（P1）：生产等价背压与容量认证

### 目标

在专用环境量化 Legacy 轮询、CDC Relay、Kafka Consumer 和数据库锁/连接池上限，为试点切流提供证据。

### 实施要求

1. 定义代表性事件大小、分区键分布、积压规模、Consumer 数量、运行时长和故障注入场景。
2. 记录吞吐、错误率、P50/P95/P99、Consumer lag、最老消息年龄、Retry/DLQ、SQL CPU/IO/锁等待、连接池等待与恢复时间。
3. 验证有界队列、单组单消息在途、Pause/Poll heartbeat、关闭排空、Broker 中断与 Rebalance；不得用无界并发提高平均吞吐。
4. SQL Server/MySQL 必须分别认证；任一 Provider 未完成时不得给出统一容量承诺。

### 验收

- 产出可重复的基线和候选结果，原始结果进入批准的位置。
- 不丢事件、不越 Offset、重复副作用为零、锁等待和连接池在预算内。
- 只有通过真实双库 E2E、告警/恢复和容量门禁后，才可提出单流试点开启 `Messaging:DeliveryCutover:Enabled=true` 的变更；该变更必须独立审批和提交。
