# Messaging CDC 可观测性（Scope C / Task 3）验证记录

- 日期：2026-08-20（Asia/Shanghai）
- 仓库：`g:\wwwroot\github_fork\Full.NET`
- 任务快照：`messaging-cdc-observability-20260820`
- 基线 HEAD（任务开始）：`e008f64eecf896c55894d7511483c6703fe8b353`
- 范围：后消息计划 Task 3 — 低基数指标补齐、Prometheus 告警对齐、切流/回退 runbook、标签白名单测试
- 切流开关：保持 `Messaging:DeliveryCutover:Enabled=false`
- 能力标记：`Capacity-not-verified`；**未**声明 `Production-verified`

## 1. 实现摘要

### Outbox（`OutboxBacklogTelemetry`）

| 仪表名 | 单位 | 说明 |
| --- | --- | --- |
| `fullnet.outbox.backlog.messages` | `{message}` | 既有积压条数 |
| `fullnet.outbox.backlog.oldest_age` | `s` | 最老待处理年龄（Prometheus：`fullnet_outbox_backlog_oldest_age_seconds`） |
| `fullnet.outbox.retry.due` / `lease.active` / `dead_letter.*` | — | 既有运维分类 |
| `fullnet.outbox.legacy.empty_poll.backoff` | `s` | Legacy 空轮询当前退避 |
| `fullnet.outbox.commit_to_capture` | `s` | 提交→捕获延迟；影子路径用 `OccurredAtUtc`→可见近似，标签仅 `database_provider` |

### Kafka / CDC（`KafkaMessagingTelemetry`）

| 仪表名 | 说明 |
| --- | --- |
| `fullnet.messaging.kafka.consumer.lag` | Consumer Group 消息滞后 |
| `fullnet.messaging.kafka.lag_retention_ratio` | 滞后相对保留窗口占比（平台可覆盖） |
| `fullnet.messaging.inbox.duplicates` | Inbox 幂等命中 |
| `fullnet.messaging.kafka.retry.routed` / `dead_letter.published` / `uncommitted.retry` | Retry / DLQ / 未提交重试 |
| `fullnet.messaging.ownership.wait` / `ownership.transitions` | 所有权等待与转换 |
| `fullnet.messaging.connector.lag` / `connector.errors` / `connector.offset.unrecoverable` | Connector 占位/采集入口 |
| `fullnet.messaging.cdc.sqlserver.capture_job_running` | SQL CDC Job 占位（默认 1） |
| `fullnet.messaging.cdc.mysql.binlog_retention_hours` | Binlog 保留占位（默认 168h） |

允许标签键：`provider`、`database_provider`、`topic_code`、`consumer_code`、`message_type_code`、`result`、`reason_code`、`connector_code`。禁止 Secret / Payload / SQL / Tenant / User 等片段。

## 2. 告警与文档

- 更新 `deploy/observability/prometheus-rules.yaml`：对齐 Outbox/Jobs 最老年龄指标名；新增 SQL CDC Job、MySQL Binlog 保留、Connector 位点不可恢复、Kafka lag 近保留告警。
- 更新 Grafana 面板表达式与 `tests/deployment/observability-contract.test.mjs`。
- 新增 `docs/runbooks/cdc-kafka-cutover-rollback.md`（切流/停止/fence/排空/回退/DLQ/对账与停止条件）。

## 3. 验证命令与结果

| 命令 | 结果 |
| --- | --- |
| `dotnet test` filter `MessagingCdcObservabilityTelemetryTests\|GetDelayAfterBatch\|KafkaMessagingTelemetry_uses_low_cardinality` | **8/8** 通过 |
| `pnpm test:observability-deploy` | **5/5** 通过 |
| `pnpm test:inner -- --snapshot messaging-cdc-observability-20260820` | Outbox+smoke **14/14** 通过（MySQL Provider，约 6m30s） |
| `Messaging:DeliveryCutover:Enabled` | 保持 `false`（Worker/API appsettings） |

## 4. 未验证项

- 生产 OTLP/Prometheus 实跑与告警 paging。
- 真实故障演练按 runbook 恢复（本切片仅交付 runbook 与指标契约）。
- SQL Server CDC Agent / MySQL Binlog 平台 exporter 接入（占位 Gauge 默认值不等于生产健康）。
- 容量矩阵与 Soak；状态保持 `Capacity-not-verified`。
- inner 阶段按预算仅跑 MySQL；SQL Server 对称 Outbox 快照未纳入本轮 inner。

## 5. 规则与 Skills

- 规则：未命中用户纠正、重复失败或高风险新类别；不更新规则候选。
- Skills：既有 `fullnet-performance-hardening` 已覆盖低基数指标与 Outbox 观测；无新 Skill 缺口。

## 6. 结论

Scope C Task 3 的代码指标、告警名对齐、runbook 与标签白名单测试已落地；正式切流开关保持关闭，不得据此提升为 `Production-verified`。
