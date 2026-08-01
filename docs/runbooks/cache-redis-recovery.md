# Runbook: Cache Redis and Realtime Redis recovery

## Separation

- `Cache:RedisConnectionString` and `Realtime:RedisBackplaneConnectionString` must differ in Production.
- Failures must not be mitigated by pointing both roles at the surviving instance without an approved exception.

## Cache Redis outage

1. Confirm `/health/ready` hysteresis (Degraded then Unhealthy) and that Realtime still delivers on its own Redis.
2. Fail closed for C0/S0 security-sensitive cache classes; rely on authority source.
3. Failover/rebuild Cache Redis; Backplane is notification only, not a transaction log.
4. Watch invalidation P99 / stale window alerts after restore.

## Realtime Redis outage

1. SignalR cross-node delivery stops; do not claim messages delivered.
2. Offline business facts remain in DB/Outbox — not in Realtime Redis.
3. Restore Realtime Redis; verify two API nodes cross-publish after reconnect (`AbortOnConnectFail=false`).
4. Keep session affinity unless WebSocketsOnly + SkipNegotiation is intentionally enabled.

## Switching instances

1. Update only the dedicated Secret for the affected role.
2. Roll Worker then API (or both if both Secrets change).
3. Never reuse one connection string for both roles in Production to “recover faster”.
