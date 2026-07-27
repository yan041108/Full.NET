# Outbox Version Retirement Scan Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Worker 增加不改变消息状态的一次性旧版本退役扫描，只有目标 Handler 的 canonical/legacy 路由在同一 `SchemaVersion` 下既无 pending 也无 dead-letter 消息时才返回安全退出码。

**Architecture:** Worker 在构建 Host 前剥离专用 CLI 参数；普通启动仍注册 `OutboxProcessor`，扫描模式不注册后台处理器，只启动必要的配置/数据库门禁并执行一次只读查询。扫描器从当前 Handler 解析 canonical type 与全部 legacy aliases，Dapper 通过现有 `IOutboxBacklogReader` 在 SQL Server/MySQL 上聚合未处理消息，避免新增未消费抽象或修改数据库结构。

**Tech Stack:** .NET 10、C#、Microsoft.Extensions.Hosting、Dapper、SQL Server、MySQL、MSTest、Testcontainers。

## Global Constraints

- 不修改数据库迁移、Outbox 消息写入/领取/重放语义、公共 HTTP API、客户端或 Docker 编排。
- 只读扫描必须使用 `SqlDataScope.HostOnly`，不得输出 Payload、TenantId、消息 Id、异常文本或连接字符串。
- 目标 Handler 必须在退役前仍处于注册状态；扫描 canonical type 时必须自动覆盖该 Handler 的全部 `LegacyEventTypes`。
- `ProcessedAtUtc IS NULL` 的 pending 与 dead-letter 都阻断退役；已处理消息与其他 type/version 不阻断。
- SQL Server 与 MySQL 必须执行同语义的双库集成验证。
- 手写注释与 XML 文档注释使用中文；稳定机器码使用 `outbox.version_retirement.*`。
- 不修改当前并行任务锁定的 Hosting RateLimit、Seeding、DatabaseOptions `ServiceCollectionExtensions`、Caching 或 Integration shard 文件。

---

### Task 1: Parse and isolate the Worker retirement command

**Files:**
- Create: `src/Hosts/Full.NET.Host.Worker/OutboxVersionRetirementCommandLine.cs`
- Create: `tests/Full.NET.UnitTests/Outbox/OutboxVersionRetirementCommandLineTests.cs`

**Interfaces:**
- Consumes: Worker 原始 `string[] args`。
- Produces: `OutboxWorkerCommandLineOptions Parse(IReadOnlyList<string> arguments)`；其中 `VersionRetirement` 为可空 `OutboxVersionRetirementRequest`，`HostArguments` 为剥离专用参数后的 Host 参数。

- [x] **Step 1: Write the failing command-line tests**

```csharp
[TestMethod]
public void Valid_retirement_arguments_are_parsed_and_removed_from_host_arguments()
{
    var options = OutboxVersionRetirementCommandLine.Parse(
        [
            "--environment", "Production",
            "--outbox-version-retirement-message-type", "fullnet.tenancy.tenant.provisioned",
            "--outbox-version-retirement-schema-version", "1"
        ]);

    Assert.AreEqual(
        new OutboxVersionRetirementRequest(
            "fullnet.tenancy.tenant.provisioned",
            1),
        options.VersionRetirement);
    CollectionAssert.AreEqual(
        new[] { "--environment", "Production" },
        options.HostArguments.ToArray());
}

[TestMethod]
public void Incomplete_or_invalid_retirement_arguments_are_rejected()
{
    string[][] invalidArguments =
    [
        ["--outbox-version-retirement-message-type", "fullnet.test"],
        ["--outbox-version-retirement-schema-version", "1"],
        ["--outbox-version-retirement-message-type", "", "--outbox-version-retirement-schema-version", "1"],
        ["--outbox-version-retirement-message-type", "fullnet.test", "--outbox-version-retirement-schema-version", "0"],
        ["--outbox-version-retirement-message-type", "fullnet.test", "--outbox-version-retirement-schema-version", "abc"]
    ];

    foreach (var arguments in invalidArguments)
    {
        var exception = Assert.ThrowsExactly<OutboxVersionRetirementException>(
            () => OutboxVersionRetirementCommandLine.Parse(arguments));
        Assert.AreEqual(
            OutboxVersionRetirementErrorCodes.CommandInvalid,
            exception.Code);
    }
}
```

