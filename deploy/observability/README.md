# Full.NET Observability Deploy Baseline

Platform-owned Fluent Bit / OpenTelemetry Collector / Prometheus rules / Grafana dashboard overlays for the modular monolith. The application Helm chart does **not** install these backends. From 2026-09-30, project acceptance is based on actual local test results under [development quality §11](../../rules/development-quality.md#11-测试与验证); CI and a dedicated production-equivalent environment are optional. The application Chart permits production `logging.ingress=Collector` after the local Host/Helm and fixed-image routing/ACK/TLS/fault baseline. These values tail matching container files once and select the `fullnet.io/log-ingress=collector` Pod label. Platform destinations still require provisioning and their own local confirmation/recovery tests. Historical notes below retain their original evidence limits; they no longer impose an execution-location gate.

## Contracts

- Application emits Compact JSON on stdout only.
- The Collector candidate has filesystem buffering, memory limits, one Tail checkpoint, retry limits, and TLS settings. These settings do not prove corrupt-chunk isolation or durable delivery.
- B2 Best Effort and Priority have separate output budgets. Fixed-version local samples cover routing and classification; node load and downstream failure evidence remain open. B2 S3 archive is only a proposed output.
- B0/B1 Durable Audit remains in the Audit/module database path; Fluent Bit must not duplicate it as another “Durable log” pipeline.
- OTel Collector pins Contrib 0.163.1 and its image digest, including the [WAL replay fix](https://github.com/open-telemetry/opentelemetry-collector-contrib/pull/44467). Trace `otlp_grpc/tempo` uses `sending_queue.storage: file_storage`; `prometheus_remote_write` uses nested `http`, its own `remote_write_queue` and `wal` (see the [component configuration](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.163.0/exporter/prometheusremotewriteexporter/config.go)). Metrics use one consumer and explicitly one request at a time. `UnderscoreEscapingWithSuffixes` preserves alert metric names. Both paths share the mounted `emptyDir`: container restart can retain it, Pod replacement or node loss cannot. WAL is not an end-to-end delivery guarantee. The chart mounts the storage directory and sets Pod `fsGroup: 10001` so the non-root Collector can open the Trace queue and Metrics WAL. The standalone config check also starts the pinned image with equivalent group permissions. A configuration update alone does not confirm end-to-end acceptance; see the active logging remediation plan.
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

### Ubuntu 对象存储容量告警

`fullnet-object-capacity.timer` 每五分钟从本机 SeaweedFS Master 读取卷槽，并检查对象存储目录所在文件系统的空闲空间。服务把结果原子写入 `/var/lib/fullnet-object-capacity/fullnet_object_storage_capacity.prom`。`growth_state` 的值 `0` 为余量充足，`1` 为低于一批增长所需的 7 个卷槽，`2` 为无空闲槽或磁盘空闲不高于 4 GiB。巡检失败时发布 `probe_success=0` 和新的采样时间，容量值随该次替换移除。巡检只读查看增长条件，实际写入资格仍由发布验证确认。

首次安装对象存储时安装脚本会安装新版巡检。已有主机可单独安装新版脚本及 unit，然后让巡检立即产生一份样本。

```bash
sudo install -m 0755 eng/deploy/ubuntu/Verify-ObjectStorageCapacity.py /usr/local/lib/fullnet/Verify-ObjectStorageCapacity.py
sudo install -m 0644 eng/deploy/ubuntu/fullnet-object-capacity.{service,timer} /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now fullnet-object-capacity.timer
sudo systemctl start fullnet-object-capacity.service
sudo journalctl -u fullnet-object-capacity.service -n 10 --no-pager
```

巡检在容量风险或阻断时返回 `1`，观测或指标写入失败时返回 `2`。容量风险和阻断仍有可采集样本。由平台在对象存储主机的 Node Exporter 中配置 `--collector.textfile.directory=/var/lib/fullnet-object-capacity`。如果采集器运行在容器中，将该目录只读挂载到其文本采集目录。使用内网受控的既有采集地址，为对象存储节点创建独立的 Prometheus job 并加载本目录的规则。

```yaml
scrape_configs:
  - job_name: fullnet-object-capacity
    scheme: https
    static_configs:
      - targets: [storage-node.internal:9100]
    metric_relabel_configs:
      - source_labels: [__name__]
        regex: fullnet_object_storage_capacity_.+
        action: keep
```

发布验证应确认该 job 中的 `up`、`probe_success`、`growth_state` 和采样时间。告警会区分容量低、增长阻断和观测不可用。采样超过十二分钟时，规则停止沿用其增长状态。观测告警要求目标已登记，目标从采集配置中删除后需要通过平台的目标清单核对。由现有告警路由接收并复验发送与恢复。`pnpm test:observability-alerts` 覆盖规则的保持、触发和恢复，`python3 -B tests/deployment/test_object_storage_capacity.py` 检查巡检输出和原子替换。 [Node Exporter 文本采集说明](https://github.com/prometheus/node_exporter#textfile-collector)

### API and Worker deployment availability

`FullNetDeploymentNoAvailableReplicas` fires when a Full.NET API or Worker Deployment expects replicas and its available count stays at zero for two minutes. The alert identifies the cluster, namespace and Deployment. It uses Kubernetes workload status through kube-state-metrics and can fire while application metrics are unavailable. Samples must be newer than 120 seconds. Target discovery and exporter outage monitoring cover missing samples, while restored availability or intentional scale-to-zero clears the condition.

Collect Deployments and preserve `kube_deployment_spec_replicas`, `kube_deployment_status_replicas_available` and `kube_deployment_labels` when a metric allowlist is used. Merge `--metric-labels-allowlist=deployments=[app.kubernetes.io/part-of,app.kubernetes.io/component]` into the existing label allowlist. Keep the workload `namespace` label when scraping. Add a consistent `cluster` label to the three series when aggregating multiple clusters. [Deployment metrics](https://github.com/kubernetes/kube-state-metrics/blob/main/docs/metrics/workload/deployment-metrics.md) · [Label allowlist](https://github.com/kubernetes/kube-state-metrics/blob/main/docs/developer/cli-arguments.md)

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
helm upgrade --install fullnet-otel opentelemetry-collector \
  --repo https://open-telemetry.github.io/opentelemetry-helm-charts --version 0.178.2 \
  --namespace observability --create-namespace -f deploy/observability/otel-collector-values.yaml
kubectl apply -f deploy/observability/prometheus-rules.yaml
# Import grafana-dashboard.json into Grafana
```

## Verification

```powershell
pnpm test:observability-deploy
node eng/testing/otel-collector-config-test.mjs
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
| `fullnet.messaging.kafka.observed_topics.lag` | `fullnet_messaging_kafka_observed_topics_lag` | Messages behind high watermark for observed topics only; excludes unobserved subscriptions and retry topics |
| `fullnet.messaging.kafka.observed_topics.lag_retention_ratio` | `fullnet_messaging_kafka_observed_topics_lag_retention_ratio` | Time-based ratio for observed topics with configured identity; unobserved topics are not covered |
| `fullnet.messaging.connector.lag` | `fullnet_messaging_connector_lag_seconds` | SQL Server source commit-time gap from the actual Connector LSN to captured high watermark; MySQL remains unknown |
| `fullnet.messaging.connector.offset.unrecoverable` | `fullnet_messaging_connector_offset_unrecoverable` | 0/1 gauge |
| `fullnet.messaging.cdc.sqlserver.capture_job_running` | `fullnet_messaging_cdc_sqlserver_capture_job_running` | Worker reads actual Agent and current database capture-job activity; no assumed default |
| `fullnet.messaging.cdc.mysql.binlog_retention_hours` | `fullnet_messaging_cdc_mysql_binlog_retention_hours` | Worker reads actual automatic expiration; 0 disables automatic expiration, alert at 0 &lt; value &lt; 24h |
| `fullnet.messaging.observation.status` | `fullnet_messaging_observation_status` | Explicit healthy/unhealthy/unknown/error/disabled/stale samples |
| `fullnet.messaging.connector.running` | `fullnet_messaging_connector_running` | Actual Connect connector and every task must be RUNNING |
| `fullnet.messaging.producer.fenced` | `fullnet_messaging_producer_fenced` | Actual persisted database fence, without stream or operation IDs |
| `fullnet.messaging.kafka.observed_topic.lag` | `fullnet_messaging_kafka_observed_topic_lag` | Bounded Worker sampling per mapped formal topic; not time lag or retention proof |

Label allow-list: `provider`, `database_provider`, `topic_code`, `consumer_code`, `message_type_code`, `result`, `reason_code`, `connector_code`, `component`, `resource_code`. The new resource codes are fixed (`cdc`, `pilot`, `database`, `sqlserver`, `mysql`); no endpoint or connector name is used as a new label. Forbidden: Secret, Payload, SQL, Tenant, User, MessageId, exception text.

CDC collection defaults to disabled; configure `Messaging:CdcMonitoring` with permissions and control mapping as described in [deployment](../messaging/README.md#cdc-runtime-monitoring). Failed or rejected routes immediately remove previous connector and consumer lag values; an in-flight refresh retains a still-fresh sample until completion or a known failure. Source coordinates are checked against the actual SQL Server CDC/MySQL Binlog window. SQL Server lag uses same-database commit-time differences. MySQL optionally uses completed GTIDs and bounded, retained Performance Schema transaction timers; Kafka optionally uses an explicitly confirmed LogAppendTime Topic ID and the bounded time index. Their prerequisites, conservative clock budget and unknown cases are documented in deployment; REST status and message counts do not supply time evidence. Encrypted MySQL positions and unsupported policies remain unknown. Platform target-down and OTLP exporter health must also be monitored. The historical 2026-08-20 record does not establish live firing; the 2026-10-28 live loop covers capture-job, Binlog configuration, stale samples, exporter recovery and separate source-window alerts. Later time/topology tests and their exact scope are in the [current record](../../docs/verification/2026-10-28-messaging-reliability-continuation.md).

Cutover/rollback steps: [`docs/runbooks/cdc-kafka-cutover-rollback.md`](../../docs/runbooks/cdc-kafka-cutover-rollback.md). Verification: [`docs/verification/messaging-cdc-observability-20260820.md`](../../docs/verification/messaging-cdc-observability-20260820.md).

## 平台指标后端独立渲染包（2026-11-21）

`deploy/helm/fullnet-observability` 是独立平台清单渲染器，输出一个 Kubernetes List；经审查后使用 kubectl apply，不能作为应用三角色 Chart 安装，也不声称 Helm Release 安装/升级管理已验收。无需 Prometheus Operator CRD。复用本目录现行 prometheus-rules.yaml 与 otel-collector-values.yaml，使用 --set-file 注入，不保留第二份手写规则或 Collector 主配置。

```bash
bash eng/deploy/Render-Observability.sh \
  --set cluster=reference-primary \
  --set prometheusClaim=platform-prometheus-data \
  --set alertmanagerClaim=platform-alertmanager-data > /PRIVATE_REVIEW_DIRECTORY/metrics.json
```

默认输出包括独立 observability 命名空间、四个固定摘要镜像的单副本 Deployment/ClusterIP Service、配置、只读工作负载状态观测角色及入口 NetworkPolicy。Prometheus 与 Alertmanager 必须引用两个不同的既有 PVC，本包不创建卷，不选择 StorageClass。Prometheus 数据保留上限为七天/10 GB，运维应为其单独准备至少 20 GiB 的存储并核对权限/备份；两者 Recreate，不能作为高可用或无中断方案。Collector WAL 复用原有 emptyDir 边界，Pod 替换/节点丢失可能丢失在途指标。本次未认证 PVC、CNI、存储故障或容量。

kube-state-metrics 只读取 applicationNamespace（默认 fullnet-production）的 Deployment、HPA 和 PDB，保留 Full.NET 角色/归属标签。Role 仅允许 get/list/watch apps/deployments、autoscaling/horizontalpodautoscalers、policy/poddisruptionbudgets；无 Secret、写入或集群级权限。可选节点/DaemonSet 观察器各用独立只读账号，应用账号不共享它们的权限；其余后端禁用服务账号 Token。全部容器非 root、只读根文件系统、去除 capabilities，具有资源预算及启动/就绪探针。服务仅 ClusterIP；NetworkPolicy 允许平台内部访问和应用命名空间的 OTLP 4317/4318，不代替实际 CNI 验证或其他适用策略的许可并集核对。不要向已有平台命名空间盲目应用命名空间标签/同名资源。

该派生 Collector **仅接收指标**，从现有配置复用单消费者、单请求并发、WAL、指标名翻译和点标签过滤；该平台派生配置将 WAL 刷新/截断周期明确设为五秒，防止低流量等待原一分钟周期而延迟可见性，并补充资源属性过滤，移除 tenant_id/user_id/DiagnosticGroup，避免 target_info 再携带这些属性。同时在资源与指标点上以 upsert 写入配置中的 cluster，覆盖客户端同名值，使应用与平台抓取指标具有同一归属。这不能代替应用自己的低基数与敏感信息策略，也不代表处理任意未知敏感字段。内部 metrics 8888 显式绑定所有容器接口，平台 Prometheus 抓取该端点与 Deployment 观测器。

应用端使用更新后的 Host 时，无全局 OTEL_EXPORTER_OTLP_ENDPOINT 也能按显式 METRICS/TRACES 端点独立导出，仅配置指标时不启用追踪或日志出口。每种信号的 PROTOCOL、HEADERS、TIMEOUT（正整数毫秒）、COMPRESSION（none/gzip）优先于通用 OTLP 配置；未设置协议沿用 SDK 的 gRPC 默认值，HTTP 必须显式使用 http/protobuf。信号 ENDPOINT 必须是完整 http/https URL，HTTP 路径不自动追加；配置错误在注册时失败且只报告键名。标准指标配置如下，设置后须重启应用；这些变量应由应用 Chart 的显式环境配置注入，不由本平台渲染包改写。

```bash
OTEL_EXPORTER_OTLP_METRICS_ENDPOINT=http://telemetry-collector.observability.svc:4318/v1/metrics
OTEL_EXPORTER_OTLP_METRICS_PROTOCOL=http/protobuf
```

API、Worker 使用现有应用 Chart 的 runtime.configMapName 引用发布命名空间内的配置对象，由其 data 提供上述键；需要鉴权的 OTLP HEADERS 通过 runtime.secretName 引用的 Secret 注入，禁止放进 values、普通 ConfigMap 或日志。更新环境配置后需显式滚动应用；运行中的进程不会自动重读这些导出选项。启用前同时检查现有通用键和同名 Secret 键，避免意外覆盖或继续启用旧全局出口。

已有全局出口时仍使用 SDK 全信号注册及其标准覆盖规则，不重复添加分信号导出器；该模式与分信号注册不可混用。禁止把指标专用后端设置为全局目的地。只有追踪后端时可单独配置 OTEL_EXPORTER_OTLP_TRACES_ENDPOINT 及协议。应用入口的现有 Egress 策略、TLS/CA 与实际可见性须在现场启用前核对；本轮不改现网配置，也不由回环 HTTP 接收测试推导生产采集完成。配置语义依据 [OpenTelemetry .NET 1.17 OTLP 配置](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.17.0/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md)。

通知默认 discard，不外发。只有显式 receiver.enabled=true 才渲染 webhook，要求 HTTPS、无 URL 凭据/query/fragment，使用 receiver.caSecretName 的 ca.pem；可选 receiver.authorizationSecretName 的 token 仅以 Bearer credentials_file 引用。清单不包含 Secret 数据，也不创建 Secret。默认 receiver.probeOnly=true 只转发合成探针，其他规则继续 discard；正式启用获准路由时另行审查 receiver.probeOnly=false、CA/凭据投影与真实触发/恢复送达。证书轮换后重新验证或滚动，不假定 TLS 客户端热加载。

```bash
# 仅对获准的测试接收器；生成配置不会发送消息。
bash eng/deploy/Render-Observability.sh --set cluster=reference-primary \
  --set prometheusClaim=platform-prometheus-data,alertmanagerClaim=platform-alertmanager-data \
  --set probeAlert=true,receiver.enabled=true,receiver.caSecretName=approved-ca \
  --set receiver.authorizationSecretName=approved-token \
  --set-string receiver.url=https://APPROVED_RECEIVER/alerts
```

probeAlert 默认关闭，显式启用才增加 FullNetTelemetryPipelineProbe（fullnet_pipeline_probe_state == 1，持续十秒）；生产既有规则及阈值保留。定向入口 helm-runtime-focused / metrics-platform 覆盖渲染边界和固定镜像真实配置校验、OTLP→查询→firing/resolved→自有 TLS 接收器，以及错误 CA 拒绝，合成身份值在点标签与 target_info 中均应移除。所有容器/网络/CA/Token 由本轮持有并回收，不发送真实通知；合成回路不是现网应用采集、KSM 实际 watch、正式地址送达或生产认证。

配置语义依据 [Collector 内部指标](https://opentelemetry.io/docs/collector/internal-telemetry/) 和 [kube-state-metrics 2.17 CLI](https://github.com/kubernetes/kube-state-metrics/blob/v2.17.0/docs/developer/cli-arguments.md)。Prometheus/Alertmanager/Collector 复用仓库既有固定摘要；KSM 固定官方 registry 的 2.17.0 摘要。平台尚须按现场逐项核对采集新鲜度、实例标签、持久卷、真实告警路由、接收恢复及故障域，D08-14 不因本包渲染而整体关闭。
本渲染包保留全部原有规则，但提供 Deployment/HPA/PDB 与 Collector 的抓取、外部 OTLP 指标入口及 externalScrapes 显式外部源接线。Node Exporter、Ingress/WAF 等宿主或外部 exporter 不由本包自动安装；节点条件与 DaemonSet 可用性有下文的显式只读采集选项；不能用规则文件存在推导全部规则都有数据或能够报警。五秒 WAL 参数只用于本派生平台，不改现有通用 Collector values，不据功能窗口推导吞吐、分位延迟或容量。WAL 低流量批次行为参见[对应 exporter 实现](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.163.0/exporter/prometheusremotewriteexporter/wal.go)。

### 隔离 Kubernetes 部署验证

在 Linux、仅连接本机 Unix Socket 的 Docker、kind 0.30.0、kubectl 和 Helm 就绪后，可执行定向入口：

```bash
FULLNET_METRICS_KUBERNETES=isolated FULLNET_TEST_REAL_HELM=helm PYTHONDONTWRITEBYTECODE=1 \
  PYTHONPATH=tests/deployment python3 -O -m unittest -v \
  metrics_platform_kubernetes_test.MetricsPlatformKubernetesTests.test_original_manifests_watch_deployments_and_preserve_both_pvc_states_after_pod_replacement
```

该入口创建唯一 Kind 集群和独立 kubeconfig，应用本渲染包的原始清单。两个 1 GiB 动态 PVC 只是本轮夹具，不改变生产存储建议；测试 Deployment 使用合成身份，应用命名空间只发 OTLP，平台命名空间执行内部查询。验证四个组件就绪、非 root/只读根、服务账号令牌分配、Deployment watch、副本变化、Secret/其他命名空间读取拒绝和 OTLP 时间戳；更换 Prometheus、Alertmanager Pod 后分别读取原样本与原测试 silence，并核对 PVC UID 保持。测试 silence 无真实通知地址，默认 discard 保持。

Kind 默认 CNI 不执行 NetworkPolicy，此入口不认证网络隔离；同节点动态卷恢复也不认证生产卷、节点丢失或 HA。HTTP OTLP 源为合成夹具，不替代实际 Host 到本平台的现场链路。入口结束回收本轮集群和临时文件，失败或跳过不得报告为通过；对应手动 workflow 场景为 metrics-kubernetes，不触发其他平台或 .NET 套件。

本清单的首次实际 Kubernetes 部署及上述 watch/PVC 场景已在[定向作业](https://github.com/yan041108/Full.NET/actions/runs/44847175955/job/136669650127)通过；该结果只覆盖原始组件与隔离集群，不代表现网已安装。

### HPA 与 PDB 状态来源

平台 KSM 复用同一服务账号和应用命名空间边界，额外采集 autoscaling/v2 HPA 和 policy/v1 PDB；不创建 HPA/PDB，也不取得修改或驱逐权限。HPA 上限规则消费 kube_horizontalpodautoscaler_status_current_replicas 与 kube_horizontalpodautoscaler_spec_max_replicas，原保持期为十五分钟；PDB 规则消费 kube_poddisruptionbudget_status_pod_disruptions_allowed，原保持期为十分钟。HPA 名称需匹配 .*fullnet-(api|worker).*，PDB 名称需匹配 .*fullnet.* 才被现有对应规则选中。

定向 workflow 场景 workload-metrics-watch 使用真实 Kubernetes API/KSM/Prometheus 验证 HPA 上限变化、当前副本系列存在、PDB 控制器计算的可中断数变化及删除后的陈旧系列移除，并核对命名空间只读权限。该入口不安装 Metrics Server、不模拟 HPA 自动伸缩或执行驱逐，不缩短原告警窗口，也不宣称正式告警已送达。指标名称和标签依据 [KSM 2.17 HPA](https://github.com/kubernetes/kube-state-metrics/blob/v2.17.0/docs/metrics/workload/horizontalpodautoscaler-metrics.md) 与 [PDB](https://github.com/kubernetes/kube-state-metrics/blob/v2.17.0/docs/metrics/policy/poddisruptionbudget-metrics.md)。

该 HPA/PDB 采集与权限范围已在[真实 Kubernetes 定向作业](https://github.com/yan041108/Full.NET/actions/runs/44848124013/job/136672778561)通过；实际伸缩、驱逐、原告警持续窗口及正式通知仍须按各自范围验证。

### 显式外部指标源

externalScrapes 默认 []，不会访问外部 exporter。需要采集已有对象容量 textfile、Fluent Bit 或边缘指标时，使用下方示例值文件，替换为已批准目标与本平台命名空间中的既有 Secret，然后只渲染并审查：

```bash
bash eng/deploy/Render-Observability.sh --set cluster=reference-primary \
  --set prometheusClaim=platform-prometheus-data,alertmanagerClaim=platform-alertmanager-data \
  --values deploy/observability/examples/external-scrapes.yaml
```

允许的 job 为 fullnet-object-capacity、fluent-bit、fullnet-edge、waf、ddos、distributed-rate-limit，每种只允许配置一次，每种最多八个去重目标。targets 使用小写 DNS 名或 IPv4 加端口（1—65535），不含 scheme、路径、凭据或查询；IPv6 不在本切片范围。metricsPath 仅允许 /metrics 或 Fluent Bit 的 /api/v1/metrics/prometheus，默认 /metrics。scheme 默认 https，始终验证证书；私有 CA 引用 caSecretName 的 ca.pem，Bearer 认证引用 authorizationSecretName 的 token。CA/Token 以只读 Secret 文件投影到 Prometheus，其他平台组件不挂载、不输出 Secret 内容或创建 Secret。HTTP 只用于明确的内部无凭据 exporter，必须显式 scheme: http，禁止同时配置 CA/凭据。

抓取周期十五秒、超时五秒、响应体上限 5 MB、每轮两万样本、每样本最多三十二个标签，标签名/值分别限制 128/1024 字节；超预算按失败处理，观察 up 和抓取错误。固定 job/instance/cluster 标签，不信任 exporter 覆盖目标标签，也不采纳其显式样本时间戳；对象容量的 sample_timestamp_seconds 指标值仍用于原十二分钟新鲜度规则。禁止 HTTP 重定向；文件凭据不以头部明文写入配置。外部 exporter 自身仍需控制敏感字段与标签基数。

本入口不安装 exporter、开启宿主端口或放行出站网络；目标、DNS、端口、Prometheus 出口许可与 exporter 访问策略必须在启用前核对。Secret 更新后显式滚动/验证 Prometheus，不能假定 CA/客户端状态已经热更新。对象容量 job 应指向已安装的 Node Exporter textfile 端点并确认定时样本确实更新；新增目标和规则存在都不能代替实际数据、持续窗口和正式通知送达证据。配置语义依据 [Prometheus 3.15 配置](https://github.com/prometheus/prometheus/blob/v3.15.0/docs/configuration/configuration.md)。定向 workflow 场景为 external-metrics-scrapes，实际证书与 Token 测试只使用本轮自有 HTTPS 夹具。

此配置入口已通过本地真实 Helm 定向检查及[真实 Kubernetes HTTPS/Secret 投影作业](https://github.com/yan041108/Full.NET/actions/runs/44849608109/job/136677573725)；错误 CA、错误 Token、重定向均被拒绝。该证明只覆盖自有合成目标，不表示真实 exporter、现场采集或通知已启用。

#### Node Exporter textfile 与双向 TLS

使用 [mTLS 抓取示例](examples/node-exporter-mtls.yaml) 配置既有目标。clientCertificateSecretName 引用平台命名空间中的既有 Secret，必须包含 tls.crt 与 tls.key；渲染器仅将这两个文件以 0440、只读方式投影到 Prometheus，不生成凭据。客户端证书可与 CA/Bearer 引用组合使用，但禁止配合 HTTP，也不接受内嵌私钥或关闭服务端证书验证。默认 externalScrapes 仍为空。

[Exporter Web 配置](examples/node-exporter-web.json) 是 exporter-toolkit 可读取的 JSON 格式 YAML，要求受信客户端证书并精确匹配 prometheus.observability.svc 的 SAN。部署前替换为实际批准的客户端身份与证书路径：服务端叶子证书需要 serverAuth 和目标 DNS/IP SAN，Prometheus 客户端叶子证书需要 clientAuth 和允许的 SAN；两端分别信任相应签发 CA，不向容器或仓库分发 CA 私钥。配置与叶子私钥应只允许运行账号读取，按现有凭据管理流程提供，不执行示例自动签发或安装。

已安装的 Node Exporter 可按下列参数接入现有定时巡检目录；INTERNAL_IP 必须是已批准的内网绑定地址，网络只允许 Prometheus 来源。容器部署时将该 textfile 目录及必要凭据目录只读挂载；本场景仅开启 textfile，无需宿主根目录、特权模式或其他默认采集器。

```bash
node_exporter --web.listen-address="${INTERNAL_IP}:9100" \
  --web.config.file=/etc/node-exporter/web.json \
  --collector.disable-defaults --collector.textfile \
  --collector.textfile.directory=/var/lib/fullnet-object-capacity
```

手动定向入口 node-exporter-mtls 使用固定 Node Exporter 1.13.1 镜像、正式容量发布函数和本轮临时证书，验证正常→阻断→观测不可用的样本变化与客户端拒绝。容量评估输入为合成数据，不调用真实磁盘/SeaweedFS 巡检，不代表现场安装、Kubernetes 客户端证书实际投影、持续容量告警或正式通知通过。协议依据 [exporter-toolkit 0.20.0 Web 配置](https://github.com/prometheus/exporter-toolkit/blob/v0.20.0/docs/web-configuration.md)；正式 textfile 目录仍由既有巡检服务更新。

[真实 Node Exporter mTLS 作业](https://github.com/yan041108/Full.NET/actions/runs/44862338467/job/136718286681) 已通过：可信客户端读取正式函数原子替换的文件，容量正常→阻断→观测不可用，旧增长状态退出、新鲜度记录消失时目标仍 up=1。无证书、非受信 CA 和错误 SAN 均 down 且无容量样本；同时核对 exporter 的 TLS 握手拒绝日志，避免将任意网络失败当作认证拒绝。测试只在自有临时网络运行，不改变原巡检、新鲜度或告警窗口。

#### Ubuntu 首次安装入口

[Install-ObjectCapacityExporter.py](../../eng/deploy/ubuntu/Install-ObjectCapacityExporter.py) 面向 Ubuntu 24.04/Linux amd64，只安装对象容量 textfile 专用服务。Node Exporter 1.13.1 离线归档须从[官方发行页](https://github.com/prometheus/node_exporter/releases/tag/v1.13.1)取得，脚本内固定官方 SHA-256，不提供跳过校验或自定义摘要。仅读取归档中三个精确普通文件，保留 LICENSE/NOTICE，不自动下载或执行归档里的安装脚本。

先以 root 运行默认预检；预检只写入自动清理的私有 PKI 临时目录，不写持久配置、服务、二进制或改变服务状态：

```bash
sudo python3 eng/deploy/ubuntu/Install-ObjectCapacityExporter.py \
  --archive /PRIVATE_DIRECTORY/node_exporter-1.13.1.linux-amd64.tar.gz \
  --bind-address 192.168.31.230 --client-san prometheus.observability.svc \
  --server-certificate /PRIVATE_DIRECTORY/exporter.crt \
  --server-key /PRIVATE_DIRECTORY/exporter.key \
  --server-ca /PRIVATE_DIRECTORY/server-ca.pem \
  --client-ca /PRIVATE_DIRECTORY/prometheus-client-ca.pem
```

该绑定地址必须实际属于安装主机，且为明确的 RFC 1918 IPv4；叶子证书须覆盖该 IP、支持 serverAuth、有效且与无口令私钥匹配。client-ca 必须是单张有效 CA 证书，client-san 是获准 Prometheus 客户端证书的精确 DNS SAN，不接受通配符。叶子私钥须由 root 持有且无组/其他用户访问权限，所有输入为有界、无链接且不可组/全局写的普通文件。脚本不持有客户端私钥，不签发证书、不修改巡检服务。

预检前需已有 root 所有且不可组/全局写的 /etc/fullnet、/usr/local/lib/fullnet、/etc/systemd/system，以及 root 所有、其他用户可读/遍历的 /var/lib/fullnet-object-capacity 目录。现有容量巡检每五分钟发布该目录文件；没有巡检服务或目录时先按对象存储部署流程准备，安装器不会伪造健康样本。审核预检后在同一命令追加 --apply 才执行首次安装，地址须属于本机、9100 端口可绑定，安装需 root 与 systemd。已有服务、同名供应商 unit、目标目录/文件/链接均拒绝，不用该入口升级现存安装。

安装输出在 /usr/local/lib/fullnet/object-capacity-exporter、/etc/fullnet/object-capacity-exporter 与 fullnet-object-capacity-exporter.service。配置目录 0700、凭据文件 0600，动态账号通过 LoadCredential 读取私有运行时副本，密钥不进入 unit/environment；只读系统、移除 capabilities，限制 CPU/内存/进程，只开启 textfile。依据 [systemd 255 凭据语义](https://github.com/systemd/systemd/blob/v255/man/systemd.exec.xml)，证书轮换须更新受控源文件并重启本服务，不能依赖运行时凭据副本自行更新。不会改防火墙、Kubernetes 配置或 Prometheus；仅对获准来源按现有网络策略放行内网 9100，再配置前述 mTLS 抓取和 CA/客户端 Secret。

安装/启用失败或受控中断时只回收本轮创建的目录/unit；一旦尝试启动，先 disable --now 并复核停止再删除。停止无法确认或资源身份改变则返回 rollback_incomplete_preserved，保留现场供人工核对，不报告安装成功。并发使用独占安装锁，锁文件作为持续协调入口保留。installed 仅表示 unit 校验、启用及当时 ActiveState 核对通过，scrapeVerified/notificationVerified 始终 false；仍需从实际 Prometheus 验证 mTLS、up、textfile 错误、容量新鲜度、告警触发及恢复送达。断电/强制 SIGKILL、升级和无中断证书轮换不由首次安装事务保证。

[官方归档与实际单元校验](https://github.com/yan041108/Full.NET/actions/runs/44863386267/job/136721568526) 已通过，使用真实 Node Exporter 1.13.1 归档和 Ubuntu systemd 255.4。该作业的服务控制器使用受控执行器，真实 systemctl 只读核对未安装状态，不写宿主服务；这份证据本身覆盖包、证书准入、文件安装和 unit 语法。后续真实 systemd/凭据运行证据见下文，发布环境安装仍需单独执行和验收。

#### 只读容量采集验收

[Verify-ObjectCapacityScrape.py](../../eng/deploy/ubuntu/Verify-ObjectCapacityScrape.py) 在 Ubuntu/Linux 上只读核对一个精确目标，固定 job=fullnet-object-capacity，要求显式 cluster 与 instance。先将既有 Prometheus 转发到本机回环接口，再在另一终端执行：

```bash
kubectl -n observability port-forward service/prometheus 19090:9090 --address=127.0.0.1
python3 eng/deploy/ubuntu/Verify-ObjectCapacityScrape.py \
  --prometheus-url http://127.0.0.1:19090 \
  --cluster reference-primary --instance 192.168.31.230:9100
```

支持经批准的 HTTPS API 地址及路径前缀，使用系统 CA 或 --ca-file；Bearer Token 只通过 --token-file 从当前账号持有的 0600 普通文件读取，不接受链接、内嵌 URL 凭据或 HTTP 携带 Token。明文仅允许回环地址，不使用环境代理、不跟随重定向、不提供跳过证书校验。单次响应最多 64 KiB/十六个序列、服务端查询三秒、套接字五秒，CLI 总墙钟上限八秒，缓慢逐字节响应不能无限延长执行。

在同一次显式时间查询中读取当前值与 timestamp(up)，避免把查询结果的评估时间误认成实际抓取时间。目标 label、唯一指标、有限数值和完整样本须符合契约；重复/额外标签、警告/部分响应、缺失、非有限或负容量值均拒绝。抓取超过六十秒视为过期；容量使用原十二分钟新鲜度及最多六十秒超前容差，不修改现行告警窗口。容量判断仍由正式巡检发布，验收器不复制磁盘/卷槽阈值算法。

| 输出 state | 退出码 | 运维含义 |
| --- | --- | --- |
| growth_headroom_available | 0 | 当前目标抓取与巡检样本新鲜，容量状态正常。 |
| growth_at_risk / growth_blocked | 1 | 采集成功，正式巡检报告容量风险或增长阻断。 |
| target_missing / target_down / scrape_stale | 2 | 精确目标不存在、抓取失败或实际抓取时间过期。 |
| textfile_unavailable / probe_unavailable / capacity_sample_stale | 2 | textfile 读取失败、巡检失败或容量样本过期。 |
| verification_failed | 2 | API/证书/输入/样本契约异常；code 为固定机器码，不输出凭据或原始响应。 |

异常状态不输出旧 freeSlots/freeBytes；失败巡检仍携带容量读数时拒绝该不一致快照。JSON 始终输出到标准输出，供现有发布检查读取；工具不写配置或服务、不自动重试/修复、不发送通知。一次成功仅证明所选目标在查询时刻可用，不能代替告警持续窗口、正式通知、实际磁盘/SeaweedFS 巡检、跨节点故障或容量认证。查询语义依据 [Prometheus 3.15 HTTP API](https://github.com/prometheus/prometheus/blob/v3.15.0/docs/querying/api.md) 和 [timestamp 函数](https://github.com/prometheus/prometheus/blob/v3.15.0/docs/querying/functions.md#timestamp)。

[实际 systemd 与验收工具链路](https://github.com/yan041108/Full.NET/actions/runs/44864480831/job/136724968240) 已通过：一次性 Ubuntu/systemd 255 环境由正式安装函数生成并启用临时 unit，Node Exporter 以动态非 root UID、零有效 capabilities 运行，使用 LoadCredential 中的证书完成 mTLS；真实 Prometheus 抓取正式函数发布的 textfile。验收 CLI 实际观察正常→阻断→巡检失败，退出码分别为 0/1/2，失败结果无旧容量读数。服务、unit 和容器已回收；容量评估输入为合成数据，unit 位于本轮 /run/systemd/system，其他文件在专有临时目录，不代表发布节点安装、实际磁盘/SeaweedFS、正式通知或容量通过。

### 可选节点条件与日志 DaemonSet 状态

nodeState.enabled 默认为 false，daemonSetNamespace 默认为空。只有显式开启才渲染额外的 kube-state-nodes 或 kube-state-daemonsets Deployment/ClusterIP 及抓取任务，复用原固定 KSM 镜像和资源预算；不创建宿主 Agent 或日志 DaemonSet。下面的例子只渲染，日志命名空间必须已存在：

```bash
bash eng/deploy/Render-Observability.sh --set cluster=reference-primary \
  --set prometheusClaim=platform-prometheus-data,alertmanagerClaim=platform-alertmanager-data \
  --values deploy/observability/examples/infrastructure-state.yaml
```

节点观察使用独立 fullnet-metrics-node-reader，只授予 get/list/watch nodes。ClusterRole/Binding 名为 <平台命名空间>-fullnet-metrics-nodes，不授予 nodes/proxy、Secret、Pod 日志或写入权限；仅输出 kube_node_status_condition。日志状态观察使用另一个 fullnet-metrics-daemonset-reader，只读取 daemonSetNamespace 内的 DaemonSet，并仅输出 kube_daemonset_status_number_unavailable；不读取日志、Pod 或其他命名空间。原应用 Deployment/HPA/PDB 账号的权限保持。两个选项可独立使用，默认不会创建 ClusterRole。

FullNetNodeDiskPressure 观察 DiskPressure=true；Fluent Bit 可用性规则选择 daemonset=fullnet-fluent-bit，两个原保持期均为一分钟。新增指标源不会代替实际探针/告警持续窗口、正式通知、物理磁盘故障或 Node Exporter 的文件系统容量。实现依据 [KSM 2.17 Node](https://github.com/kubernetes/kube-state-metrics/blob/v2.17.0/docs/metrics/cluster/node-metrics.md) 与 [DaemonSet](https://github.com/kubernetes/kube-state-metrics/blob/v2.17.0/docs/metrics/workload/daemonset-metrics.md)。

本工具是清单渲染器，不管理资源删除：已启用后仅改回 false/空再 apply 不会自动删除旧观察器或撤销已创建权限。停用时先核对对象归属并撤销对应 ClusterRoleBinding/RoleBinding，再回收观察 Deployment、Service、ServiceAccount 及专属 Role/ClusterRole；不要删除原应用观察器的授权。定向运行场景为 infrastructure-state-metrics，只在自有集群验证；节点条件使用不可调度的合成 Node，真实节点不改。

默认关闭、显式账号范围与原阈值已通过真实 Helm 定向检查；[实际 Kubernetes 状态和权限作业](https://github.com/yan041108/Full.NET/actions/runs/44850873746/job/136681668955)通过合成节点条件与测试 DaemonSet 的变化/恢复。此证据不代表物理磁盘压力、真实日志 Agent 或现网告警已经验证。

### 真实 Host 到平台指标的定向链路

手动工作流 dotnet-integration-focused / host-platform-metrics 只运行现有 HostLoggingMetricsPipelineTests 的链路方法。用例从当前本地 Helm 清单读取实际 Collector/Prometheus 配置与规则，Host 只设置指标端点与协议，Collector 使用 UID 10001 和有界临时 WAL；不需要数据库或业务账号。保留当前十五秒规则评估、一分钟优先日志丢弃 for 及五分钟 increase/恢复窗口，因此真实窗口需要数分钟，不能用快速合成 probe 替代。

本用例移除未启动服务的抓取/通知，只验证真实 Full.NET 默认 Host 注册、双日志通道指标、共享实例标识、Prometheus 原告警 firing/恢复及 Remote Write 无乱序拒绝。运行环境需 .NET 10、真实 Helm 3.16.4、本地 Docker 及两个实际固定镜像；定向入口会按当前清单准备镜像。它不代表完整 Api/Worker 业务启动、Kubernetes 放置/存储/CNI、正式通知或容量已认证。

[仅指标 Host 的实际链路作业](https://github.com/yan041108/Full.NET/actions/runs/44858176487/job/136705165930)已通过，同一 Host 实例的双通道指标、优先丢弃告警触发及原五分钟窗口后的恢复均实际验证。该范围仍是通用注册与隔离两后端，不替代完整业务宿主、现场网络或正式通知送达。

### OTLP 平台归属标签

平台 cluster 不仅用于 Prometheus 抓取目标，也在派生 Collector 的资源和指标点处理中统一 upsert。缺失值补齐，客户端同名值（包括不同类型）被平台配置覆盖；target_info 同样使用该归属。job/instance 及其他合法维度保留，tenant_id/user_id/DiagnosticGroup 继续从资源和点删除。cluster 是平台保留标签，业务指标不得用它表达另一种分组；它只是配置归属，不证明客户端已经通过认证，实际入口访问控制仍须成立。

只在此平台派生配置中写入，不改通用 Collector values；不增加按请求产生的动态维度或额外处理阶段。改变 cluster 会改变时间序列标识，已有历史数据不会被重写。语义依据 [Attributes upsert](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.163.0/processor/attributesprocessor/README.md) 和 [Resource processor](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.163.0/processor/resourceprocessor/README.md)。定向运行入口 metrics-cluster-context 使用实际固定镜像和原五秒 WAL，核对缺失/伪造归属、Gauge/Counter 值及 target_info，不重复通知或业务宿主验收。

[固定镜像真实归属数据流](https://github.com/yan041108/Full.NET/actions/runs/44860483409/job/136712475935)已通过缺失/伪造 cluster、Gauge/Counter/target_info 一致性和身份过滤验证；只证明本平台归属处理，不证明客户端真实性、现场网络或正式通知。
