# Full.NET Seed Data Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立可执行生产 Baseline、可叠加开发/演示/测试数据、模块可扩展且 SQL Server/MySQL 双库可验证的种子数据管道，并把现有 `--seed-local` 迁移为显式 `development` profile。

**Architecture:** 业务模块只实现 `Full.NET.Seeding.Abstractions` 中的贡献者契约；`Full.NET.Seeding.Dapper` 负责 Baseline 继承、依赖排序、数据库锁、执行审计和失败边界；`Host.Migrator` 在迁移成功后显式运行 profile。Production 使用 Baseline，Development/Demo/Test 在 Baseline 上叠加；场景 Test Factory 继续提供每个用例的隔离数据。

**Tech Stack:** .NET 10、Dapper、Microsoft.Data.SqlClient、MySqlConnector、DbUp、Microsoft Testing Platform、Testcontainers SQL Server/MySQL

**Migration status:** Seed 执行审计已经由 `007_SeedExecutionAudit.sql` 落地；该迁移中的 MySQL UUID 列仍为 `char(36)`，属于 ADR-0003 的存量转换范围。后续 UUID Binary16 使用 008/009，命名规范化使用 010/011，不得修改或复用已实现的 007。

## Global Constraints

- 默认 Migrator 只迁移；只有显式 `--seed baseline|development|demo|test` 才运行 Seed。
- Production 只允许 Baseline，必须在获取锁和写入审计前拒绝 Development/Demo/Test，首版没有绕过开关。
- Development、Demo 和 Test 必须确定性先执行 Baseline，再执行自己的 Overlay。
- `--seed-local` 仅保留一个兼容周期并精确映射为 `development`；新旧参数同时出现必须失败。
- 首个管理员等 System Bootstrap 通过 Baseline Contributor 复用现有安全服务，必须使用显式 Secret，禁止内置或记录默认密码。
- Contributor 按稳定名称和依赖图执行；审计历史不能作为跳过真实协调的依据。
- 每个 Contributor 自己保证事务和幂等；跨 Contributor 不建立全局事务，失败后通过重跑恢复。
- Seed 不删除数据、不重置密码、不覆盖用户修改的显示名称、状态或自定义授权。
- SQL Server 与 MySQL 必须同时实现迁移、执行锁、审计和真实集成测试。
- API/Worker 不得引用或执行 Seed Orchestrator；只有 Migrator 装配运行入口；Test 专用 Contributor 不进入正式发布物。
- 稳定 code/Identifier/Username/PermissionCode 不本地化；默认语言使用规范 `zh-CN`。
- 所有手写代码和 SQL 注释必须使用清晰中文并说明安全、事务或提供程序差异。

---

### Task 1: Seed contracts and project boundaries

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Seeding.Abstractions/Full.NET.Seeding.Abstractions.csproj`
- Create: `src/BuildingBlocks/Full.NET.Seeding.Abstractions/SeedProfile.cs`
- Create: `src/BuildingBlocks/Full.NET.Seeding.Abstractions/SeedContext.cs`
- Create: `src/BuildingBlocks/Full.NET.Seeding.Abstractions/SeedContributionResult.cs`
- Create: `src/BuildingBlocks/Full.NET.Seeding.Abstractions/IDataSeedContributor.cs`
- Create: `src/BuildingBlocks/Full.NET.Seeding.Abstractions/ISeedOrchestrator.cs`
- Modify: `Full.NET.slnx`
- Modify: `tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj`
- Modify: `tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj`
- Modify: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`
- Test: `tests/Full.NET.UnitTests/Seeding/SeedProfileTests.cs`

**Interfaces:**
- Consumes: `Full.NET.Abstractions.Results.Result<T>`。
- Produces: `SeedProfile`、`SeedProfileNames.TryParse(string?, out SeedProfile)`、`SeedContext`、`SeedContributionResult`、`IDataSeedContributor`、`ISeedOrchestrator.RunAsync`。

- [x] **Step 1: 创建会失败的契约和架构测试**

新增 `SeedProfileTests`：