- [x] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --filter "FullyQualifiedName~OutboxVersionRetirementCommandLineTests" --minimum-expected-tests 2
```

Expected: build fails because the command-line types do not exist.

- [x] **Step 3: Implement the minimal parser**

```csharp
internal sealed record OutboxVersionRetirementRequest(
    string MessageType,
    int SchemaVersion);

internal sealed record OutboxWorkerCommandLineOptions(
    OutboxVersionRetirementRequest? VersionRetirement,
    IReadOnlyList<string> HostArguments);

internal static class OutboxVersionRetirementErrorCodes
{
    public const string CommandInvalid = "outbox.version_retirement.command_invalid";
    public const string HandlerNotFound = "outbox.version_retirement.handler_not_found";
    public const string AmbiguousHandler = "outbox.version_retirement.ambiguous_handler";
    public const string Safe = "outbox.version_retirement.safe";
    public const string Blocked = "outbox.version_retirement.blocked";
}

internal sealed class OutboxVersionRetirementException : Exception
{
    public OutboxVersionRetirementException(string code)
        : base(code)
    {
        Code = code;
    }

    public string Code { get; }
}
```

Implement `OutboxVersionRetirementCommandLine.Parse` so both dedicated options are required exactly once, values cannot be empty/options, schema version is a positive invariant integer, unrelated arguments preserve order, and dedicated arguments never reach `Host.CreateApplicationBuilder`.

- [x] **Step 4: Run the focused tests and verify GREEN**

Run the commands from Step 2.

Expected: build succeeds with 0 warnings/0 errors and both focused test methods pass.

- [x] **Step 5: Commit the parser slice**

```powershell
git add src/Hosts/Full.NET.Host.Worker/OutboxVersionRetirementCommandLine.cs tests/Full.NET.UnitTests/Outbox/OutboxVersionRetirementCommandLineTests.cs
git commit -m "feat(outbox): parse version retirement scan command"
```

### Task 2: Add a dual-provider read-only version snapshot

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Data.Abstractions/IOutboxBacklogReader.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperOutboxStore.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/OutboxSql.cs`
- Modify: `tests/Full.NET.IntegrationTests/Messaging/OutboxRecoveryTests.cs`

**Interfaces:**
- Consumes: canonical/legacy `messageTypes` 与正整数 `schemaVersion`。
- Produces: `Task<OutboxVersionRetirementSnapshot> ReadVersionRetirementAsync(IReadOnlyCollection<string> messageTypes, int schemaVersion, CancellationToken cancellationToken)`。

- [x] **Step 1: Write the failing SQL Server/MySQL integration tests**

```csharp
[TestMethod]
public async Task SqlServer_version_retirement_snapshot_counts_only_target_unprocessed_routes()
{
    await VerifyVersionRetirementSnapshotAsync(
        DatabaseProvider.SqlServer,
        await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
}

[TestMethod]
public async Task MySql_version_retirement_snapshot_counts_only_target_unprocessed_routes()
{
    await VerifyVersionRetirementSnapshotAsync(
        DatabaseProvider.MySql,
        await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
}
```

The shared assertion must migrate a fresh database, insert canonical and legacy schema-1 messages, mark one target route dead-lettered, insert a processed target route plus unrelated type/version rows, then assert:

```csharp
Assert.AreEqual(1L, snapshot.PendingCount);
Assert.AreEqual(1L, snapshot.DeadLetterCount);
Assert.AreEqual(oldestTargetOccurredAtUtc, snapshot.OldestUnprocessedOccurredAtUtc);
```

- [x] **Step 2: Run discovery/build and verify RED without occupying Docker**

Run:

```powershell
dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore
```

Expected: build fails because `ReadVersionRetirementAsync` and `OutboxVersionRetirementSnapshot` do not exist. Do not start Testcontainers in the shared development queue.

- [x] **Step 3: Extend the existing read contract and Dapper store**

Add:

```csharp
Task<OutboxVersionRetirementSnapshot> ReadVersionRetirementAsync(
    IReadOnlyCollection<string> messageTypes,
    int schemaVersion,
    CancellationToken cancellationToken);

public sealed record OutboxVersionRetirementSnapshot(
    long PendingCount,
    long DeadLetterCount,
    DateTimeOffset? OldestUnprocessedOccurredAtUtc);
```

Validate non-null/non-empty/distinct routes and positive schema version in `DapperOutboxStore`, then dispatch to provider-specific row mapping. Use `DateTimeOffset?` for SQL Server and normalize MySQL `DateTime?` to UTC exactly like the existing backlog path.

- [x] **Step 4: Add host-only provider SQL**

SQL Server statement:

```sql
SELECT COUNT_BIG(CASE WHEN DeadLetteredAtUtc IS NULL THEN 1 END) AS PendingCount,
       COUNT_BIG(CASE WHEN DeadLetteredAtUtc IS NOT NULL THEN 1 END) AS DeadLetterCount,
       MIN(OccurredAtUtc) AS OldestUnprocessedOccurredAtUtc
FROM fn_outbox_message
WHERE ProcessedAtUtc IS NULL
  AND MessageType IN @MessageTypes
  AND SchemaVersion = @SchemaVersion;
```

MySQL statement:

```sql
SELECT SUM(CASE WHEN DeadLetteredAtUtc IS NULL THEN 1 ELSE 0 END) AS PendingCount,
       SUM(CASE WHEN DeadLetteredAtUtc IS NOT NULL THEN 1 ELSE 0 END) AS DeadLetterCount,
       MIN(OccurredAtUtc) AS OldestUnprocessedOccurredAtUtc
FROM fn_outbox_message
WHERE ProcessedAtUtc IS NULL
  AND MessageType IN @MessageTypes
  AND SchemaVersion = @SchemaVersion;
```

Both statements must use stable statement names under `outbox.read_version_retirement.*` and `SqlDataScope.HostOnly`.

- [x] **Step 5: Build and run non-Docker static/focused checks**

Run:

```powershell
dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore
pnpm test:sql-safety
```

Expected: 0 warnings/0 errors and SQL safety/catalog checks pass. Defer the two Testcontainers methods until this branch reaches the head of the shared queue.

- [x] **Step 6: Commit the data slice**

```powershell
git add src/BuildingBlocks/Full.NET.Data.Abstractions/IOutboxBacklogReader.cs src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperOutboxStore.cs src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/OutboxSql.cs tests/Full.NET.IntegrationTests/Messaging/OutboxRecoveryTests.cs
git commit -m "feat(outbox): read version retirement blockers"
```

### Task 3: Execute the scan without starting the background processor

**Files:**
- Create: `src/Hosts/Full.NET.Host.Worker/OutboxVersionRetirementScanner.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/Program.cs`
- Create: `tests/Full.NET.UnitTests/Outbox/OutboxVersionRetirementScannerTests.cs`

**Interfaces:**
- Consumes: `OutboxVersionRetirementRequest`、已注册的 `IIntegrationEventHandler` 与 `IOutboxBacklogReader`。
- Produces: `OutboxVersionRetirementReport`，包含稳定 `Code`、canonical message type、schema version、实际扫描 routes、pending/dead-letter 数与最老未处理时间。

- [x] **Step 1: Write failing scanner tests**

