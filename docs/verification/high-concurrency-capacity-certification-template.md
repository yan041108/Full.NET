# High-Concurrency Capacity Certification Template

- Status default: `Capacity-not-verified`
- Incomplete rule: missing any checklist item ⇒ result = `Incomplete`
- Order gate: 2K → 5K → 10K → Soak
- Providers: certify SQL Server and MySQL separately; never cross-claim
- Acceptance environment: local processes, isolated containers or kind Kubernetes are sufficient under the 2026-09-30 project decision. Record shared resources and limit conclusions to the tested hardware and scope.

## Run identity

| Field | Value |
|---|---|
| Git SHA | |
| Image digest (api/worker/migrator) | |
| Helm release / values URI | |
| Hardware (CPU/RAM/disk/network) | |
| Database provider + parameters URI | |
| Redis cache / realtime parameters URI | |
| Data scale | |
| Load model (`closed_loop` / `open_loop`) | |
| Profile (`2k` / `5k` / `10k` / `soak`) | |
| Raw results URI | |

## Required evidence checklist

- [ ] application_metrics
- [ ] load_generator_metrics
- [ ] pod_metrics
- [ ] node_metrics
- [ ] database_metrics
- [ ] redis_cache_metrics
- [ ] redis_realtime_metrics
- [ ] s3_metrics
- [ ] collector_metrics
- [ ] actual_active_requests (must not equate k6 VUs)
- [ ] arrival_rate_dropped_iterations
- [ ] threadpool_queue_thread_count
- [ ] allocation_rate
- [ ] gc_pause_gen2
- [ ] socket_httpclient
- [ ] db_connection_pool_wait
- [ ] log_audit_worker_backlog

## Scenario coverage

- [ ] hot_cache / cold_cache / read_mostly
- [ ] mixed_write / hot_key / missing_key
- [ ] batch_invalidation / redis_cold_start
- [ ] http_operation_log_profiles / b1_audit_microbatch
- [ ] upload / signalr
- [ ] outbox_jobs_backlog / database_primary_failover

## Stop conditions observed

Record any early stop for error rate, P99, recovery time, DB connection/lock/IO, cache stale window, Audit/Outbox reliability, tenant isolation, scheduler arrival rate, or load-generator resource budget.

## Result

| Provider | Profile | Model | Result (`Incomplete` / `PassCandidate` / `Fail`) | Notes |
|---|---|---|---|---|
| SqlServer | 2k | closed_loop | | |
| SqlServer | 2k | open_loop | | |
| MySql | 2k | closed_loop | | |
| MySql | 2k | open_loop | | |

Only after the required 10K + Soak scenarios actually pass for both providers may a separate task update the corresponding capacity status. Local evidence is sufficient; an unexecuted profile or a smaller smoke does not establish 10K capacity.
