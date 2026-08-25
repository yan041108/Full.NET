# Database capacity protection verification

## Scope

- Baseline: `166d0911cd09cb5307f1f751a76f35406fa09035`
- Branch: `codex/db-capacity-protection`
- Environment: Windows, .NET SDK `10.0.400`, Node `v24.12.0`, pnpm `10.26.0`, Docker Desktop `29.6.2`
- Providers: Microsoft SQL Server and MySQL reusable Testcontainers
- Capacity status: `Capacity-not-verified`

This slice adds static connection-budget validation, bounded normal/critical database admission, connection wait/hold telemetry, API 503 overload mapping, Worker acquisition backoff, Helm wiring and Prometheus alerts. It does not claim a throughput or latency improvement and does not implement adaptive limits. The later P2/P3 follow-up changes command lifetime and adds allow-listed Native AOT command reuse; see `2026-08-26-db-command-lifetime-and-aot-reuse.md`.

## Results

### Unit and contract tests

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~DatabaseCapacityOptionsValidatorTests|FullyQualifiedName~DatabaseAdmissionGateTests|FullyQualifiedName~DatabaseAdmissionPriorityScopeTests|FullyQualifiedName~DatabaseConnectionTelemetryTests|FullyQualifiedName~DbSessionCapacityTests|FullyQualifiedName~StandardApiResultMapperTests|FullyQualifiedName~ErrorResourceCompletenessTests|FullyQualifiedName~OutboxProcessorTests"
```

Result: 64 passed, 0 failed, 0 skipped.

```powershell
node --test tests/deployment/observability-contract.test.mjs tests/deployment/helm-contract.test.mjs
```

Result: 14 passed, 0 failed.

### Tiny-pool dual-provider concurrency

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter FullyQualifiedName~DatabaseCapacityConcurrencyTests
```

Result: 2 passed, 0 failed. Each Provider used `MaxPoolSize=2`, normal admission `1`, queue `1`, and health reserve `1`. The scenario held one real connection with a slow query, queued and canceled one acquisition, rejected the overflow request, then verified recovery with a new query.

Unit coverage additionally verifies zero-queue rejection, admission timeout, already-canceled tokens, open failure, idempotent release, transaction-dispose failure, wait measurement including Provider open time, and a Worker critical permit remaining available while normal admission is full.

### AOT and architecture

```powershell
pnpm test:aot:analyzers
```

Result: build succeeded with 0 warnings and 0 errors.

```powershell
pnpm test:dotnet:architecture --selection api-native-aot
```

Result: 48 passed, 0 failed; build succeeded with 0 warnings and 0 errors.

### Affected integration and Release build

```powershell
pnpm test:integration:affected:plan -- --base 166d0911cd09cb5307f1f751a76f35406fa09035 --phase inner
pnpm test:inner -- --base 166d0911cd09cb5307f1f751a76f35406fa09035
```

The final plan selected `Data, Outbox, smoke`, including the new capacity concurrency fixture. Result: 38 passed, 0 failed, 0 skipped in 12m 59s; the inner selector confirmed MySQL coverage. SQL Server coverage is provided by the explicit dual-provider tiny-pool command above.

```powershell
dotnet build Full.NET.slnx -c Release
```

Result: succeeded with 0 warnings and 0 errors.

### Governance and naming

```powershell
pnpm test:governance
```

Result: 52 passed, 0 failed.

```powershell
pnpm test:naming
```

Result: 29 passed, 1 failed. The failure reports four pre-existing `FNSQL003 unsupported_ddl` findings in unchanged SQL Server/MySQL `100_MessagingDomainAuditRequestedOutcome.sql` migrations. Those files are absent from this task's diff and were introduced by commit `f3ea5f51c76275968f0525b4b5c57c0a865eed6b`; this task does not modify or suppress that unrelated naming debt.

`git diff --check` reported no whitespace errors. Git only emitted the repository's Windows LF-to-CRLF working-copy notices.

## Operational boundary

- Helm production releases enable the protection for API and Worker. The connection Secret must explicitly match the role's `apiMaxPoolSize` or `workerMaxPoolSize`; an omitted Provider keyword defaults to 100 and therefore fails startup when the declared value differs.
- Metrics use only `provider`, `host_role`, and acquisition `outcome`; raw SQL, connection strings, pool names, tenants and exception messages are excluded.
- API overload returns HTTP 503 with `common.database_capacity_exhausted` and `Retry-After`. Worker backs off new acquisition; Outbox renewal and terminal writes use the critical reserve.
- No production-equivalent soak, 2K/5K/10K run, adaptive high-water validation, or before/after performance benchmark was executed. Fixed QPS and latency improvements are unverified.