```csharp
[TestClass]
public sealed class SeedProfileTests
{
    [DataRow("baseline", SeedProfile.Baseline)]
    [DataRow("development", SeedProfile.Development)]
    [DataRow("DEVELOPMENT", SeedProfile.Development)]
    [DataRow("demo", SeedProfile.Demo)]
    [DataRow("test", SeedProfile.Test)]
    [TestMethod]
    public void Supported_names_are_parsed_exactly(
        string value,
        SeedProfile expected)
    {
        Assert.IsTrue(SeedProfileNames.TryParse(value, out var actual));
        Assert.AreEqual(expected, actual);
    }

    [DataRow(null)]
    [DataRow("")]
    [DataRow("production")]
    [DataRow("dev")]
    [TestMethod]
    public void Unsupported_names_are_rejected(string? value) =>
        Assert.IsFalse(SeedProfileNames.TryParse(value, out _));
}
```

在 Architecture Tests 中先引用预期程序集并把它加入 `BuildingBlockAssemblies` 与 `ProductionAssemblies.All`；新增断言 Seeding Abstractions 不依赖 Dapper、Modules 或 Hosts。

- [x] **Step 2: 运行 RED**

Run: `dotnet build Full.NET.slnx --configuration Release`

Expected: FAIL，`Full.NET.Seeding.Abstractions` 项目和类型不存在。

- [x] **Step 3: 实现最小公共契约**

`SeedProfile.cs`：

```csharp
namespace Full.NET.Seeding.Abstractions;

/// <summary>
/// 标识生产安全 Baseline 以及允许叠加的环境种子数据集合。
/// </summary>
public enum SeedProfile
{
    Baseline,
    Development,
    Demo,
    Test,
}

/// <summary>
/// 提供 CLI 与审计使用的规范 profile 名称。
/// </summary>
public static class SeedProfileNames
{
    public static bool TryParse(string? value, out SeedProfile profile)
    {
        if (string.Equals(value, "baseline", StringComparison.OrdinalIgnoreCase))
        {
            profile = SeedProfile.Baseline;
            return true;
        }

        if (string.Equals(value, "development", StringComparison.OrdinalIgnoreCase))
        {
            profile = SeedProfile.Development;
            return true;
        }

        if (string.Equals(value, "demo", StringComparison.OrdinalIgnoreCase))
        {
            profile = SeedProfile.Demo;
            return true;
        }

        if (string.Equals(value, "test", StringComparison.OrdinalIgnoreCase))
        {
            profile = SeedProfile.Test;
            return true;
        }

        profile = default;
        return false;
    }

    public static string ToCanonicalName(this SeedProfile profile) => profile switch
    {
        SeedProfile.Baseline => "baseline",
        SeedProfile.Development => "development",
        SeedProfile.Demo => "demo",
        SeedProfile.Test => "test",
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };

    public static IReadOnlySet<SeedProfile> EffectiveLayers(
        this SeedProfile profile) => profile switch
    {
        SeedProfile.Baseline => new HashSet<SeedProfile> { SeedProfile.Baseline },
        SeedProfile.Development => new HashSet<SeedProfile>
        {
            SeedProfile.Baseline,
            SeedProfile.Development,
        },
        SeedProfile.Demo => new HashSet<SeedProfile>
        {
            SeedProfile.Baseline,
            SeedProfile.Demo,
        },
        SeedProfile.Test => new HashSet<SeedProfile>
        {
            SeedProfile.Baseline,
            SeedProfile.Test,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };
}
```

其余契约使用以下签名：

```csharp
public sealed record SeedContext(
    Guid RunId,
    SeedProfile Profile,
    string EnvironmentName,
    string DefaultLocale,
    string CorrelationId);

public sealed record SeedContributionResult(
    int CreatedCount,
    int UpdatedCount,
    int SkippedCount,
    string Code);

public interface IDataSeedContributor
{
    string Name { get; }
    int Version { get; }
    IReadOnlySet<SeedProfile> Profiles { get; }
    IReadOnlyCollection<string> Dependencies { get; }
    Task<SeedContributionResult> SeedAsync(
        SeedContext context,
        CancellationToken cancellationToken = default);
}

public sealed record SeedRunResult(
    Guid RunId,
    SeedProfile Profile,
    int ContributorCount,
    int CreatedCount,
    int UpdatedCount,
    int SkippedCount);

public interface ISeedOrchestrator
{
    Task<Result<SeedRunResult>> RunAsync(
        SeedProfile profile,
        CancellationToken cancellationToken = default);
}
```

