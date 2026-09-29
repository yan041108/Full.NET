# Full.NET Observability Deploy Baseline

Platform-owned Fluent Bit / OpenTelemetry Collector / Prometheus rules / Grafana dashboard overlays for the modular monolith. The application Helm chart does **not** install these backends. The Fluent Bit values are an unqualified Collector candidate: they now tail matching container files once and select the `fullnet.io/log-ingress=collector` Pod label, but the fixed-version routing and failure tests in the [logging delivery plan](../../docs/superpowers/plans/2026-09-28-configurable-log-delivery.md) have not passed. The application Chart still rejects production `logging.ingress=Collector`.

## Contracts

- Application emits Compact JSON on stdout only.
- The Collector candidate has filesystem buffering, memory limits, one Tail checkpoint, retry limits, and TLS settings. These settings do not prove corrupt-chunk isolation or durable delivery.
- B2 Best Effort and Priority have separate output budgets. Routing and classification still require the fixed-version behavior test and load/failure evidence. B2 S3 archive is only a proposed output.
- B0/B1 Durable Audit remains in the Audit/module database path; Fluent Bit must not duplicate it as another “Durable log” pipeline.
- OTel Collector enables `memory_limiter`, `batch`, retry, and `file_storage` queues.
- Pipeline failures must not recurse into the same sink.
- The candidate requires nonempty Compact JSON `@t`/`@mt`, application-generated `Instance` and `log.class`, and a UUIDv7 `LogEventId`; records missing these fields are filtered out. This field gate is a candidate until the fixed-image behavior run passes. `@l` is omitted for Information; application, trace, stream, reliability, classification, and event names are conditional. The Collector removes `DiagnosticGroup` before outputs.
- Dynamic `DiagnosticGroup` must not become file names, index names, tenant labels, or Metrics labels.
- Static `pnpm test:observability-deploy` checks the presence of configuration and alerts. The dedicated `Fluent Bit Collector route` workflow runs real CRI/Docker samples against a pinned Fluent Bit image, including mixed Pod labels, missing metadata, overlapping Priority/Error classification, and a graceful restart with the Tail checkpoint retained. Until that workflow finishes on the exact change, these behaviors are unverified. A graceful checkpoint test does not prove crash persistence, output failure recovery, or delivery confirmation.

## Candidate apply example (not a Collector production procedure)

Do not apply the Fluent Bit values below as an approved `Collector` route. Run LG00/LG06 behavior and failure tests and qualify the platform outputs before a production cutover.

```bash
helm upgrade --install fullnet-fluent-bit <fluent-bit-chart> -f deploy/observability/fluent-bit-values.yaml
helm upgrade --install fullnet-otel <otel-collector-chart> -f deploy/observability/otel-collector-values.yaml
kubectl apply -f deploy/observability/prometheus-rules.yaml
# Import grafana-dashboard.json into Grafana
```

## Verification

```powershell
pnpm test:observability-deploy
```

Capacity remains `Capacity-not-verified` until Task 14 dedicated hardware certification.

## Messaging / CDC metric contracts

Application meters use dotted names; Prometheus/OTLP typically replace `.` with `_` and append the unit suffix (for example `s` → `_seconds`).

| Code instrument | Prometheus example | Notes |
| --- | --- | --- |
| `fullnet.outbox.backlog.oldest_age` | `fullnet_outbox_backlog_oldest_age_seconds` | Align alerts/dashboards to this name (not the legacy `fullnet_outbox_oldest_message_age_seconds`) |
| `fullnet.jobs.backlog.oldest_age` | `fullnet_jobs_backlog_oldest_age_seconds` | Same alignment |
| `fullnet.outbox.legacy.empty_poll.backoff` | `fullnet_outbox_legacy_empty_poll_backoff_seconds` | Legacy empty-poll backoff |
| `fullnet.outbox.commit_to_capture` | `fullnet_outbox_commit_to_capture_seconds` | Shadow path or platform fill; tag `database_provider` only |
| `fullnet.messaging.kafka.consumer.lag` | `fullnet_messaging_kafka_consumer_lag` | Messages behind high watermark |
| `fullnet.messaging.kafka.lag_retention_ratio` | `fullnet_messaging_kafka_lag_retention_ratio` | Platform should set time-based ratio for retention alerts |
| `fullnet.messaging.connector.lag` | `fullnet_messaging_connector_lag_seconds` | Placeholder until Connect exporter wired |
| `fullnet.messaging.connector.offset.unrecoverable` | `fullnet_messaging_connector_offset_unrecoverable` | 0/1 gauge |
| `fullnet.messaging.cdc.sqlserver.capture_job_running` | `fullnet_messaging_cdc_sqlserver_capture_job_running` | Platform fill via `UpdateCdcPlatformHealth` |
| `fullnet.messaging.cdc.mysql.binlog_retention_hours` | `fullnet_messaging_cdc_mysql_binlog_retention_hours` | Platform fill; alert &lt; 24h |

Label allow-list: `provider`, `database_provider`, `topic_code`, `consumer_code`, `message_type_code`, `result`, `reason_code`, `connector_code`. Forbidden: Secret, Payload, SQL, Tenant, User, MessageId, exception text.

Cutover/rollback steps: [`docs/runbooks/cdc-kafka-cutover-rollback.md`](../../docs/runbooks/cdc-kafka-cutover-rollback.md). Verification: [`docs/verification/messaging-cdc-observability-20260820.md`](../../docs/verification/messaging-cdc-observability-20260820.md).