```csharp
[TestMethod]
public async Task Scan_includes_canonical_and_legacy_routes_and_blocks_when_any_message_remains()
{
    var reader = Substitute.For<IOutboxBacklogReader>();
    reader.ReadVersionRetirementAsync(
            Arg.Is<IReadOnlyCollection<string>>(routes =>
                routes.SequenceEqual(new[] { TestHandler.Canonical, TestHandler.Legacy })),
            1,
            Arg.Any<CancellationToken>())
        .Returns(new OutboxVersionRetirementSnapshot(2, 1, Oldest));

    var report = await new OutboxVersionRetirementScanner(
            reader,
            [new TestHandler()])
        .ScanAsync(
            new OutboxVersionRetirementRequest(TestHandler.Canonical, 1),
            CancellationToken.None);

    Assert.AreEqual(OutboxVersionRetirementErrorCodes.Blocked, report.Code);
    Assert.IsFalse(report.CanRetire);
}

[TestMethod]
public async Task Scan_returns_safe_only_when_pending_and_dead_letter_counts_are_zero()
{
    var reader = Substitute.For<IOutboxBacklogReader>();
    reader.ReadVersionRetirementAsync(
            Arg.Any<IReadOnlyCollection<string>>(),
            1,
            Arg.Any<CancellationToken>())
        .Returns(new OutboxVersionRetirementSnapshot(0, 0, null));

    var report = await new OutboxVersionRetirementScanner(
            reader,
            [new TestHandler()])
        .ScanAsync(
            new OutboxVersionRetirementRequest(TestHandler.Canonical, 1),
            CancellationToken.None);

    Assert.AreEqual(OutboxVersionRetirementErrorCodes.Safe, report.Code);
    Assert.IsTrue(report.CanRetire);
}

[TestMethod]
public async Task Scan_rejects_a_route_without_a_registered_handler()
{
    var scanner = new OutboxVersionRetirementScanner(
        Substitute.For<IOutboxBacklogReader>(),
        []);

    var exception = await Assert.ThrowsExactlyAsync<OutboxVersionRetirementException>(
        () => scanner.ScanAsync(
            new OutboxVersionRetirementRequest("fullnet.missing", 1),
            CancellationToken.None));

    Assert.AreEqual(
        OutboxVersionRetirementErrorCodes.HandlerNotFound,
        exception.Code);
}
```

- [x] **Step 2: Build/run the focused scanner tests and verify RED**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --filter "FullyQualifiedName~OutboxVersionRetirementScannerTests" --minimum-expected-tests 3
```

Expected: build fails because the scanner/report do not exist.

- [x] **Step 3: Implement the scanner and stable report**

```csharp
internal sealed record OutboxVersionRetirementReport(
    string Code,
    string MessageType,
    int SchemaVersion,
    IReadOnlyList<string> Routes,
    long PendingCount,
    long DeadLetterCount,
    DateTimeOffset? OldestUnprocessedOccurredAtUtc)
{
    public bool CanRetire =>
        PendingCount == 0
        && DeadLetterCount == 0;
}
```

Resolve the requested route through `IntegrationEventHandlerMatcher.Match`, reject zero/multiple handlers with stable codes, scan `EventType` followed by ordinal-distinct `LegacyEventTypes`, and derive `Safe`/`Blocked` only from both counts.

- [x] **Step 4: Wire one-shot mode in Program**

Program must:

1. Parse/strip dedicated arguments before `Host.CreateApplicationBuilder`.
2. Register `OutboxProcessor` only when no retirement request exists.
3. Build the host and retain existing `ValidateUniqueRoutes`.
4. For normal mode, call `RunAsync` unchanged.
5. For scan mode, call `StartAsync`, set `CurrentTenantAccessor` to Host in a scope, execute the scanner, write one camelCase JSON report to stdout, stop the host, and return exit code `0` when safe or `2` when blocked.
6. For command/route errors, write only a JSON object containing the stable `code` to stderr and return `1`.

- [x] **Step 5: Run focused and full non-Docker checks**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --filter "FullyQualifiedName~OutboxVersionRetirement" --minimum-expected-tests 5
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 413
```

Expected on this branch base: five new methods pass and full Unit is 413/413. After rebasing the queued slices, replace 413 with `rebased main Unit canonical + 5`.

- [x] **Step 6: Commit the executable scan**

```powershell
git add src/Hosts/Full.NET.Host.Worker/OutboxVersionRetirementScanner.cs src/Hosts/Full.NET.Host.Worker/Program.cs tests/Full.NET.UnitTests/Outbox/OutboxVersionRetirementScannerTests.cs
git commit -m "feat(outbox): execute version retirement scan"
```

### Task 4: Rebase, verify both databases, document, and integrate