全部 public API 添加中文 XML 文档，解释 Baseline 的生产安全边界、Overlay 只适用于对应环境，且结果计数不包含 Secret。

- [x] **Step 4: 运行 GREEN**

Run: `dotnet build Full.NET.slnx --configuration Release --no-restore`

Run: `dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 222 --timeout 5m`

Run: `dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 17 --timeout 5m`

Expected: 构建 0 错误；profile 与新依赖规则全部通过。

- [x] **Step 5: 提交**

```powershell
git add Full.NET.slnx src/BuildingBlocks/Full.NET.Seeding.Abstractions tests/Full.NET.UnitTests tests/Full.NET.ArchitectureTests
git commit -m "feat: add seed data contracts"
```

### Task 2: Contributor graph, command parsing and configuration boundary

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Seeding.Dapper/Full.NET.Seeding.Dapper.csproj`
- Create: `src/BuildingBlocks/Full.NET.Seeding.Dapper/SeedOptions.cs`
- Create: `src/BuildingBlocks/Full.NET.Seeding.Dapper/SeedCommandLine.cs`
- Create: `src/BuildingBlocks/Full.NET.Seeding.Dapper/SeedContributorGraph.cs`
- Create: `src/BuildingBlocks/Full.NET.Seeding.Dapper/SeedErrorCodes.cs`
- Create: `src/BuildingBlocks/Full.NET.Seeding.Dapper/ServiceCollectionExtensions.cs`
- Modify: `Full.NET.slnx`
- Modify: `tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj`
- Test: `tests/Full.NET.UnitTests/Seeding/SeedCommandLineTests.cs`
- Test: `tests/Full.NET.UnitTests/Seeding/SeedContributorGraphTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `SeedProfile` 与 `IDataSeedContributor`。
- Produces: `SeedCommandLine.Parse(IReadOnlyList<string>)` 返回 `SeedCommandLineOptions(SeedProfile? Profile, bool UsesLegacyAlias)`、`SeedContributorGraph.Order`、`SeedOptions.SectionName = "Seeding"`。

- [x] **Step 1: 先写 CLI 和依赖图失败测试**

覆盖以下行为：

```csharp
Assert.AreEqual(
    SeedProfile.Development,
    SeedCommandLine.Parse(["--seed", "development"]).Profile);
Assert.AreEqual(
    SeedProfile.Baseline,
    SeedCommandLine.Parse(["--seed", "baseline"]).Profile);
Assert.IsTrue(SeedCommandLine.Parse(["--seed-local"]).UsesLegacyAlias);
Assert.ThrowsExactly<SeedConfigurationException>(() =>
    SeedCommandLine.Parse(["--seed-local", "--seed", "development"]));
Assert.ThrowsExactly<SeedConfigurationException>(() =>
    SeedCommandLine.Parse(["--seed", "production"]));
```

依赖图使用测试 Contributor 覆盖：按 Name 确定性排序、依赖优先、重复名称、缺失依赖、循环依赖、Version 小于 1 和当前 profile 不适用的贡献者过滤；Development/Demo/Test 各自包含 Baseline Contributor，但不包含其他 Overlay。

- [x] **Step 2: 运行 RED**

Run: `dotnet build Full.NET.slnx --configuration Release`

Expected: FAIL，Dapper Seed 项目与解析/排序类型不存在。

- [x] **Step 3: 实现项目与配置边界**

`SeedOptions` 固定：

```csharp
public sealed class SeedOptions
{
    public const string SectionName = "Seeding";
    public string DefaultLocale { get; set; } = "zh-CN";
    public int LockTimeoutSeconds { get; set; } = 30;
}
```

`SeedCommandLine.Parse` 只接受零个 Seed 参数、四个规范 `--seed` profile 或单独 `--seed-local`。重复参数、缺值、新旧参数并存或未知 profile 抛出只包含安全稳定 code 的 `SeedConfigurationException`。

