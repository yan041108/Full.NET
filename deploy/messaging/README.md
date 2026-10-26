# Full.NET Messaging CDC Shadow (Development / Test Only)

Kafka 4.1.2 and Debezium Connect overlays for **local development and integration testing only**. The application Helm chart (`deploy/helm/fullnet`) does **not** install Kafka, Debezium, or any CDC infrastructure.

## Scope

- Capture committed `INSERT` rows from the isolated diagnostic `fn_messaging_shadow_event` via SQL Server CDC or MySQL ROW Binlog.
- Route events to one explicitly configured `${FULLNET_SHADOW_TOPIC}` (for example `fullnet.dev.shadow.organization-unit.v1`) using Debezium Outbox Event Router.
- Shadow Topics are for comparison evidence only; **no business Consumer** may subscribe to them.
- Heartbeat, Schema History, and Connect internal topics use `fullnet.dev.shadow.internal.*` and are likewise not business-facing.

## Pinned images (no `:latest`)

| Component | Image |
| --- | --- |
| Kafka broker | `apache/kafka:4.1.2` |
| Debezium Connect | `quay.io/debezium/connect:3.4.3.Final` |

Production must **not** run these public images directly. Production Connect must be built by the trusted platform from fixed Debezium Connector artifacts (or an approved supported distribution), with vulnerability scan, signing, SBOM, and digest pinning evidence on file.

## Quick start (dev)

```bash
export FULLNET_SQLSERVER_HOST=host.docker.internal
export FULLNET_SQLSERVER_PORT=1433
export FULLNET_SQLSERVER_USER=fullnet_cdc_reader
export FULLNET_SQLSERVER_PASSWORD=replace-me
export FULLNET_SQLSERVER_DATABASE=fullnet_dev

export FULLNET_MYSQL_HOST=host.docker.internal
export FULLNET_MYSQL_PORT=3306
export FULLNET_MYSQL_USER=fullnet_cdc_reader
export FULLNET_MYSQL_PASSWORD=replace-me
export FULLNET_MYSQL_DATABASE=fullnet_dev

docker compose -f deploy/messaging/compose.kafka-debezium.yml up -d
```

Set `FULLNET_SHADOW_TOPIC` to the dedicated catalog binding. Expand connector placeholders before registration; Kafka Connect does not implicitly expand shell variables in submitted JSON.

Run `deploy/messaging/sqlserver/enable-shadow-cdc.sql` (privileged DBA operation, **not DbUp**) and `deploy/messaging/mysql/verify-binlog.sql` before registering connectors.

## Secret injection

Compose and connector templates use placeholder environment variables. Inject secrets via shell env, gitignored `.env`, or your platform secret store. Never commit passwords or connection strings.

## SQL Server CDC (privileged operations)

SQL Server CDC is an **explicit privileged DBA operation**:

1. Database-level CDC and SQL Server Agent must be enabled by operations - **not** by DbUp migrations, API startup, or Worker bootstrap.
2. Shadow CDC for `dbo.fn_messaging_shadow_event` uses stable capture instance `fullnet_fn_messaging_shadow_event`. Formal capacity profiles separately capture `fn_messaging_outbox_event`.
3. To disable: run `sqlserver/disable-shadow-cdc.sql` during approved maintenance.

## MySQL Binlog prerequisites

- `log_bin=ON`
- `binlog_format=ROW`
- `binlog_row_image=FULL`

Use `mysql/verify-binlog.sql` to inspect current settings.

