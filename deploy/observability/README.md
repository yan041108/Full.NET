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