`SeedContributorGraph.Order` 先验证全部名称和版本，再用 `profile.EffectiveLayers()` 过滤 Baseline 与目标 Overlay，检查依赖，使用 Name 作为同层排序键执行 Kahn 拓扑排序。循环返回 `seeding.dependency.cycle`，不得依赖 DI 枚举顺序。

- [x] **Step 4: 验证 GREEN**

Run: `dotnet build Full.NET.slnx --configuration Release --no-restore`

Run: `dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 246 --timeout 5m`

Run: `dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 18 --timeout 5m`

Expected: CLI、profile 过滤和依赖图全部通过，无任意 profile 透传。

- [x] **Step 5: 提交**

```powershell
git add Full.NET.slnx src/BuildingBlocks/Full.NET.Seeding.Dapper tests/Full.NET.UnitTests
git commit -m "feat: validate seed profiles and contributors"
```

### Task 3: Dual-database execution audit and lease

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Seeding.Dapper/SeedExecutionLease.cs`
- Create: `src/BuildingBlocks/Full.NET.Seeding.Dapper/SeedExecutionStore.cs`
- Create: `src/BuildingBlocks/Full.NET.Seeding.Dapper/SeedOrchestrator.cs`
- Modify: `src/BuildingBlocks/Full.NET.Seeding.Dapper/ServiceCollectionExtensions.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/007_SeedExecutionAudit.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/007_SeedExecutionAudit.sql`
- Modify: `tests/Full.NET.IntegrationTests/Migrations/SqlServerMigrationTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Migrations/MySqlMigrationTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj`
- Test: `tests/Full.NET.UnitTests/Seeding/SeedOrchestratorTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Seeding/SeedInfrastructureTests.cs`

**Interfaces:**
- Consumes: Task 2 的排序、配置和错误码；现有 `DatabaseOptions`、`IClock`、`IIdGenerator`。
- Produces: `AddFullNetSeeding(IConfiguration)` 与 `ISeedOrchestrator` Dapper 实现。

- [x] **Step 1: 写 Orchestrator RED 测试**

使用可记录的 Store/Lease 替身和真实 Contributor 对象断言：

- Production 运行 Baseline 可以继续；运行 Development/Demo/Test 在 `AcquireAsync` 前返回 `seeding.profile.not_allowed`；
- 非 Production 获取一次 lease，逐项记录 Running/Succeeded；
- Contributor 失败后记录 Failed 并停止后续项；
- 取消返回 `seeding.execution.cancelled`；
- 成功结果聚合 Created/Updated/Skipped；
- 审计只接收 ErrorCode，不接收异常 Message 或 Seed 输入。

同时先创建 `SeedInfrastructureTests`，让 SQL Server/MySQL 分别断言 `007_SeedExecutionAudit.sql` 后存在 run/item 两表、同一资源的第二个 lease 在短超时内失败、释放后可再次获取，以及参数化 run/item 审计可以读取。此时测试必须因迁移、Lease 和 Store 不存在而失败。

- [x] **Step 2: 运行 Unit RED**

Run: `dotnet build Full.NET.slnx --configuration Release`

Expected: FAIL，Orchestrator、Store 与 Lease 类型不存在。

- [x] **Step 3: 编写双库迁移**

两份 `007_SeedExecutionAudit.sql` 创建 `fn_seed_run` 和 `fn_seed_run_item`。`005_SuperAdministrator.sql` 与 `006_SuperAdministratorAuditActor.sql` 已由受保护超级管理员切片占用；SQL Server 使用 `uniqueidentifier/datetimeoffset/nvarchar`，MySQL 使用 `char(36)/datetime(6)/varchar`；都包含：

这里记录的是 007 落地时的真实物理类型，不表示 MySQL `char(36)` 是最终方案。008/009 UUID Binary16 计划必须把 `fn_seed_run.Id` 与 `fn_seed_run_item.RunId` 一并转换，并保持 Seeder/Contributor 只使用 C# `Guid`。

```text
fn_seed_run: Id PK, Profile, EnvironmentName, Status, ApplicationVersion,
             CorrelationId, StartedAt, CompletedAt NULL, ErrorCode NULL
