# Kafka Capacity Scope C Implementation Plan

**Status:** 2026-08-15 Tasks 1–8 已实现并完成本地验证。MySQL + Connect + 真实 Kafka 缩减集成测试已通过；SQL Server 路径在 Testcontainers CDC Agent 不可用时按设计 `Inconclusive`。专用 Scope C 工作流 smoke 仍保留为后续运维任务。生产等价容量矩阵尚未运行，状态保持 `Capacity-not-verified`。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现 `transaction_outbox_cdc` 容量范围，测量 `开环调度 → 业务事务 Outbox → Debezium CDC → Kafka → 生产 Inbox/Handler` 全链路，同时保持 Scope A/B 兼容。

**Architecture:** 生产侧复用 `IOutboxWriter` + `ICommandTransaction`；CDC 侧通过外部 Kafka Connect REST 注册容量 Connector（`deploy/messaging/connectors/*-outbox-capacity.json`，Topic 前缀 `fullnet.capacity.cdc`）；消费侧复用 Scope B 的 `KafkaCapacityWorkerConsumerLoop` 与生产 `KafkaConsumerMessageProcessor`。Runner 对 Scope C 跳过 owned topic 删除；manifest/checkpoint 记录 Connect/Connector 摘要。

**Tech Stack:** .NET 10、Dapper、Confluent.Kafka、Debezium Connect 3.4.3 REST、SQL Server/MySQL、MSTest。

## Global Constraints

- 稳定机器码：`transaction_outbox_cdc`。
- Connect 拓扑：外部 Connect REST（`KafkaCapacity:Connect:BaseUri`）；Runner 不自启 Connect 容器。
- 事件契约：复用 `fullnet.capacity.worker.message` / schema 1 / `CdcKafka` 所有权；Outbox ContentType 必须为 Envelope V2 认可的 MessagePack 字符串（负载仍为自定义二进制 Codec）。
- 正确性硬门禁：`Enqueued == Acknowledged == CdcPublished == Consumed`，Lost/Duplicate/Corrupted/OutOfOrder/Unflushed 全 0，排空完成。
- 未在专用生产等价环境执行前继续 `Build-verified / Capacity-not-verified`；不得解除 ADR-0006 切流门禁。

## Delivered Artifacts

| 区域 | 路径 |
| --- | --- |
| Scope C Driver | `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityOutboxCdcDriver.cs` |
| Outbox / CDC / Connect | `KafkaCapacityOutboxProducer.cs`、`KafkaCapacityCdcTracker.cs`、`KafkaCapacityConnectAdminClient.cs`、`KafkaCapacityConnectorTemplateFactory.cs` |
| 消费内核抽取 | `KafkaCapacityWorkerConsumerLoop.cs` |
| Topic 策略 | `KafkaCapacityRunTopicResolver.cs` |
| Connector 模板 | `deploy/messaging/connectors/mysql-outbox-capacity.json`、`sqlserver-outbox-capacity.json` |
| 单元测试 | `tests/Full.NET.UnitTests/Messaging/KafkaCapacityOutboxCdcTests.cs` |
| 集成测试 | `tests/Full.NET.IntegrationTests/Messaging/KafkaOutboxCdcCapacityRunnerTests.cs` |
| 运维与状态 | `docs/operations/kafka-capacity-runner.md`、`docs/roadmap/capability-status.md` |

## Verification Record (2026-08-15)

- Scope C MySQL + Connect 缩减 CLI：`exit 0`，`CorrectnessPassed`，`outboxCdc.cdcPublished == consumed == acknowledged`。
- KafkaCapacity 聚焦单测：86/86 通过（Unit）。
- Scope B 回归：既有 KafkaCapacity 单测/集成子集保持绿。
- Architecture / Governance / Release build：见 Task 8 合并门禁输出。
- 任务快照：`codex-kafka-capacity-scope-c-20260815`；基线提交：`bfe20f6019d4ba85713770c7541e9bdc907a1f3e`。

## Follow-ups (Out of Scope)

- GitHub 工作流 `scope_c_smoke` Profile（Connect + DB Secret 隔离）。
- 专用生产等价 Scope C 正式矩阵与 Soak。
- SQL Server CDC 在 CI 容器栈长期 Inconclusive 的生产等价认证路径。
