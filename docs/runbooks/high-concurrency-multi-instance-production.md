# Runbook: High-concurrency multi-instance production

## SLO and ownership

- Monthly availability SLO: **99.9%**.
- Primary recovery owner: Platform On-call (API/Worker/Ingress).
- Secondary: Database On-call, Cache/Realtime Redis On-call, Observability On-call.
- Capacity status until dedicated certification: `Capacity-not-verified`.

## Topology reminders

- Three Helm releases: `fullnet-migrator` → `fullnet-worker` → `fullnet-api` (`eng/deploy/Invoke-FullNetRelease.ps1`).
- Cache Redis and Realtime Redis are separate fault domains.
- Chart does not install DB/Redis/S3/Loki/Tempo/WAF.

## Expand/Contract and rollback

1. Prefer Expand (additive schema) then contract in a later release.
2. If Migrator fails: stop; do not roll API/Worker forward.
3. If Worker fails after Migrator: keep API off new traffic; fix consumers; do not silently rewrite contracted columns.
4. RPO/RTO targets follow ADR-0005 / Spec §21.3; verify with restore drills before claiming compliance.

## Collector interruption

1. Confirm Fluent Bit / OTel `file_storage` queues are draining, not recursing.
2. Application continues Compact JSON stdout; do not enable a second Durable Audit log sink.
3. Page Observability On-call if Priority drops or Spool disk nears full.

## Edge / WAF

1. Global rate/connection budgets are owned by Edge; ingress-nginx local limits are not global.
2. If WAF/DDoS/external limiter is down, follow declared `fail-closed`/`fail-open` policy and page Edge On-call.

## Database connection capacity

1. The API connection Secret must set `Max Pool Size` to `databaseConnectionBudget.apiMaxPoolSize`; the Worker Secret must set it to `databaseConnectionBudget.workerMaxPoolSize`. Startup fails when the parsed Provider value differs, including when an omitted keyword silently falls back to 100.
2. Keep `PermitLimit + HealthReserve + CriticalWorkerReserve <= MaxPoolSize`. Health checks bypass normal admission; Outbox lease renewal and terminal writes use the Worker critical reserve.
3. Page Database On-call when `FullNetDatabaseConnectionAcquireTimeout` fires. For `FullNetDatabaseConnectionAdmissionRejected`, first compare `fullnet_db_connection_wait_seconds`, connection hold time, Provider, host role, HPA saturation and database CPU/locks before increasing a pool or permit limit.
4. API rejection is HTTP 503 with code `common.database_capacity_exhausted`; Worker stops new acquisition and backs off. Do not convert this signal into unbounded retries.
5. Static limits are the production baseline. Do not enable adaptive high-water control until saturation, rejection and recovery evidence supports explicit hysteresis and cooldown values.
6. Non-transaction commands release their logical connection lease and admission permit when the command/reader completes, even if the request scope remains alive. Explicit transactions retain one connection until commit, rollback or scope disposal; a long connection hold without a transaction therefore indicates an unfinished reader, provider call or disposal path rather than normal scope lifetime.
7. Native AOT command reuse is allow-listed at startup by stable statement name and fixed scalar parameter order. Unregistered statements and collection-expanded parameters deliberately allocate and dispose commands normally; do not add runtime SQL/shape discovery or an unbounded command-plan cache during an incident.

## Related runbooks

- `docs/runbooks/data-protection-key-recovery.md`
- `docs/runbooks/cache-redis-recovery.md`
- `docs/runbooks/audit-log-backpressure.md`
