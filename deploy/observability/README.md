# Full.NET Observability Deploy Baseline

Platform-owned Fluent Bit / OpenTelemetry Collector / Prometheus rules / Grafana dashboard overlays for the modular monolith. The application Helm chart does **not** install these backends. From 2026-09-30, project acceptance is based on actual local test results under [development quality §11](../../rules/development-quality.md#11-测试与验证); CI and a dedicated production-equivalent environment are optional. The application Chart permits production `logging.ingress=Collector` after the local Host/Helm and fixed-image routing/ACK/TLS/fault baseline. These values tail matching container files once and select the `fullnet.io/log-ingress=collector` Pod label. Platform destinations still require provisioning and their own local confirmation/recovery tests. Historical notes below retain their original evidence limits; they no longer impose an execution-location gate.

## Contracts

- Application emits Compact JSON on stdout only.
- The Collector candidate has filesystem buffering, memory limits, one Tail checkpoint, retry limits, and TLS settings. These settings do not prove corrupt-chunk isolation or durable delivery.
- B2 Best Effort and Priority have separate output budgets. Fixed-version local samples cover routing and classification; node load and downstream failure evidence remain open. B2 S3 archive is only a proposed output.
- B0/B1 Durable Audit remains in the Audit/module database path; Fluent Bit must not duplicate it as another “Durable log” pipeline.
- OTel Collector enables `memory_limiter`, `batch`, retry, and `file_storage` queues.
- Pipeline failures must not recurse into the same sink.
- The candidate requires nonempty Compact JSON `@t`/`@mt`, application-generated `Instance` and `log.class`, and a UUIDv7 `LogEventId`; records missing these fields are filtered out in the fixed-image local sample. `@l` is omitted for Information; application, trace, stream, reliability, classification, and event names are conditional. The Collector removes `DiagnosticGroup` before outputs.
- Dynamic `DiagnosticGroup` must not become file names, index names, tenant labels, or Metrics labels.
- Static `pnpm test:observability-deploy` checks the presence of configuration and alerts. The dedicated `Fluent Bit Collector route` workflow runs real CRI/Docker samples against a pinned Fluent Bit image, including mixed Pod labels, missing metadata, overlapping Priority/Error classification, Tail checkpoint restarts, and a bounded local output-fault sample. Local results and the finite-retry loss probe are in the [verification record](../../docs/verification/2026-09-29-fluent-bit-collector-route-fault.md). The workflow has not finished on the exact change; local samples do not prove downstream delivery confirmation.
- `pnpm test:observability-forward-ack:live` uses the pinned Fluent Bit image for both Forward roles, then replaces the receiver with a TCP peer that reads without acknowledging. It verifies successful output counters only advance after protocol ACK. The local fault test kills the sender before ACK, deletes the source log, and recovers the ID from its retained buffer; a receiver with `storage.sync full` also recovers the ACKed event after its own SIGKILL while downstream is unavailable. The [fault record](../../docs/verification/2026-09-30-fluent-bit-pre-ack-crash.md) limits this evidence to process crashes. The target platform must still prove its durable ACK boundary, volume/node failure behavior, and final delivery.
- Both Forward outputs require certificate-chain and hostname verification and read the same PEM CA bundle from `/fluent-bit/tls/ca.crt`. Before installing this candidate, provision the platform-owned `fullnet-forward-ca` Secret with a `ca.crt` key in the Fluent Bit namespace; its name is configurable in `extraVolumes`, and it is mounted read-only. Each receiver certificate SAN must match its configured `Host`. Roll Fluent Bit Pods after CA rotation until reload behavior is qualified. `pnpm test:observability-forward-tls:live` tests the configured path with an ephemeral CA and matching SAN, then checks rejection of an untrusted CA and a mismatched hostname. The [TLS record](../../docs/verification/2026-09-30-fluent-bit-forward-tls.md) covers the local fixed image only; target Secret projection, rotation, and receiver durability still need platform evidence.
- Output aliases keep priority Forward, best-effort Forward, and proposed archive metrics distinct. `FullNetFluentBitPriorityOutputDrop` and `FullNetFluentBitBestEffortOutputDrop` use Fluent Bit dropped-record and retry-exhausted counters. `FullNetFluentBitPriorityOutputRetryWithoutProgress` additionally detects Priority retries with no successful output records in the five-minute window after a two-minute hold; it does not prove that every individual record progressed when other records succeed. `FullNetFluentBitTargetDown` needs a discovered `job=fluent-bit` target; `FullNetFluentBitDaemonSetUnavailable` separately checks kube-state-metrics for missing Collector Pods while the DaemonSet exists. Neither rule detects a deleted DaemonSet without a separate existence monitor. Fixed-version local metrics, the shared smoke scrape configuration, and Prometheus `promtool` rule tests are checked by `pnpm test:observability-alerts`. The dedicated workflow also invokes the script exposed as `pnpm test:observability-alerts:live`: a pinned Prometheus scrapes a pinned Fluent Bit after an output fault and checks both firing loss alerts. The pinned live smoke passed locally on Windows Docker Desktop, including a zero-drop baseline, both firing output-loss alerts, and a target-down alert after stopping Fluent Bit. The no-progress and DaemonSet rules have `promtool` evidence only, pending target-platform scrape and notification tests. The smoke has not reached a CI terminal result on this change; the target platform's scrape, notification, and downstream delivery still need qualification.
- Scrape `/api/v2/metrics/prometheus` for the fixed Fluent Bit 4.1.1 candidate. `FullNetFluentBitForwardQueueCapacityLow` reports Forward logical queue capacity below 20% for five minutes; it is a diagnostic signal, not a pre-loss guarantee. In the [storage-pressure record](../../docs/verification/2026-09-30-fluent-bit-storage-pressure.md), a fixed-image `64KB` queue lost records while reporting 93.6% available capacity. The output-drop alerts detect that loss after it occurs. The former v1 `fluentbit_storage_chunks_up / fluentbit_storage_chunks_total` expression had no series. `FullNetNodeDiskPressure` replaces the PVC-only `FullNetFluentBitDiskFull` rule; node pressure cannot measure this Pod's `emptyDir` bytes or its 2 GiB limit. The ENOSPC probe shows input chunk write failures with zero output drops. Neither characterization qualifies Collector disk reliability; target-cluster scrape, `emptyDir` behavior, notification, and recovery still need verification.
- The ENOSPC probe also observes `fluentbit_input_ingestion_paused=0` and `fluentbit_input_storage_overlimit=0` during input chunk write failures. Neither gauge is an input disk-write error alarm for this candidate. Independent input-error monitoring and LogEventId reconciliation remain incomplete reliability work; application Collector configuration admission does not certify lossless delivery.
- `pnpm test:observability-logstash-pq:live` runs a pinned Kafka 4.1.2 → Logstash 9.5.4/PQ one-event SIGKILL/restart probe. The [PQ restart record](../../docs/verification/2026-09-30-logstash-pq-restart.md) checks recovery while the Broker is stopped. [Elastic's Kafka/PQ guidance](https://www.elastic.co/docs/reference/logstash/tips-best-practices) explicitly does not guarantee that offsets are committed only after events are safely persisted to the PQ. This combination is blocked for the project's reliable Kafka consumer admission; the probe is limited recovery evidence, not a production gate pass.
- `pnpm test:observability-vector-boundary:live` characterizes a separate pinned Vector Kafka → Elasticsearch candidate using item-level 429, 201, and 400 Bulk responses. The [boundary record](../../docs/verification/2026-09-30-vector-kafka-es-boundary.md) shows a permanent 400 item was dropped and a later successful item advanced the committed Offset past it. This configuration also fails reliable consumer admission.

## Platform apply example

### Independent log consumer monitoring

`pnpm test:log-consumer:throughput:live` extends the isolated local Kubernetes delivery runner with a bounded 1000-record burst after the TLS revocation/recovery checks. Each JSON event is exactly 2048 UTF-8 bytes; all IDs and complete document contents must match Elasticsearch, the DLQ must not grow, and the final committed Offset must reach 1003 with zero lag within a 120-second total observation budget. The `throughput` report records observed rates including Kafka CLI startup and commit-query overhead, consumer cgroup CPU/throttling deltas, Pod-lifetime memory peak and final downstream resource samples. Top-level functional counts retain the original three-record phase. This is a single-partition, single-consumer burst baseline, not maximum throughput, sustained capacity or API P99. Results are published after cleanup to `artifacts/log-consumer-throughput/result.json`; use `pnpm test:log-consumer:throughput-proof` for quick reconciliation tests.

For the local `kind-fullnet-local` discovery experiment, install the small, separate monitoring release using `log-consumer-operator-local-values.yaml`. It disables Grafana, Alertmanager, kube-state-metrics, node exporters and default cluster scraping. Both PodMonitor and namespace selectors require `fullnet.io/log-smoke=selected`; Prometheus is restricted to its dedicated namespace. This is local test tooling, not a production monitoring configuration. The release and cluster-wide Operator CRDs remain installed for repeat runs; the smoke runner removes its random consumer namespace, Secrets and downstream containers.

Use the pinned upstream chart archive (89.2.0, Operator v0.93.1), verifying its SHA-256 before installation:

```powershell
New-Item -ItemType Directory -Force artifacts/log-consumer-kubernetes | Out-Null
curl.exe -fL --retry 2 https://github.com/prometheus-community/helm-charts/releases/download/kube-prometheus-stack-89.2.0/kube-prometheus-stack-89.2.0.tgz -o artifacts/log-consumer-kubernetes/kube-prometheus-stack-89.2.0.tgz
if ($LASTEXITCODE -ne 0) { throw 'Chart download failed' }
if ((Get-FileHash artifacts/log-consumer-kubernetes/kube-prometheus-stack-89.2.0.tgz -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'f594687a8d7e471b2bc90886843500a0849354e550794ebde42264792987a323') { throw 'Chart digest mismatch' }
helm upgrade --install fullnet-log-monitor artifacts/log-consumer-kubernetes/kube-prometheus-stack-89.2.0.tgz --namespace fullnet-local-log-monitoring --create-namespace --kube-context kind-fullnet-local -f deploy/observability/log-consumer-operator-local-values.yaml --wait --timeout 5m
pnpm test:log-consumer:operator:live
```

The runner requires the same consumer image and Docker Desktop prerequisites as `test:log-consumer:kubernetes:live`. It creates an actual chart PodMonitor, checks both selector exclusions, then verifies `podMonitor/<test namespace>/log-consumer/0`, `job="fullnet-log-consumer"`, Pod IP port 8080 and consumer metrics through the Operator-managed Prometheus. Only the Prometheus API is forwarded for this discovery check. Its separate static Prometheus still tests the original TargetDown rule through the test bridge; that alert scenario is not Operator target discovery. Results are written to `artifacts/log-consumer-operator/result.json` only after task cleanup.

The optional local restart overlay `log-consumer-restart-local-values.yaml` adds kube-state-metrics from the same pinned chart. It grants only Pod list/watch, limits exported metrics to `kube_pod_labels` and `kube_pod_container_status_restarts_total`, and allows only `app.kubernetes.io/name` as an application label. `honorLabels: true` preserves the workload namespace carried by those metrics. The ServiceMonitor is selected only in the dedicated monitoring namespace. These limits follow the [official kube-state-metrics CLI options](https://github.com/kubernetes/kube-state-metrics/blob/main/docs/developer/cli-arguments.md); merge the allowlist with existing platform requirements when applying equivalent production settings.

After downloading and checking the archive as above:

```powershell
helm upgrade fullnet-log-monitor artifacts/log-consumer-kubernetes/kube-prometheus-stack-89.2.0.tgz --namespace fullnet-local-log-monitoring --kube-context kind-fullnet-local -f deploy/observability/log-consumer-operator-local-values.yaml -f deploy/observability/log-consumer-restart-local-values.yaml --wait --timeout 5m
pnpm test:log-consumer:restart-alert:live
```

This separate runner uses a real consumer with deliberately missing startup configuration, no Secret or downstream client. It creates a random namespace and loads the unchanged official restart expression as a PrometheusRule. It checks actual Kubernetes restart count, actual Prometheus alert firing, exclusion while the same restarting Pod has an unrelated application label, firing after label restoration, and resolution after the test Pod is deleted. This last step proves removal of the monitored test workload, not recovery of a healthy consumer. Task namespace, rule, Pod and API forwarding are cleaned before publishing `artifacts/log-consumer-restart-alert/result.json`; the dedicated monitoring release remains available. No external notification is sent.

`pnpm test:log-consumer:notifications:live` adds a temporary, in-cluster Alertmanager and webhook receiver to that same real restart experiment. It requires the local restart monitoring setup and no existing alerting endpoints on the dedicated Prometheus. Prepare the fixed test images:

```powershell
docker pull node@sha256:ebfe2f90462722a7a4de65e91990e97fe0d401c70e0e762c5b53302f905ec1c1
docker pull quay.io/prometheus/alertmanager@sha256:690c7b525f4367aa91f73e2f91c632206d32e97c6384bdbf2fb7a861b420340d
docker tag quay.io/prometheus/alertmanager@sha256:690c7b525f4367aa91f73e2f91c632206d32e97c6384bdbf2fb7a861b420340d quay.io/prometheus/alertmanager:v0.34.0
pnpm test:log-consumer:notifications:live
```

The runner exports only linux/amd64 image content for the local kind nodes, avoiding imports of uncached platforms in Docker Desktop multi-architecture indexes. Alertmanager's default route discards unrelated events; only the exact test namespace/Pod/rule is routed to the local receiver with `send_resolved: true`, following the [official webhook configuration](https://prometheus.io/docs/alerting/latest/configuration/#webhook_config). The receiver checks identity, status, version and fingerprint, caps bodies at 65536 bytes and retained attempts/events at 100, and stores no original annotations or extra labels. Its fault budget defaults to zero and accepts only integers from 0 to 8. All services are ClusterIP; only the proof API is forwarded to loopback, with no public Ingress or external recipient.

The four phases are consumed in order: first firing, label exclusion resolved, label restoration firing, final Pod removal resolved. They must share a fingerprint, preventing a prior resolved event from proving a later phase. Prometheus `spec.alerting` is temporarily set to the task Alertmanager and restored to its exact prior field value using resource-version and ownership comparisons. Unexpected concurrent changes fail cleanup rather than being overwritten. Result publication requires task cleanup and this CRD configuration restoration; runtime configuration propagation still follows the Operator's normal reconciliation. The reusable monitoring tools and Docker image cache remain. Results are in `artifacts/log-consumer-notifications/result.json`; concrete external receiver credentials, notification restart durability, HA, long outages and delivery to people are outside this local acceptance.

Run `pnpm test:log-consumer:notification-retry:live` with the same image preparation to explicitly inject two HTTP 503 responses. Rejected attempts do not count as accepted events, and invalid requests do not consume the fault budget. The real Alertmanager must produce the first three valid attempts as `503 → 503 → 200` for the same firing identity and fingerprint, then complete the four phases above. The test uses a one-hour repeat interval to distinguish these retries from regular repeats. Alertmanager v0.34.0 treats HTTP 5xx as recoverable, as implemented in its [webhook notifier](https://raw.githubusercontent.com/prometheus/alertmanager/v0.34.0/notify/webhook/webhook.go). The independent report is `artifacts/log-consumer-notification-retry/result.json`; the [local verification record](../../docs/verification/2026-10-01-log-consumer-notification-retry.md) covers this short failure window only.

Run `pnpm test:log-consumer:notification-restart:live` separately to replace the isolated Alertmanager Pod after the first firing delivery. The new Pod must have a different UID, be Ready, and deliver the same active alert fingerprint again before the label exclusion/restoration and final removal phases. Results are in `artifacts/log-consumer-notification-restart/result.json`, published only after configuration restoration and cleanup. Replacement discards emptyDir storage; this tests receiving Prometheus resends after replacement, not durable notification recovery or HA. The receiver remains running and retains the evidence across Alertmanager replacement.

Run `pnpm test:log-consumer:notification-outage:live` separately to return HTTP 503 for 60000 milliseconds from the first valid notification. Timing uses the receiver's monotonic clock; image setup and invalid requests cannot consume the outage window. The duration defaults to zero, is bounded to 300000 milliseconds, and cannot be combined with the attempt-count fault budget. The runner verifies multiple same-fingerprint rejections before the deadline and an actual HTTP 200 delivery at or after the deadline, then completes all four notification phases. Attempts remain capped at 100, including elapsed milliseconds only in this mode. Results are published after cleanup and restoration to `artifacts/log-consumer-notification-outage/result.json`. This tests a bounded 60-second HTTP failure, not arbitrary-duration outages, durable notification recovery or HA.

Enable the separate `fullnet-log-consumer` chart's `podMonitor.enabled` only after installing Prometheus Operator CRDs. Set `podMonitor.labels` to the actual Prometheus `podMonitorSelector`, allow the release namespace in `podMonitorNamespaceSelector`, and set `monitoringNamespace` to the namespace hosting the scraper. The PodMonitor uses `/metrics`, 15s sampling and `jobLabel: app.kubernetes.io/name`, yielding `job="fullnet-log-consumer"`. The chart does not install the monitoring stack or notification routing.

`FullNetLogConsumerRestarting` requires scraped `kube_pod_container_status_restarts_total` and `kube_pod_labels` with the application name. Configure the platform's kube-state-metrics container with `--metric-labels-allowlist=pods=[app.kubernetes.io/name]` (merge this key into any existing allowlist), and retain these metric families if using a metric allowlist. Do not use a wildcard label allowlist. This follows the [official kube-state-metrics CLI](https://github.com/kubernetes/kube-state-metrics/blob/main/docs/developer/cli-arguments.md). Verify the actual exported label `label_app_kubernetes_io_name="fullnet-log-consumer"`; absence of that label disables the scoped restart rule even when promtool tests pass.

The consumer reports only its assigned partitions' sampled committed-offset lag. A missing, negative, over-budget or older-than-30s sample reports `lag_known=0`, `lag_records=-1`; do not graph unknown values as zero. Per-process ES/DLQ/Offset result counters may disappear before a scrape on fast failure, so target-down and scoped Kubernetes restart alerts complement them. Restart metric export and Alertmanager notifications still require platform wiring; rule evaluation alone does not prove notifications were delivered.

The application permits `Collector` after local acceptance. Before applying these platform values, configure actual destinations and the CA Secret, and run the corresponding local behavior/recovery tests. The example does not install or certify Kafka/Elasticsearch downstream services.

```bash
helm upgrade --install fullnet-fluent-bit <fluent-bit-chart> -f deploy/observability/fluent-bit-values.yaml
helm upgrade --install fullnet-otel <otel-collector-chart> -f deploy/observability/otel-collector-values.yaml
kubectl apply -f deploy/observability/prometheus-rules.yaml
# Import grafana-dashboard.json into Grafana
```

## Verification

```powershell
pnpm test:observability-deploy
pnpm test:observability-alerts
pnpm test:observability-forward-tls:live
pnpm test:observability-enospc:live
pnpm test:observability-queue-overflow:live
pnpm test:observability-logstash-pq:live
pnpm test:observability-vector-boundary:live
```

Capacity remains `Capacity-not-verified` until the required Task 14 measurements actually pass; local execution is sufficient under the current acceptance standard.

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
