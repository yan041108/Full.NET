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

## Related runbooks

- `docs/runbooks/data-protection-key-recovery.md`
- `docs/runbooks/cache-redis-recovery.md`
- `docs/runbooks/audit-log-backpressure.md`
