# Full.NET Observability Deploy Baseline

Platform-owned Fluent Bit / OpenTelemetry Collector / Prometheus rules / Grafana dashboard overlays for the modular monolith. The application Helm chart does **not** install these backends.

## Contracts

- Application emits Compact JSON on stdout only.
- Fluent Bit: filesystem buffer, memory limits, corrupt-chunk isolation via independent DB files, retry limits, TLS.
- B2 Best Effort and Priority streams use separate capacity/routing; B2 cold archive goes to S3.
- B0/B1 Durable Audit remains in the Audit/module database path; Fluent Bit must not duplicate it as another “Durable log” pipeline.
- OTel Collector enables `memory_limiter`, `batch`, retry, and `file_storage` queues.
- Pipeline failures must not recurse into the same sink.
- Required log fields: `timestamp`, `level`, `message`, `application`, `instance`, `trace_id`, `span_id`, `log.class`, `log.stream`, `reliability.class`, `data.classification`, `DiagnosticGroup`, `EventName`.
- Dynamic `DiagnosticGroup` must not become file names, index names, tenant labels, or Metrics labels.

## Apply (platform)

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