fn_seed_run_item: RunId + Contributor PK/FK, ContributorVersion, Status,
                  CreatedCount, UpdatedCount, SkippedCount,
                  StartedAt, CompletedAt NULL, ErrorCode NULL
```

状态列限制为 16 字符，Contributor 为 128 字符，ErrorCode 为 128 字符。SQL 文件头用中文说明这是执行审计而非幂等跳过表，已发布后只能向前迁移。

- [x] **Step 4: 实现 provider-specific lease**

`SeedExecutionLease.AcquireAsync` 根据 `DatabaseProvider` 打开并持有专用连接：

```text
SQL Server: EXEC @result = sys.sp_getapplock
            @Resource = 'Full.NET.Seeding',
            @LockMode = 'Exclusive',
            @LockOwner = 'Session',
            @LockTimeout = @LockTimeoutMilliseconds

MySQL: SELECT GET_LOCK('Full.NET.Seeding', @LockTimeoutSeconds)
```

SQL Server 返回值小于 0 或 MySQL 不返回 1 时映射 `seeding.lock.timeout`。Dispose 时分别调用 `sp_releaseapplock`、`RELEASE_LOCK`，保持原执行结果优先。

- [x] **Step 5: 实现审计 Store 和 Orchestrator**

Store 使用参数化 Dapper 写入 run/item；Orchestrator 顺序固定为：环境门禁、依赖图验证、获取锁、StartRun、Contributor 循环、CompleteRun。Orchestrator 与 Contributor 都注册为 Scoped，并由 Migrator 的同一个显式执行 Scope 解析，禁止 Singleton 捕获 Scoped Contributor 或请求级租户状态。Contributor 改变 `CurrentTenant` 时必须在自身 `finally` 中恢复。

- [x] **Step 6: 运行 Unit GREEN**

Run: `dotnet build Full.NET.slnx --configuration Release --no-restore`

Run: `dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 132 --timeout 5m`

Expected: Orchestrator 的门禁、失败、取消和计数测试全部通过。

- [x] **Step 7: 运行真实双库迁移/锁 GREEN**

迁移测试断言两表、主外键、长度和可空性。运行 Step 1 已建立的 `SeedInfrastructureTests`，确认每个 provider 都能执行迁移、获取第一个 lease、让第二个短超时获取失败、释放后重新获取成功，并查询 run/item 审计行。

Run: `dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --minimum-expected-tests 10 --timeout 10m`

Expected: SQL Server/MySQL 的迁移、锁竞争和审计均通过。

- [x] **Step 8: 提交**

```powershell
git add src/BuildingBlocks/Full.NET.Seeding.Dapper src/BuildingBlocks/Full.NET.Migrations.DbUp tests/Full.NET.UnitTests tests/Full.NET.IntegrationTests
git commit -m "feat: add dual database seed runner"
```

### Task 4: Tenancy development contributor

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Seeding.Abstractions/SeedContributionException.cs`
- Modify: `src/BuildingBlocks/Full.NET.Seeding.Dapper/SeedOrchestrator.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Full.NET.Modules.Tenancy.csproj`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Seeding/LocalTenantSeedContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenancyModule.cs`
- Test: `tests/Full.NET.UnitTests/Tenancy/LocalTenantSeedContributorTests.cs`

**Interfaces:**
- Consumes: `IDataSeedContributor`、`ITenantProvisioningService`、`IQueryExecutor`、`TenantSql`。
- Produces: contributor `Name = "tenancy.local_tenant"`、`Version = 1`、Profiles 只含 Development。

- [x] **Step 1: 写 Contributor RED 测试**

覆盖三个独立场景：

1. 查不到 Identifier 时调用 `ProvisionAsync(new("local", "Full.NET Local", "localhost"))`，返回 Created=1；
2. 已存在且 Identifier/Name/Domain 完全一致时不调用 Provision，返回 Skipped=1；
3. Identifier 已存在但 Name 或 Domain 不一致时返回/抛出稳定 `seeding.data.conflict`，不覆盖现有租户。

同时断言 Demo profile 不会选择该 Contributor，取消令牌传入查询和 Provision。

- [x] **Step 2: 运行 RED**

Run: `dotnet build Full.NET.slnx --configuration Release`

Expected: FAIL，Tenancy 尚未引用 Seed Abstractions，Contributor 不存在。

- [x] **Step 3: 实现只查询完整摘要的 SQL**

新增 `TenantSql.FindSummaryByIdentifier`：

```sql
SELECT Id, Identifier, Name, Domain, IsActive, Version
FROM fn_tenant_tenant
WHERE Identifier = @Identifier
```

使用 `SqlDataScope.Global`，只允许 Seeder 在 Host 上下文中调用；保留现有计数 SQL，避免无关重构。

- [x] **Step 4: 实现 Contributor 并注册**

Contributor 先按规范 Identifier 查询；完全匹配返回 skipped；存在冲突拒绝；不存在时调用真实 `ITenantProvisioningService`，从而保留领域校验、事务与 MessagePack Outbox。`TenancyModule.AddServices` 使用 `TryAddEnumerable` 注册 Scoped Contributor，避免重复注册。

- [x] **Step 5: 运行 GREEN**

Run: `dotnet build Full.NET.slnx --configuration Release --no-restore`

Run: `dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 260 --timeout 5m`

Expected: 新建、跳过、冲突和取消全部通过；既有 Tenancy 测试不回归。

- [x] **Step 6: 提交**

```powershell
git add src/Modules/Full.NET.Modules.Tenancy tests/Full.NET.UnitTests/Tenancy
git commit -m "feat: seed local development tenant"
```

### Task 5: Identity Baseline contributor and Migrator composition

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity/Full.NET.Modules.Identity.csproj`
- Create: `src/Modules/Full.NET.Modules.Identity/Seeding/HostAdministratorSeedContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Modify: `src/Hosts/Full.NET.Host.Migrator/Full.NET.Host.Migrator.csproj`
- Create: `src/Hosts/Full.NET.Host.Migrator/MigratorWorkflow.cs`
- Modify: `src/Hosts/Full.NET.Host.Migrator/Program.cs`
- Modify: `src/Hosts/Full.NET.AppHost/Program.cs`
- Modify: `src/Hosts/Full.NET.Host.Migrator/appsettings.json`
- Modify: `tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj`
- Test: `tests/Full.NET.UnitTests/Identity/HostAdministratorSeedContributorTests.cs`
- Test: `tests/Full.NET.UnitTests/Hosting/MigratorWorkflowTests.cs`
- Modify: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`

