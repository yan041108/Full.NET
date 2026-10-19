# Runbook: Cache Redis and Realtime Redis recovery

## Separation

- API/Worker require a dedicated `Cache:RedisConnectionString` in Production/Staging. Realtime uses `Realtime:RedisBackplaneConnectionString`. Cache and Realtime endpoint sets must not overlap; different Redis databases, passwords or option ordering do not provide physical isolation.
- The one-shot Migrator uses `AddFullNetMigratorCaching` and does not connect to Redis. Keep migration readiness independent of cache readiness.
- Failures must not be mitigated by pointing both roles at the surviving instance without an approved exception.

Startup compares effective endpoints: omitted ports mean 6379 for ordinary Redis, 6380 for TLS, or 26379 for Sentinel (Sentinel takes precedence over TLS). Endpoint-set overlap is rejected in Production/Staging even if a development sharing flag is enabled. This follows the pinned [StackExchange.Redis 2.10.1 connection rules](https://github.com/StackExchange/StackExchange.Redis/blob/2.10.1/src/StackExchange.Redis/EndPointCollection.cs).

Before rollout, record the infrastructure resource IDs and failure domains behind both role-specific Secret references, and compare the actual Redis server/cluster identities. Different DNS aliases, proxies, ports or TLS settings can still route to the same instance; string comparison alone cannot establish physical isolation. Keep credentials out of the evidence and diagnostic logs. The current code/Native verification scope is tracked in the [cache hardening plan](../superpowers/plans/2026-10-03-cache-hardening.md).

## Cache Redis outage

1. Confirm `/health/ready` hysteresis (Degraded then Unhealthy) and that Realtime still delivers on its own Redis.
2. Tenant admission is C0: new requests read tenant status/domain binding from the authoritative database. A database failure must not fall back to an old active tenant cache. In-flight operations already admitted are not retroactively terminated by this check.
3. Failover/rebuild Cache Redis. [Redis Pub/Sub](https://redis.io/docs/latest/develop/pubsub/#delivery-semantics) does not replay notifications missed during a disconnect; the SDK's local recovery queue does not add subscriber history.
4. Confirm a fresh subscription and publication, then perform one authorized business update in the affected scope and verify its new version from a different API node. A successful GET, reconnect or publish alone does not prove remote L1 invalidation. Existing stale display entries still rely on their TTL and owner contract; tenant admission must continue authoritative reads.
5. Watch invalidation publisher latency and fault alerts after restore. SDK stale-hit events cannot measure business staleness while an entry's TTL is still valid. Use bounded rollout and observe database load: default same-key factory coalescing is local to a FusionCache instance, so several cold nodes may each query the source. The project has not enabled a distributed refill lock; see the [pinned lock implementation](https://github.com/ZiggyCreatures/FusionCache/blob/v2.9.0/src/ZiggyCreatures.FusionCache/FusionCache.cs).

`distributed-cache` also observes the existing SDK Backplane. A successful L2 GET does not clear a known publication/subscription failure. Unknown evidence (including after reconnect) remains Degraded/HTTP 200; an observed failure becomes Unhealthy/HTTP 503 on the second consecutive check. A subscription acknowledgement or valid incoming message restores subscription evidence; an actual successful publication restores publication evidence. Require both and a successful L2 GET for Healthy. Circuit timer closure and reconnect alone are insufficient. This passive observation adds no connections or probe messages and cannot immediately detect an idle ACL change or silent disconnect; a cold or quiet instance can stay Degraded until actual publication provides evidence.

## Realtime Redis outage

1. SignalR cross-node delivery stops; do not claim messages delivered.
2. Offline business facts remain in DB/Outbox — not in Realtime Redis.
3. Restore Realtime Redis; verify two API nodes cross-publish after reconnect (`AbortOnConnectFail=false`).
4. Keep session affinity unless WebSocketsOnly + SkipNegotiation is intentionally enabled.

## Switching instances

1. Update only the dedicated Secret for the affected role.
2. Roll Worker then API (or both if both Secrets change).
3. Never reuse one connection string for both roles in Production to “recover faster”.

## Cache namespace

L2 always uses `fullnet:{environment}:{application}:v2:` and the Backplane channel prefix always uses `fullnet:{environment}:{application}:v2`. All replicas of one application must use the same stable `Cache:ApplicationName`; independent applications must use different names. A node ID must never be used as the application name. `UseIsolatedNamespace` has been removed under the development-foundation direct-evolution rule.

Changing the namespace does not delete old Redis data. Unreferenced keys expire naturally; this change does not authorize clearing a database or historical evidence. Tenant admission reads the authority and has no cache-only Outbox consumers or legacy-key cleanup chain.

## Cleanup and snapshot budgets

- `Cache:InvalidationTimeout` defaults to 5 seconds for independent post-commit cleanup. Request cancellation does not undo the committed write. A local cleanup failure still allows the distributed attempt; a deadline is not proof that every remote node invalidated its L1.
- `ICacheBatchInvalidator` uses that same total budget, including validation and concurrency waiting. Defaults are `InvalidationMaxBatchItems=1000`, `InvalidationMaxBatchBytes=262144`, `InvalidationMaxConcurrency=4` and `InvalidationMaxConcurrentBatches=32`. Supported maxima are 10000 inputs, 8 MiB, 32 SDK operations and 256 admitted batches. Capacity overflow fails immediately without queueing another batch. Duplicate inputs still consume the input/byte budget; only identical registered entry, complete target, key/tag kind and propagation scope deduplicate. No new Redis or cross-node acknowledgement protocol is introduced.
- Invalid targets reject the whole batch before deletion. Individual SDK failures do not stop other targets within the remaining budget; after all started operations finish, the earliest failed input's original exception is rethrown. Callers must handle a partially completed batch as idempotent cleanup. Tenancy retains the local phase before the shared phase and passes its independent post-commit cancellation budget.
- `Cache:RedisOperationTimeout` defaults to 1 second; native Redis connect/command timeouts and Fusion distributed hard timeout are configured. AutoRecovery has `AutoRecoveryMaxItems=1000` and `AutoRecoveryMaxRetryCount=3`; it remains best effort, not durable delivery. In FusionCache 2.9.0, the capacity check, eviction and insertion are separate operations: concurrent submissions can exceed the configured item threshold. This setting does not establish a hard item or memory limit; strict resource capacity remains unverified.
- The project does not override FusionCache 2.9.0's L2/Backplane circuit-breaker durations: both default to zero, meaning disabled. The SDK recovery delay remains 5 seconds; the 2-second delay used by a fault fixture is not a production setting. Keep these facts separate from configured Redis command deadlines and health hysteresis. Full ranges and pinned SDK sources are in the [resource-option table](../operations/cache-reliability-telemetry.md).
- Diagnostic policy reads use an in-process authority snapshot. `Cache:DiagnosticSnapshotMaxAge` defaults to 1 minute (supported range 30 seconds–5 minutes); source failure, age expiry or clock rollback removes temporary diagnostic widening. Redis key deletion alone does not refresh another node's snapshot.

Diagnostic policy authority polling runs independently on each API node every 30 seconds, using the `Cache:InvalidationTimeout` cancellation budget for reload work. A management operation only refreshes the receiving node through the diagnostic policy store and accepts `current_node_only`. Its `snapshot:settings.diagnostic-policy:current-node` result does not acknowledge remote snapshot updates. Other nodes converge through their own polls, and expired rules cannot continue widening sampling while awaiting another poll. The old Redis document is no longer backfilled by the runtime snapshot store.

## Display policies and Provider tokens

The administration catalog exposes `runtimeStatus=observing` only when this node has subscribed to SDK events, `unknown` when it has not, and `not_applicable` for C0/N0 cache access. This does not indicate whether an individual entry has been used, whether Redis is healthy, or whether another node received an invalidation. The Vue page renders these distinctions and discards results after reading permission or the selected catalog changes.

`Cache:Entries:*:MaxSerializedBytes` accepts 1 byte through 64 MiB. The Grid owner executes the per-entry business JSON budget on fill and cached read; C0/N0 bypass cache access. This is separate from `Cache:MaxDeserializationBytes`, which limits incoming SDK JSON before parsing. Do not infer a per-entry budget from its CLR type or equate either setting with a process memory bound. Cached `RequiresVersionRecheck` remains rejected until its owner supplies a real protocol.

Grid preferences now default to 1 minute in L1 and 15 minutes in L2. Missed notifications and late factory completion still allow stale display data; the convergence budget includes source-read duration, L2 lifetime, subsequent L1 promotion and jitter. These defaults are not a measured propagation SLA.

For canonical `fullnet:{environment}:{scope}:{module}:{resource}:...` keys, the SDK options provider resolves `{module}.{resource}` and carries policy jitter and Fail-Safe into the Hybrid adapter. S2 Fail-Safe requires an explicit `FailSafeMaxDuration` covering normal TTL plus jitter, up to one day. Negative caching with Fail-Safe is rejected because this adapter cannot distinguish that lifetime when selecting per-key defaults. C0/N0 callers must continue to bypass reads and writes.

In FusionCache 2.9.0, `JitterMaxDuration` changes L1 expiration; it does not extend physical L2 expiration. S0-L2 therefore cannot use this setting to spread shared-layer expiry. Include layer behavior when calculating recovery windows, and verify Fail-Safe retention with jitter and repeated failures before treating it as an absolute business staleness limit. See the [pinned expiration implementation](https://github.com/ZiggyCreatures/FusionCache/blob/v2.9.0/src/ZiggyCreatures.FusionCache/FusionCacheEntryOptions.cs).

WeCom, DingTalk and WeChat Mini Program access tokens are an explicit process-local exception. They never enter Redis or the cache administration API. A SHA-256 fingerprint of application identity and credentials separates rotations; the cache holds at most 1024 identities per provider, merges refreshes for the same credentials, renews 60 seconds early and owns a 30-second refresh cancellation budget. Cancelling one waiter does not cancel shared refresh work. Host disposal cancels and drains owned refresh tasks. Do not log credentials, fingerprints or token values.

## Observed events and alarms

The added `fullnet.cache.runtime.events` counter has only stable `layer`, `operation` and `result` labels. It covers L1/L2 hits and misses, foreground/background factory outcomes, Fail-Safe activation and L2 circuit transitions. It never labels raw keys, tenants, domains or exception text. Existing stale-hit and Backplane counters retain their names. SDK stale hits describe expiration, not a comparison with the business version.

Batch metrics are `fullnet.cache.invalidation.batch.duration` (ms), `.targets` (`kind=unique|duplicate`) and `.rejections` (`reason=capacity|invalid_input|timeout|canceled`). Target counts describe the prevalidated batch, not successfully applied deletions; individual success/failure remains in existing invalidation metrics. None of these counters confirms remote-node invalidation. SDK event totals can include internal coordination entries; do not treat them as a business-only hit ratio. SDK AutoRecovery remains best effort; do not invent queue-depth or replay guarantees where the SDK offers no supported observation API.

The [Prometheus rules](../../deploy/observability/prometheus-rules.yaml) include invalidation failure, recent Backplane/L2 circuit-open, factory failure and batch-budget rejection alerts. The existing invalidation P99 alert keeps its published identifier but measures publisher latency, not stale data age. Circuit alerts require a circuit breaker to be enabled; recent transitions can still alert after recovery. Confirm actual readiness and subscription/publication evidence. S2 Fail-Safe activation alone is permitted degradation, not a security-staleness alert. Recorded rule and fixture validation is linked from the [acceptance record](../verification/2026-10-14-cache-concentrated-acceptance.md); neither it nor the configured thresholds establishes measured production budgets.

## L1 payload capacity

`Cache:L1PayloadCapacity` defaults to 10000 weight units (allowed 1–1000000). Governed payloads cost one unit. A dedicated memory store keeps this budget separate from other host `IMemoryCache` users; a missing explicit size is assigned one at insertion. Capacity rejection does not discard a successfully computed response. Watch `fullnet.cache.runtime.events` with `layer=l1`, `operation=eviction`, `result=capacity` alongside source reads and latency.

This is not a byte or process-memory limit. FusionCache tag invalidation metadata retains the SDK's zero weight and protected priority, so capacity eviction cannot erase invalidation markers. Do not introduce unbounded per-request tag names. Measure payload/key diversity, tag count, managed heap, process memory and source load in a bounded working-set experiment before raising the budget. Current capacity status remains `Capacity-not-verified`.