**Files:**
- Modify: `docs/operations/outbox-worker-topology.md`
- Modify: `docs/roadmap/capability-status.md`
- Create: `docs/verification/outbox-version-retirement-scan-2026-07-27.md`
- Modify: `docs/verification/test-threshold-audit-2026-07-19.md`
- Modify: `README.md`
- Modify: `docs/development/getting-started.md`
- Modify: `.github/workflows/ci.yml`
- Modify: `.agents/skills/fullnet-module-delivery/references/delivery-map.md`

**Interfaces:**
- Consumes: RateLimit、Seeding、DatabaseOptions、Caching 完整合入清理后的最新 `main`。
- Produces: 主线可执行的只读命令、双库证据、同步 canonical 门槛与已删除的临时分支/worktree。

- [x] **Step 1: Wait for the queue and rebase onto the latest clean main**

Verify Docker/Integration process count is zero, fetch the latest local `main`, then rebase this branch. Resolve only documentation/canonical drift; stop if any owned source/test file overlaps unexpectedly.

- [x] **Step 2: Run the two focused database tests**

Run:

```powershell
dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --filter "version_retirement_snapshot" --minimum-expected-tests 2 --timeout 15m
```

Expected: SQL Server 1/1 and MySQL 1/1 pass; pending/dead-letter/processed/other-version routing assertions all hold.

- [x] **Step 3: Update runbook and capability evidence**

Document the exact operational form:

```powershell
dotnet run --project src/Hosts/Full.NET.Host.Worker -c Release --no-build -- `
  --outbox-version-retirement-message-type fullnet.tenancy.tenant.provisioned `
  --outbox-version-retirement-schema-version 1
```

Record exit codes `0=safe`, `1=invalid/error`, `2=blocked`; state that dead letters block retirement, aliases are included automatically, the Handler must still be deployed, a producer freeze/retirement window is still required, and the command never replays or mutates messages. Remove only the now-closed “版本退役扫描” gap; adjacent upgrades, replay automation, production pressure/alerting remain open, so capability stays `Build-verified`.

- [x] **Step 4: Discover and synchronize canonical counts**

Build all four test assemblies, run `--list-tests json`, and set:

- Unit canonical = rebased main Unit canonical + 5.
- Compatibility canonical = rebased main value.
- Architecture canonical = rebased main value.
- Integration canonical = rebased main Integration canonical + 2.

Apply the exact discovered values to README, getting-started, CI, delivery-map and the latest threshold audit. The verification record must distinguish focused dual-database execution from full Integration execution.

- [x] **Step 5: Run final fresh gates**

Run:

```powershell
dotnet build Full.NET.slnx -c Release --no-restore
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 416
dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --no-ansi --progress off --minimum-expected-tests 7
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 49
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --minimum-expected-tests 191 --timeout 90m
pnpm test:governance
pnpm test:skills
pnpm exec prettier --check .
git diff --check
git status --short --branch
```

Expected: Release build 0 warnings/0 errors; all four suites meet exact discovered totals; Governance 11/11; Skill contracts 52/52; workspace, diff and status checks pass. Full Integration owns Docker only in this final queue position and leaves zero containers/processes.

The literals above are the fresh discovery totals from final base `216475c` plus
this slice: Unit 416, Compatibility 7, Architecture 49, Integration 191.

- [x] **Step 6: Perform rule/skill evolution reviews and close evidence**

Read `rules/rule-evolution.md` and `rules/skill-evolution.md`; record whether either threshold is met. Update the verification record with fresh commands, results, HEADs, Docker/process state, and explicit remaining Outbox gaps.

- [x] **Step 7: Commit, fast-forward main, reverify, and clean**

```powershell
git add docs README.md .github/workflows/ci.yml .agents/skills/fullnet-module-delivery/references/delivery-map.md
git commit -m "docs(outbox): close version retirement scan"
git -C G:\wwwroot\github_fork\Full.NET merge --ff-only codex/outbox-version-retirement-scan
```

After fast-forwarding, rerun the non-Docker canonical/static gates from `main`, confirm clean status apart from user-owned untracked directories, remove the linked worktree, delete `codex/outbox-version-retirement-scan`, and hand off the new main HEAD plus Docker/process state.