**Interfaces:**
- Consumes: `AddFullNetSeeding`、`SeedCommandLine.Parse`、`ISeedOrchestrator`、`IIdentityBootstrapService` 与 `IdentityOptions.Bootstrap`。
- Produces: `identity.host_administrator` Baseline Contributor，以及 Migrator 的迁移、可选 Seed 两阶段退出语义；Contributor 必须遵循受保护超级管理员计划，不能复制角色或权限 SQL。

- [ ] **Step 1: 写 Identity Baseline Contributor RED 测试**

覆盖：Username/Password 任一缺失都返回 `seeding.bootstrap.secret_missing` 且不调用 Bootstrap；完整 Secret 调用 `BootstrapHostAdminAsync`，新建返回 Created=1、已存在受保护角色/账号关系修复返回 Updated=1；Bootstrap 失败映射稳定错误码；结果、日志和异常不包含 Password。Contributor 固定 `Name = "identity.host_administrator"`、`Version = 1`、Profiles 只含 Baseline。它只调用领域服务，不复制角色、权限或最后一名保护算法；Overlay 不得创建超级管理员。

- [ ] **Step 2: 写 Workflow RED 测试**

用替身依赖覆盖：

- 无 Seed 参数：只运行迁移，不运行 Orchestrator；
- `--seed baseline`：迁移成功后运行 Baseline；
- `--seed development`：迁移成功后运行 Development，Orchestrator 负责先执行 Baseline；
- `--seed-local`：运行 Development 并产生弃用标志；
- 迁移失败：不运行 Seed；Seed 失败：进程失败；
- 取消传播到两个阶段。

- [ ] **Step 3: 运行 RED**

