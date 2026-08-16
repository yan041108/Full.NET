# Outbox / CDC / Kafka Worker 轮询与背压修复计划

**目标：** 修正 Cursor 提交后暴露的旧 Outbox 路由、CDC 切流竞态、Kafka Retry 不消费、长时 Handler 阻断 Kafka Poll，以及空闲数据库轮询压力问题。

**可靠性边界：** 业务事务只写一个 Outbox；Legacy/Shadow 仍由旧 Worker 消费；CDC 流只写追加表并由 Broker/Inbox 消费；任何背压都只能延迟处理，不能丢弃事件或提前提交 Offset。

**实施状态（2026-08-09）：** Task 1—4 已完成代码修复与聚焦验证；Task 5 的本地构建、单元、双库切换/回退/Inbox、Kafka Retry/DLQ/中断恢复已完成。**2026-08-16 superseded：** Organization CDC E2E 恢复 `Build-verified / Pilot`（Task 6）；真实 Debezium 全链路与生产等价容量认证仍未完成，切流默认关闭并继续标记 `Capacity-not-verified`。

## Task 1：恢复 Legacy Outbox 正确路由并降低空转轮询

- 为未登记的既有事件流保留 `LegacyPolling` 默认所有权，避免 Notifications 等现有生产者因 Topic 目录不完整而运行时失败。
- Legacy Worker 接受 `LegacyPolling` 与 `ShadowCdc`；不得把 Shadow 事件误判为所有权撤销后写入死信。
- 空批次使用有上限的指数退避；有积压时立即继续，小批次恢复基础间隔。
- 增加单元测试覆盖未知流、Shadow 流、退避上限与积压恢复。

## Task 2：实现 Kafka Poll 心跳与有界背压

- Consumer 每个 Group 同时只处理一条消息；处理期间暂停分区，并以短超时继续调用 `Consume`，维持心跳和 Rebalance 回调。
- Inbox/Handler、Retry/DLQ 发布不得直接调用 Consumer；处理结果返回 Poll 循环后再由唯一 Consumer 调用路径提交 Offset。
- Consumer 客户端预取队列设置显式上限，配置心跳轮询间隔并验证其小于 Session/MaxPoll 边界。
- 将实际 Worker 的 Pause/Poll/Commit/Seek/Resume 与关闭排空抽为可测试边界；关闭超时后继续观察故障任务，避免未观察异常。
- 增加长时处理不停止 Poll、失败不提交、成功提交及有界关闭排空的验证。

## Task 3：闭合 Retry Topic

- Worker 同时订阅基础 Topic 与所有静态 Retry Topic。
- Retry 发布写入稳定的 `retry_not_before_utc` 头；消费过早时暂停并持续 Poll，到期后再进入 Inbox。
- Retry/DLQ 发布成功后才提交来源 Offset；发布失败保持未提交。
- 增加 Topic 计划、延迟头解析与重试阶段验证。

## Task 4：关闭不安全正式切流

- 在生产者读所有权/写 Outbox 与切流更新之间建立同一数据库事务可证明的流级互斥；SQL Server/MySQL 必须成对实现。
- 在互斥与真实双库 CDC→Kafka→Inbox E2E 完成前，正式 `CdcKafka` 切流入口保持失败关闭，不能依赖文档提醒。
- 回退同样失败关闭：第一事务先等待既有生产者并提交持久化 generation fence，新生产者停止写入后才在事务外执行 Connector fencing 与 Broker 排空；最终事务只接受同一 generation 且 CDC 位点覆盖数据库 producer fence 的证明。生产适配器未完成时保留拒绝全部回退的默认实现。
- 删除或隔离 synthetic mirror 证据；**2026-08-16 superseded：** 能力状态已升为 `Build-verified / Pilot`（见 Task 6 E2E），仍禁止默认切流。

## Task 5：验证

- 运行 Messaging/Outbox/Kafka 单元测试、架构测试、SQL Server/MySQL 相关集成测试。
- 运行任务快照影响集、`dotnet build Full.NET.slnx -c Release --no-restore`、`git diff --check`。
- 将实际执行命令、结果和未验证项写入 `docs/verification/`，不得把外部 Connector 未运行写成通过。