MySQL 的 Shadow 与 Capacity 模板将 schema history 限制在配置的目标数据库及
对应的单表：Shadow 为 `fn_messaging_shadow_event`，Capacity 为 `fn_messaging_outbox_event`。两项 `schema.history.internal.store.only.captured.*.ddl`
均显式设为 `true`。`no_data` 仍需读取表结构，不能依赖数据库/表捕获过滤器避免扫描
实例中的其他历史库。此连接器仅负责固定 Outbox；未来若扩大捕获范围，须按
[Debezium schema history 恢复规则](https://debezium.io/documentation/reference/3.4/connectors/mysql.html#mysql-capturing-data-from-tables-not-captured-by-the-initial-snapshot-no-schema-change)
重新准备对应结构历史，不能直接假定旧 history 已包含新增表。

## Verification

All four Outbox templates explicitly use `io.debezium.converters.BinaryDataConverter`,
with `value.converter.delegate.converter.type=org.apache.kafka.connect.json.JsonConverter`
and `value.converter.delegate.converter.type.schemas.enable=false`. Business MemoryPack
bytes pass through unchanged; schema-change and heartbeat metadata use the delegate
on their separate infrastructure topics. The standard Kafka ByteArrayConverter rejects
those STRUCT records and stops the task. Cutover and rollback bind all three settings
to the resource identity. See the [Debezium 3.4 converter contract](https://debezium.io/documentation/reference/3.4/transformations/outbox-event-router.html#_using_apache_avro_as_the_payload_format).

Use the Debezium 3.4 key `topic.heartbeat.prefix`; `heartbeat.topics.prefix` is
rejected. The two MySQL profiles set `heartbeat.interval.ms=1000` so committed
source offsets can advance while the captured Outbox is quiet. Rollback captures
the database binlog boundary after the producer fence and control-table commits;
the last business record alone cannot prove that boundary. Heartbeats stay on
infrastructure topics and use the JSON delegate. The control plane binds the
heartbeat settings, rejects an application SQL heartbeat action, and retains the
source-position coverage check. See the [MySQL heartbeat options](https://debezium.io/documentation/reference/3.4/connectors/mysql.html#mysql-property-heartbeat-interval-ms).

```powershell
node --test tests/deployment/messaging-cdc-contract.test.mjs
pnpm test:helm
pnpm test:observability-deploy
```

## Production boundary

- `deploy/helm/fullnet` must not reference `quay.io/debezium/*` or ship Kafka/Debezium as application-owned stateful services.
- Shadow validation must complete before any formal business Consumer is enabled on CDC-delivered topics.

## Bounded real Legacy capture

`Messaging:ShadowCapture` defaults to disabled. Apply the paired 296 migrations before enabling this policy. Configure the same enabled policy on every business-producing API/Worker instance: `EventType`, `SchemaVersion`, `MaximumEvents` (1..10000), `MaximumPayloadBytes` (1..33554432) and `MaximumWindowSeconds` (1..300). A configured single pilot stream and persisted Legacy ownership are prerequisites. A missing ownership row is a blocker, never automatically created by capture endpoints.

Host Messaging → Delivery overview → Shadow capture reads the current generation. Starting and closing require `messaging.delivery.cutover`, trusted Host context and an audit reason. Readers require `messaging.events.read`. Creation retries retain RequestId and ExpectedGeneration; new generations remove only prior diagnostic payloads and invalidate prior evidence. Real `IOutboxWriter` Legacy writes reuse one ID/serialization/tenant/time and commit the diagnostic copy in the same transaction. Missing metadata blocks the generation. Quota/time termination keeps Legacy operating, never writes a formal copy, and cannot establish evidence on its own.

Bind separate `ConnectorName` and `ShadowConnectorName`, `TopicName` and `ShadowTopicName` under `Messaging:KafkaConnectRollback:Streams`. Both real connector configurations and identities participate in cutover proof. Both shadow connectors emit `CaptureGeneration:header:capture_generation` and preserve the exact `occurred_at_utc` header. MySQL uses `OccurredAtUtc` as the record timestamp. SQL Server `datetimeoffset` maps to a string ZonedTimestamp, so its template uses the default Debezium processing timestamp; do not add `table.field.event.timestamp=OccurredAtUtc` or silently remove a production setting inside a test factory. The verifier uses the earliest source time to locate a bounded immutable broker window and then checks every ID, generation, exact business timestamp and payload. Clock skew that excludes samples causes proof rejection. Identical at-least-once copies count once; shadow verification never invokes business handlers.

The [2026-10-25 shadow validation](../../docs/superpowers/plans/2026-10-25-messaging-real-cdc-validation.md) established controlled-capture evidence. The subsequent [full control-chain validation](../../docs/superpowers/plans/2026-10-25-messaging-real-cutover-rollback.md) passed both real MySQL and SQL Server Agent scenarios (2 passed, no failures or skips): business HTTP, durable proof, HTTP cutover, CDC/Kafka/Inbox, genuine pause, audit-failure recovery, successful rollback and Legacy delivery. The formal Topic high-water mark remains unchanged after rollback. Production Worker components run in the test process; multi-process recovery, network partitions, throughput, soak and independent review remain unverified. Capture and formal cutover remain disabled by default.