Run: `dotnet build Full.NET.slnx --configuration Release`

Expected: FAIL，`MigratorWorkflow` 不存在。

- [ ] **Step 4: 实现 Baseline Contributor**

Identity 模块引用 Seed Abstractions 并以 Scoped `TryAddEnumerable` 注册 Contributor。Contributor 从 `IOptions<IdentityOptions>` 读取 Bootstrap 配置，在任何日志或结果建立前验证 Username/Password 成对存在，再调用现有 `IIdentityBootstrapService`；不得复制密码校验、用户 SQL 或授权同步逻辑。

- [ ] **Step 5: 抽取可测试工作流**

`Program.cs` 只负责 Host/DI/日志和退出码；`MigratorWorkflow.RunAsync` 按迁移、可选 Seed 两阶段执行。安全异常对 CLI 只暴露稳定 code；完整异常由结构化日志记录一次。所有新增公开/内部扩展点使用中文 XML 文档说明迁移失败阻断后续写入。

- [ ] **Step 6: 更新装配和 AppHost**

Migrator 引用 `Full.NET.Seeding.Dapper`，调用 `AddFullNetSeeding(builder.Configuration)`；AppHost 将 `.WithArgs("--seed-local")` 改为 `.WithArgs("--seed", "development")`。`appsettings.json` 增加：

```json
{
  "Seeding": {
    "DefaultLocale": "zh-CN",
    "LockTimeoutSeconds": 30
  }
}
```

不增加 Enabled、Force 或 Production 绕过配置。

- [ ] **Step 7: 加强架构门禁**

Architecture Tests 断言 Host.Api 和 Host.Worker 不依赖 `Full.NET.Seeding.Dapper`；Migrator 必须依赖它；Modules 不依赖 `Full.NET.Seeding.Dapper`。

- [ ] **Step 8: 运行 GREEN**

Run: `dotnet build Full.NET.slnx --configuration Release --no-restore`

Run: `dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 142 --timeout 5m`

Run: `dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 13 --timeout 5m`

Expected: Baseline Contributor、工作流顺序、Secret 边界和 Host 依赖门禁通过。

- [ ] **Step 9: 提交**

```powershell
git add src/Modules/Full.NET.Modules.Identity src/Hosts tests/Full.NET.UnitTests/Identity tests/Full.NET.UnitTests/Hosting tests/Full.NET.ArchitectureTests
git commit -m "feat: orchestrate baseline and development seeds"
```

### Task 6: End-to-end dual database seed contract verification

**Files:**
- Create: `tests/Full.NET.IntegrationTests/Seeding/DevelopmentSeedTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Seeding/TestOnlySeedContributor.cs`
- Modify: `tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj`
- Modify: `tests/Full.NET.IntegrationTests/Tenancy/TenantProvisioningTests.cs`

**Interfaces:**
- Consumes: S0-S2 的完整 Migrator 服务注册、Development Contributor、双库审计和 Outbox。
- Produces: 首次、重复、冲突、失败恢复与 Production 门禁的双库证据。

- [ ] **Step 1: 增加两个 provider 的纵向契约测试**

每个 provider 启动全新 Testcontainer，执行迁移并构建与 Migrator 相同的服务集合。测试顺序：

1. `RunAsync(Development)` 首次成功，并按依赖顺序执行 `identity.host_administrator` 与 `tenancy.local_tenant`；
2. 查询宿主管理员、受保护超级管理员系统角色/关系和 `fn_tenant_tenant`，各自只有期望记录，local 恰好 1 条；超级管理员动态权限由独立授权测试验证，不以逐项权限行数量作为事实源；
3. 查询未处理的 TenantProvisioned Outbox，恰好 1 条；
4. 第二次运行成功，管理员密码不改变，租户和创建 Outbox 仍各 1 条，第二个 run 的 Baseline/Development item 以 Updated/Skipped 报告；
5. 在另一全新数据库手工写入 Identifier=`local`、Domain=`conflict.localhost` 后运行 Development，返回 `seeding.data.conflict` 且不覆盖；
6. Production 环境运行 Baseline 成功；运行 Development/Demo/Test 返回 `seeding.profile.not_allowed`，且拒绝的 profile 不新增 `fn_seed_run`；
7. 非 Production 运行 Test 时执行 Baseline 和测试程序集注册的 `TestOnlySeedContributor`，但不执行 Development/Demo Contributor。

这些场景的单元行为已经在 Tasks 3-5 分别完成 RED/GREEN；本任务把它们组合成真实双库纵向证据，不新增生产行为。

- [ ] **Step 2: 运行纵向验证**

Run: `dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --minimum-expected-tests 18 --timeout 15m`

Expected: SQL Server/MySQL 的 Baseline 生产初始化、Development/Test 继承、首次、重复、冲突、审计、Outbox 和 Production 门禁全部通过。若任一场景失败，停止任务并使用 `superpowers:systematic-debugging` 先建立最小失败复现，再修改所属实现；禁止放宽门禁、删除断言或绕过真实 Dapper。

- [ ] **Step 3: 提交**

```powershell
git add src tests/Full.NET.IntegrationTests
git commit -m "test: verify dual database development seeds"
```

### Task 7: Documentation, CI thresholds and governance

**Files:**
- Modify: `README.md`
- Modify: `docs/development/getting-started.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `docs/superpowers/specs/2026-07-17-fullnet-architecture-design.md`
- Modify: `.github/workflows/ci.yml`
- Modify: `AGENTS.md`
- Modify: `rules/development-quality.md`
- Modify: `rules/skill-evolution.md`
- Modify: `.agents/skills/fullnet-module-delivery/references/delivery-map.md`

**Interfaces:**
- Consumes: Tasks 1-6 的最终命令、测试数量和运行边界。
- Produces: 可复制的开发 Seed 用法、生产门禁、真实完成状态和后续 Skill 候选证据。

- [ ] **Step 1: 更新使用文档**

README/getting-started 明确：

- Aspire 默认传 `--seed development`；
- 手工 Migrator 的新命令和 `--seed-local` 弃用期；
- 管理员凭据必须通过 user-secrets/部署 Secret；
- 默认 Migrator 不执行 Seed，生产通过显式 Baseline 初始化，Production 不执行 Development/Demo/Test；
- Seed 数据持久存在于目标数据库；IntegrationTests 在临时容器中复用 Baseline/Test，并以 Test Factory 隔离场景数据；
- 重跑只补齐/跳过，不删除或覆盖用户数据；
- 查看 `fn_seed_run`/`fn_seed_run_item` 时不得把表当业务幂等开关。

- [ ] **Step 2: 更新 CI 与测试门槛**

先用最终程序集输出确认精确测试数量，再把 Unit、Architecture、Integration 的 `--minimum-expected-tests` 同步到 README、getting-started、CI 和项目 Skill 交付地图。CI 必须实际运行 SQL Server/MySQL Testcontainers 测试，Docker 不可用时失败而不是静默跳过。

- [ ] **Step 3: 演进项目规则和 Skill 候选**

新增强制 Seed 边界：Production 只允许 Baseline、Development/Demo/Test 继承 Baseline、首个管理员必须使用 Secret、测试专用 Contributor 不进入发布物、场景 Test Factory 保持隔离、Contributor 必须双库幂等。更新 `fullnet-seed-data-delivery` 候选；在第二个真实业务模块贡献 Seed 之前不创建新 Skill。

- [ ] **Step 4: 执行完整验证**

Run: `dotnet build Full.NET.slnx --configuration Release --no-restore`

Run: 四套 Microsoft Testing Platform 程序集，使用最终确认的最小数量门槛。

Run: `pnpm test:workspace`

Run: `pnpm test:clients`

Run: `pnpm build:clients`

Run: `pnpm test:e2e`

Run: `python -X utf8 tests/skills/validate_project_skills.py`

Run: `git diff --check`

Expected: 所有维护范围通过；SQL Server/MySQL 没有跳过；文档准确说明 Baseline、Development/Demo/Test 继承和 Scenario Test Fixture。

- [ ] **Step 5: 提交**

```powershell
git add README.md docs .github AGENTS.md rules .agents/skills/fullnet-module-delivery/references/delivery-map.md
git commit -m "docs: govern seed data delivery"
```
