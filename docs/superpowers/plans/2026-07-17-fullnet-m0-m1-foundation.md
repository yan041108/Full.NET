# Full.NET M0-M1 Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the independently runnable Full.NET engineering foundation and first tenancy vertical slice with source-generated System.Text.Json, high-concurrency structured logging, Dapper, DbUp, MessagePack Outbox, FusionCache, standard HTTP/ProblemDetails, Worker, Migrator, Aspire AppHost, SQL Server, and MySQL verification.

**Architecture:** This plan delivers M0 and M1 only. It creates small BuildingBlocks, explicit module registration, a Dapper-first data path, a global tenant registry, source-generated external JSON, binary versioned integration events, and a secure-by-construction request path from host resolution to a tenancy query. API, Worker, and Migrator are separate hosts; Aspire orchestrates them locally. Same-process modules call typed contracts without serialization; gRPC, SignalR, AI, and Agentic Web remain documented extension boundaries until a real later feature consumes them.

**Tech Stack:** .NET 10 LTS, ASP.NET Core Minimal APIs, source-generated System.Text.Json, MessagePack 3.1.8, Serilog.AspNetCore 10.0.0 with Serilog.Sinks.Async 2.1.0, Dapper 2.1.79, DbUp 6.1.x/7.2.0 providers, Microsoft.Data.SqlClient 7.0.2, MySqlConnector 2.6.1, FusionCache 2.6.0 with `.AsHybridCache()`, Redis, OpenTelemetry, .NET Aspire 13.4.6, MSTest 4.3.2, Testcontainers 4.13.0, NetArchTest 1.3.2, BenchmarkDotNet 0.15.8.

## Global Constraints

- Repository root is exactly `G:\wwwroot\github_fork\Full.NET`; do not touch the dirty parent repository.
- Target `net10.0`; enable Nullable, implicit usings, latest analyzers, and warnings as errors.
- Use Dapper for runtime relational access. Do not add EF Core, SqlSugar, Furion, MediatR, or a generic repository.
- Officially verify SQL Server and MySQL 8 in every database task.
- Every SQL statement is parameterized and declares `Global`, `TenantRequired`, or `HostOnly` scope.
- Tenant-required SQL contains `@TenantId`; no automatic SQL parsing or predicate rewriting.
- FusionCache is the only cache implementation. Expose the same instance as both `IFusionCache` and Microsoft `HybridCache`; never call `AddHybridCache()`.
- Default API contract is real HTTP status codes plus ProblemDetails. Admin.NET envelopes are opt-in through `Full.NET.Compatibility.AdminNet` and must retain real HTTP status codes.
- Use System.Text.Json with per-module source-generated `JsonSerializerContext` for public HTTP DTOs. Newtonsoft.Json is not an M0-M1 dependency.
- Same-process modules use typed contracts without serialization. Reliable Outbox events use MessagePack binary payloads with explicit integer keys, `MessagePackSecurity.UntrustedData`, `ContentType`, and `SchemaVersion`; never use Typeless or Contractless resolvers.
- Application code logs only through `ILogger<T>`; fixed hot-path messages use `[LoggerMessage]`. Serilog is the provider, and slow sinks run behind a bounded asynchronous buffer with observable queue and drop counts.
- Operational logs may shed low-priority events under pressure; security and business audit records must later use transactional database/Outbox persistence and are never implemented as best-effort Serilog events.
- Use constructor injection only; no service locator and no static global service provider.
- Keep module internals `internal`; only `Contracts` are public to other modules.
- Use UUID v7 through `Guid.CreateVersion7()`; persist SQL Server IDs as `uniqueidentifier` and MySQL IDs as `char(36)` in M1.
- Store timestamps as UTC. Use `DateTimeOffset` in C# and provider-appropriate UTC columns.
- Full.NET source is MIT. Treat Admin.NET.Pro as a capability/acceptance reference unless explicit MIT relicensing permission exists for copied code.
- Use `apply_patch` for hand-written file changes; `dotnet new` is allowed for mechanical project scaffolding.
- Follow TDD: failing focused test, observed failure, minimal implementation, passing focused test, then commit.

---

## Plan Scope and Follow-on Plans

This plan ends when a migrated database can provision a tenant transactionally, write a versioned MessagePack Outbox message, resolve a tenant from the request host, return it through `/api/v1/tenancy/current` using a source-generated JSON contract, process the Outbox message in Worker, expose FusionCache through both abstractions, emit bounded structured logs, and pass SQL Server/MySQL integration tests plus focused serialization benchmarks.

Create later plans only after this one is complete:

1. M2 Tenancy administration, Identity Dapper Store, sessions, RBAC, Organization, data scopes, `Full.NET.Realtime.Abstractions`, SignalR/MessagePack, and Redis Backplane.
2. M3 Settings, Auditing, Files, Notifications consuming `IRealtimePublisher`, Jobs, CodeGeneration, templates, CRM sample, and Vue admin.
3. M4 dual-database hardening, E2E, performance budgets, Docker release, security review, and 1.0 packaging.
4. M5+ separate plans for `Full.NET.AI.Abstractions`, AI providers, Microsoft Agent Framework integration, MCP Client/Server, and the isolated AG-UI adapter, plus one plan per remaining Admin.NET parity module.
5. Create a gRPC + Protobuf plan only when the first real cross-process synchronous contract is identified; do not route the modular monolith through gRPC in M0-M1.

## Planned File Map

```text
Full.NET.slnx
global.json
Directory.Build.props
Directory.Packages.props
.editorconfig
.gitignore
LICENSE
THIRD-PARTY-NOTICES
.github/workflows/ci.yml

src/BuildingBlocks/
  Full.NET.Abstractions/
    Results/{ErrorType.cs,Error.cs,Result.cs,PagedResult.cs}
    Messaging/{ICommand.cs,IQuery.cs,ICommandTransaction.cs}
    Tenancy/{TenantContext.cs,ICurrentTenant.cs,CurrentTenantAccessor.cs}
    Time/{IClock.cs,SystemClock.cs}
    Ids/{IIdGenerator.cs,GuidV7IdGenerator.cs}
  Full.NET.Modularity/
    Messaging/{CommandDispatcher.cs,QueryDispatcher.cs}
    Modules/{IFullNetModule.cs,FullNetModuleRegistry.cs,ModuleExtensions.cs}
  Full.NET.Data.Abstractions/
    {DatabaseProvider.cs,DatabaseOptions.cs,SqlDataScope.cs,SqlStatement.cs}
    {IQueryExecutor.cs,ICommandExecutor.cs,IOutboxWriter.cs,IOutboxStore.cs}
  Full.NET.Data.Dapper/
    {DbConnectionFactory.cs,DbSession.cs,SqlScopeGuard.cs,DapperSqlExecutor.cs,DapperCommandTransaction.cs}
    Outbox/{OutboxMessage.cs,DapperOutboxWriter.cs,DapperOutboxStore.cs}
  Full.NET.Serialization.MessagePack/
    {MessagePackIntegrationEventSerializer.cs,ServiceCollectionExtensions.cs}
  Full.NET.Migrations.DbUp/
    {IDatabaseMigrationRunner.cs,DbUpMigrationRunner.cs,MigrationAssembly.cs}
    Migrations/{SqlServer,MySql}/*.sql
  Full.NET.Caching.Fusion/
    {CacheOptions.cs,CacheKeyBuilder.cs,ServiceCollectionExtensions.cs}
  Full.NET.Hosting/
    Api/{IApiResultMapper.cs,StandardApiResultMapper.cs,FullNetExceptionHandler.cs}
    Serialization/{FullNetJsonOptionsExtensions.cs}
    Observability/{LoggingOptions.cs,FullNetAsyncLogMonitor.cs,HostingLog.cs,ServiceDefaultsExtensions.cs,HealthEndpointExtensions.cs}

src/Modules/Full.NET.Modules.Tenancy/
  Contracts/{TenantSummary.cs,TenantProvisionedIntegrationEvent.cs}
  Domain/Tenant.cs
  Persistence/{TenantSql.cs,TenantResolver.cs}
  Features/ProvisionTenant/{Command.cs,Handler.cs}
  Features/GetCurrentTenant/{Query.cs,Handler.cs,Endpoint.cs}
  Serialization/TenancyJsonSerializerContext.cs
  TenancyModule.cs

src/Compatibility/Full.NET.Compatibility.AdminNet/
  {AdminNetEnvelope.cs,AdminNetApiResultMapper.cs,ServiceCollectionExtensions.cs}

src/Hosts/
  Full.NET.Host.Api/{Program.cs,appsettings.json,appsettings.Development.json}
  Full.NET.Host.Worker/{Program.cs,OutboxProcessor.cs,appsettings.json}
  Full.NET.Host.Migrator/{Program.cs,appsettings.json}
  Full.NET.AppHost/{Program.cs,appsettings.json}

tests/
  Full.NET.UnitTests/
  Full.NET.ArchitectureTests/
  Full.NET.IntegrationTests/
  Full.NET.CompatibilityTests/

benchmarks/
  Full.NET.Benchmarks/{Program.cs,SerializationBenchmarks.cs}
```

### Task 1: Create the buildable repository skeleton

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `.editorconfig`
- Create: `.gitignore`
- Create: `LICENSE`
- Create: `THIRD-PARTY-NOTICES`
- Create: `Full.NET.slnx`
- Create: all M0-M1 `.csproj` files listed in the file map

**Interfaces:**
- Produces: a clean `net10.0` solution with centrally managed package versions and empty projects for later tasks.

- [ ] **Step 1: Verify the independent repository baseline**

Run:

```powershell
git -C 'G:\wwwroot\github_fork\Full.NET' rev-parse --show-toplevel
git -C 'G:\wwwroot\github_fork\Full.NET' status --short
```

Expected: top level is `G:/wwwroot/github_fork/Full.NET`; status is empty before scaffolding.

- [ ] **Step 2: Create SDK and repository-wide build configuration**

Create `global.json`:

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": true
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  },
  "msbuild-sdks": {
    "MSTest.Sdk": "4.3.2"
  }
}
```

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <Deterministic>true</Deterministic>
    <NuGetAudit>true</NuGetAudit>
    <NuGetAuditMode>all</NuGetAuditMode>
  </PropertyGroup>
  <PropertyGroup Condition="'$(CI)' == 'true'">
    <ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
  </PropertyGroup>
</Project>
```

Create `Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Aspire.Hosting.MySql" Version="13.4.6" />
    <PackageVersion Include="Aspire.Hosting.Redis" Version="13.4.6" />
    <PackageVersion Include="Aspire.Hosting.SqlServer" Version="13.4.6" />
    <PackageVersion Include="BenchmarkDotNet" Version="0.15.8" />
    <PackageVersion Include="Dapper" Version="2.1.79" />
    <PackageVersion Include="dbup-core" Version="6.1.1" />
    <PackageVersion Include="dbup-mysql" Version="6.1.0" />
    <PackageVersion Include="dbup-sqlserver" Version="7.2.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.10" />
    <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.10" />
    <PackageVersion Include="Microsoft.AspNetCore.TestHost" Version="10.0.10" />
    <PackageVersion Include="Microsoft.Data.SqlClient" Version="7.0.2" />
    <PackageVersion Include="Microsoft.Extensions.Caching.Hybrid" Version="10.8.0" />
    <PackageVersion Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="10.0.10" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Binder" Version="10.0.10" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="10.0.10" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.10" />
    <PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="10.8.0" />
    <PackageVersion Include="Microsoft.Extensions.Options" Version="10.0.10" />
    <PackageVersion Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.10" />
    <PackageVersion Include="Microsoft.Extensions.ServiceDiscovery" Version="10.8.0" />
    <PackageVersion Include="MessagePack" Version="3.1.8" />
    <PackageVersion Include="Microsoft.OpenApi" Version="2.10.0" />
    <PackageVersion Include="MySqlConnector" Version="2.6.1" />
    <PackageVersion Include="MSTest" Version="4.3.2" />
    <PackageVersion Include="NetArchTest.Rules" Version="1.3.2" />
    <PackageVersion Include="NSubstitute" Version="6.0.0" />
    <PackageVersion Include="OpenTelemetry.Api" Version="1.17.0" />
    <PackageVersion Include="OpenTelemetry.Api.ProviderBuilderExtensions" Version="1.17.0" />
    <PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.17.0" />
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.17.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.16.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Http" Version="1.16.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Runtime" Version="1.16.0" />
    <PackageVersion Include="Scalar.AspNetCore" Version="2.16.13" />
    <PackageVersion Include="Serilog" Version="4.4.0" />
    <PackageVersion Include="Serilog.AspNetCore" Version="10.0.0" />
    <PackageVersion Include="Serilog.Formatting.Compact" Version="3.0.0" />
    <PackageVersion Include="Serilog.Sinks.Async" Version="2.1.0" />
    <PackageVersion Include="Serilog.Sinks.Console" Version="6.1.1" />
    <PackageVersion Include="Testcontainers.MsSql" Version="4.13.0" />
    <PackageVersion Include="Testcontainers.MySql" Version="4.13.0" />
    <PackageVersion Include="ZiggyCreatures.FusionCache" Version="2.6.0" />
    <PackageVersion Include="ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis" Version="2.6.0" />
    <PackageVersion Include="ZiggyCreatures.FusionCache.OpenTelemetry" Version="2.6.0" />
    <PackageVersion Include="ZiggyCreatures.FusionCache.Serialization.SystemTextJson" Version="2.6.0" />
  </ItemGroup>
</Project>
```

Create `.editorconfig`:

```ini
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true

[*.cs]
indent_style = space
indent_size = 4
dotnet_diagnostic.IDE0005.severity = suggestion
csharp_style_namespace_declarations = file_scoped:warning
csharp_style_prefer_primary_constructors = true:suggestion

[*.{xml,json,yml,yaml}]
indent_style = space
indent_size = 2

[*.md]
trim_trailing_whitespace = false
```

Create `.gitignore`:

```gitignore
.vs/
.idea/
.vscode/
**/bin/
**/obj/
TestResults/
coverage/
BenchmarkDotNet.Artifacts/
*.user
*.suo
*.nupkg
*.snupkg
*.log
.aspire/
```

Create `LICENSE` with the unmodified MIT permission/warranty text and this copyright line:

```text
MIT License

Copyright (c) 2026 Full.NET Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

Create `THIRD-PARTY-NOTICES` with this initial inventory and retain each dependency's own notice when redistribution requires it:

```text
Full.NET Third-Party Notices

Dapper — Apache-2.0 — https://github.com/DapperLib/Dapper
DbUp — MIT — https://github.com/DbUp/DbUp
FusionCache — MIT — https://github.com/ZiggyCreatures/FusionCache
MessagePack-CSharp — MIT — https://github.com/MessagePack-CSharp/MessagePack-CSharp
Serilog and Serilog.Sinks.Async — Apache-2.0 — https://github.com/serilog
.NET / ASP.NET Core / Aspire — MIT — https://github.com/dotnet
OpenTelemetry .NET — Apache-2.0 — https://github.com/open-telemetry/opentelemetry-dotnet
Microsoft.Data.SqlClient — MIT — https://github.com/dotnet/SqlClient
MySqlConnector — MIT — https://github.com/mysql-net/MySqlConnector
Testcontainers for .NET — MIT — https://github.com/testcontainers/testcontainers-dotnet
MSTest — MIT — https://github.com/microsoft/testfx
NetArchTest — MIT — https://github.com/BenMorris/NetArchTest
Scalar — MIT — https://github.com/scalar/scalar
BenchmarkDotNet — MIT — https://github.com/dotnet/BenchmarkDotNet
```

- [ ] **Step 3: Scaffold the projects mechanically**

Run from the repository root:

```powershell
dotnet new sln --name Full.NET --format slnx
dotnet new classlib -n Full.NET.Abstractions -o src/BuildingBlocks/Full.NET.Abstractions
dotnet new classlib -n Full.NET.Modularity -o src/BuildingBlocks/Full.NET.Modularity
dotnet new classlib -n Full.NET.Data.Abstractions -o src/BuildingBlocks/Full.NET.Data.Abstractions
dotnet new classlib -n Full.NET.Data.Dapper -o src/BuildingBlocks/Full.NET.Data.Dapper
dotnet new classlib -n Full.NET.Serialization.MessagePack -o src/BuildingBlocks/Full.NET.Serialization.MessagePack
dotnet new classlib -n Full.NET.Migrations.DbUp -o src/BuildingBlocks/Full.NET.Migrations.DbUp
dotnet new classlib -n Full.NET.Caching.Fusion -o src/BuildingBlocks/Full.NET.Caching.Fusion
dotnet new classlib -n Full.NET.Hosting -o src/BuildingBlocks/Full.NET.Hosting
dotnet new classlib -n Full.NET.Modules.Tenancy -o src/Modules/Full.NET.Modules.Tenancy
dotnet new classlib -n Full.NET.Compatibility.AdminNet -o src/Compatibility/Full.NET.Compatibility.AdminNet
dotnet new web -n Full.NET.Host.Api -o src/Hosts/Full.NET.Host.Api
dotnet new worker -n Full.NET.Host.Worker -o src/Hosts/Full.NET.Host.Worker
dotnet new console -n Full.NET.Host.Migrator -o src/Hosts/Full.NET.Host.Migrator
dotnet new console -n Full.NET.AppHost -o src/Hosts/Full.NET.AppHost
dotnet new mstest -n Full.NET.UnitTests -o tests/Full.NET.UnitTests
dotnet new mstest -n Full.NET.ArchitectureTests -o tests/Full.NET.ArchitectureTests
dotnet new mstest -n Full.NET.IntegrationTests -o tests/Full.NET.IntegrationTests
dotnet new mstest -n Full.NET.CompatibilityTests -o tests/Full.NET.CompatibilityTests
dotnet new console -n Full.NET.Benchmarks -o benchmarks/Full.NET.Benchmarks
```

Delete generated `Class1.cs`, `UnitTest1.cs`, and the Worker template class. Change `Full.NET.AppHost.csproj` to:

```xml
<Project Sdk="Aspire.AppHost.Sdk/13.4.6">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.MySql" />
    <PackageReference Include="Aspire.Hosting.Redis" />
    <PackageReference Include="Aspire.Hosting.SqlServer" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Full.NET.Host.Api\Full.NET.Host.Api.csproj" />
    <ProjectReference Include="..\Full.NET.Host.Migrator\Full.NET.Host.Migrator.csproj" />
    <ProjectReference Include="..\Full.NET.Host.Worker\Full.NET.Host.Worker.csproj" />
  </ItemGroup>
</Project>
```

Normalize every test project to this SDK form before adding task-specific references:

```xml
<Project Sdk="MSTest.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
</Project>
```

Add all projects to `Full.NET.slnx` with explicit `dotnet sln Full.NET.slnx add <path>` calls. Use exactly this dependency direction:

| Project | Project references | Framework/package references |
|---|---|---|
| Abstractions | none | none |
| Modularity | Abstractions | `Microsoft.AspNetCore.App` |
| Data.Abstractions | Abstractions | none |
| Data.Dapper | Abstractions, Data.Abstractions | Dapper, SqlClient, MySqlConnector, Options.ConfigurationExtensions |
| Serialization.MessagePack | Data.Abstractions | MessagePack |
| Migrations.DbUp | Data.Abstractions | dbup-core, dbup-sqlserver, dbup-mysql |
| Caching.Fusion | Abstractions | FusionCache core/serializer/backplane/OTel, HybridCache, StackExchangeRedis cache |
| Hosting | Abstractions | `Microsoft.AspNetCore.App`, HTTP resilience, service discovery, OpenTelemetry, Serilog.AspNetCore, Compact formatter, Async/Console sinks |
| Modules.Tenancy | Abstractions, Modularity, Data.Abstractions, Caching.Fusion | `Microsoft.AspNetCore.App`, HybridCache, MessagePack attributes |
| Compatibility.AdminNet | Abstractions, Hosting | `Microsoft.AspNetCore.App` |
| Host.Api | Hosting, Modularity, Data.Dapper, Serialization.MessagePack, Migrations.DbUp, Caching.Fusion, Modules.Tenancy | OpenAPI, Scalar |
| Host.Worker | Hosting, Modularity, Data.Dapper, Serialization.MessagePack, Caching.Fusion, Modules.Tenancy | `Microsoft.AspNetCore.App` |
| Host.Migrator | Hosting, Modularity, Data.Dapper, Serialization.MessagePack, Migrations.DbUp, Modules.Tenancy | `Microsoft.AspNetCore.App` |
| AppHost | Api, Worker, Migrator | Aspire SQL Server/MySQL/Redis |
| UnitTests | all tested BuildingBlocks and Tenancy | DI, NSubstitute |
| ArchitectureTests | all production projects | NetArchTest.Rules |
| IntegrationTests | Api, Data.Dapper, Migrations.DbUp, Tenancy | MVC Testing, Dapper, Testcontainers SQL Server/MySQL |
| CompatibilityTests | Compatibility.AdminNet, Hosting | `Microsoft.AspNetCore.App`, DI |
| Benchmarks | Serialization.MessagePack, Modules.Tenancy | BenchmarkDotNet |

Use `<FrameworkReference Include="Microsoft.AspNetCore.App" />` where shown and unversioned `<PackageReference Include="..." />` entries because versions come from `Directory.Packages.props`. BuildingBlocks must never reference Modules or Hosts.

- [ ] **Step 4: Verify the empty solution builds**

Run:

```powershell
dotnet restore Full.NET.slnx
dotnet build Full.NET.slnx --no-restore
```

Expected: restore and build succeed with zero warnings and zero errors.

- [ ] **Step 5: Commit the skeleton**

```powershell
git add global.json Directory.Build.props Directory.Packages.props .editorconfig .gitignore LICENSE THIRD-PARTY-NOTICES Full.NET.slnx src tests benchmarks
git commit -m "build: scaffold Full.NET foundation"
```

### Task 2: Implement Result, messaging contracts, and dispatchers

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Results/ErrorType.cs`
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Results/Error.cs`
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Results/Result.cs`
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Results/PagedResult.cs`
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Messaging/ICommand.cs`
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Messaging/IQuery.cs`
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Messaging/ICommandTransaction.cs`
- Create: `src/BuildingBlocks/Full.NET.Modularity/Messaging/CommandDispatcher.cs`
- Create: `src/BuildingBlocks/Full.NET.Modularity/Messaging/QueryDispatcher.cs`
- Test: `tests/Full.NET.UnitTests/Results/ResultTests.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/DispatcherTests.cs`

**Interfaces:**
- Produces: `Result<T>`, `Error`, `PagedResult<T>`, `ICommand<TResult>`, `ITransactionalCommand<TResult>`, `IQuery<TResult>`, handler and dispatcher interfaces, `ICommandTransaction.ExecuteAsync<T>()`.

- [ ] **Step 1: Write failing Result tests**

```csharp
using Full.NET.Abstractions.Results;

namespace Full.NET.UnitTests.Results;

[TestClass]
public sealed class ResultTests
{
    [TestMethod]
    public void Success_contains_value_and_no_error()
    {
        var result = Result<string>.Success("ok");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("ok", result.Value);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void Failure_contains_error_and_no_value()
    {
        var error = new Error("tenant.not-found", "Tenant was not found.", ErrorType.NotFound);
        var result = Result<string>.Failure(error);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Value);
        Assert.AreEqual(error, result.Error);
    }
}
```

Run `dotnet test tests/Full.NET.UnitTests --filter FullyQualifiedName~ResultTests`. Expected: compile failure because the Result types do not exist.

- [ ] **Step 2: Implement the result types**

```csharp
namespace Full.NET.Abstractions.Results;

public enum ErrorType
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    BusinessRule,
    RateLimited,
    Unexpected
}

public sealed record Error(
    string Code,
    string Message,
    ErrorType Type,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);

public sealed class Result<T>
{
    private Result(bool isSuccess, T? value, Error? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }
    public T? Value { get; }
    public Error? Error { get; }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(Error error) => new(false, default, error);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total);
```

Run the focused test again. Expected: two tests pass.

- [ ] **Step 3: Write failing dispatcher tests**

Use these exact test-local types:

```csharp
private sealed record EchoCommand(string Value) : ITransactionalCommand<string>;
private sealed class EchoHandler : ICommandHandler<EchoCommand, string>
{
    public Task<Result<string>> HandleAsync(EchoCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(Result<string>.Success(command.Value));
}

private sealed class RecordingTransaction : ICommandTransaction
{
    public bool Executed { get; private set; }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        Executed = true;
        return await action(cancellationToken);
    }
}
```

The test builds a `ServiceCollection`, registers `EchoHandler`, `RecordingTransaction`, and `CommandDispatcher`, sends `new EchoCommand("value")`, then asserts success, value, and `Executed == true`. Run it and expect a compile failure for missing messaging types.

- [ ] **Step 4: Implement messaging contracts and dispatchers**

```csharp
namespace Full.NET.Abstractions.Messaging;

public interface ICommand<TResult>;
public interface ITransactionalCommand;
public interface ITransactionalCommand<TResult> : ICommand<TResult>, ITransactionalCommand;

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface ICommandDispatcher
{
    Task<Result<TResult>> SendAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>;
}

public interface IQuery<TResult>;

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

public interface IQueryDispatcher
{
    Task<Result<TResult>> SendAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResult>;
}

public interface ICommandTransaction
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}
```

```csharp
namespace Full.NET.Modularity.Messaging;

public sealed class CommandDispatcher(IServiceProvider services, ICommandTransaction? transaction = null)
    : ICommandDispatcher
{
    public Task<Result<TResult>> SendAsync<TCommand, TResult>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>
    {
        var handler = services.GetRequiredService<ICommandHandler<TCommand, TResult>>();

        if (command is ITransactionalCommand)
        {
            return (transaction ?? throw new InvalidOperationException("No command transaction is registered."))
                .ExecuteAsync(ct => handler.HandleAsync(command, ct), cancellationToken);
        }

        return handler.HandleAsync(command, cancellationToken);
    }
}

public sealed class QueryDispatcher(IServiceProvider services) : IQueryDispatcher
{
    public Task<Result<TResult>> SendAsync<TQuery, TResult>(
        TQuery query,
        CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResult> =>
        services.GetRequiredService<IQueryHandler<TQuery, TResult>>()
            .HandleAsync(query, cancellationToken);
}
```

Register both dispatchers as scoped services in `ModularityServiceCollectionExtensions.AddFullNetModularity()`.

- [ ] **Step 5: Run tests and commit**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests --filter "FullyQualifiedName~ResultTests|FullyQualifiedName~DispatcherTests"
dotnet build Full.NET.slnx
```

Expected: focused tests and full build pass with zero warnings.

```powershell
git add src/BuildingBlocks/Full.NET.Abstractions src/BuildingBlocks/Full.NET.Modularity tests/Full.NET.UnitTests
git commit -m "feat: add result and messaging primitives"
```

### Task 3: Implement source-generated JSON conventions, high-concurrency logging, ProblemDetails, and service defaults

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Hosting/Api/IApiResultMapper.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Api/StandardApiResultMapper.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Api/FullNetExceptionHandler.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Serialization/FullNetJsonOptionsExtensions.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Observability/LoggingOptions.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Observability/FullNetAsyncLogMonitor.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Observability/HostingLog.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Observability/ServiceDefaultsExtensions.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Observability/HealthEndpointExtensions.cs`
- Test: `tests/Full.NET.UnitTests/Hosting/StandardApiResultMapperTests.cs`
- Test: `tests/Full.NET.UnitTests/Hosting/FullNetJsonOptionsTests.cs`
- Test: `tests/Full.NET.UnitTests/Observability/FullNetAsyncLogMonitorTests.cs`

**Interfaces:**
- Consumes: `Result<T>`, `Error`, `ErrorType` from Task 2.
- Produces: `IApiResultMapper.Map<T>()`, `IApiResultMapper.MapException()`, `AddFullNetJson()`, `AddFullNetServiceDefaults()`, `UseFullNetRequestLogging()`, `MapFullNetHealthEndpoints()`, and observable bounded-log-buffer state.

- [ ] **Step 1: Write failing status mapping, JSON options, and async-buffer tests**

Create tests that call `StandardApiResultMapper.Map()` with `Result<string>.Failure(...)` and assert the returned object implements `IStatusCodeHttpResult` with these exact mappings: Validation 400, Unauthorized 401, Forbidden 403, NotFound 404, Conflict 409, BusinessRule 422, RateLimited 429, Unexpected 500. Also assert success maps to status 200 and carries the raw string value.

Run `dotnet test tests/Full.NET.UnitTests --filter FullyQualifiedName~StandardApiResultMapperTests`. Expected: compile failure for the missing mapper.

In `FullNetJsonOptionsTests`, build a service collection, call `AddFullNetJson()`, resolve `IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>`, and assert the configured `JsonSerializerOptions` uses `JsonSerializerDefaults.Web` camel-case behavior. The test must also insert a test source-generated context into `TypeInfoResolverChain`, serialize a test DTO through its generated `JsonTypeInfo`, and assert `{"value":"ok"}`.

In `FullNetAsyncLogMonitorTests`, use a fake `IAsyncLogEventSinkInspector` with `Count = 25`, `BufferSize = 100`, and `DroppedMessagesCount = 3`; call `StartMonitoring()`, assert the snapshot contains those values, call `StopMonitoring()`, and assert the snapshot returns zeros. Expected before implementation: compile failure for the missing JSON extension and monitor.

- [ ] **Step 2: Implement the API mapper**

```csharp
namespace Full.NET.Hosting.Api;

public interface IApiResultMapper
{
    IResult Map<T>(Result<T> result, HttpContext httpContext);
    IResult MapException(Exception exception, HttpContext httpContext);
}

public sealed class StandardApiResultMapper : IApiResultMapper
{
    public IResult Map<T>(Result<T> result, HttpContext httpContext)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        var error = result.Error ?? new Error(
            "common.unexpected",
            "An unexpected error occurred.",
            ErrorType.Unexpected);

        var problem = new ProblemDetails
        {
            Status = ToStatusCode(error.Type),
            Title = error.Message,
            Type = $"https://full.net/errors/{error.Code}"
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;
        if (error.ValidationErrors is not null)
        {
            problem.Extensions["errors"] = error.ValidationErrors;
        }

        return Results.Problem(problem);
    }

    public IResult MapException(Exception exception, HttpContext httpContext) =>
        Map(Result<object?>.Failure(new Error(
            "common.unexpected",
            "An unexpected error occurred.",
            ErrorType.Unexpected)), httpContext);

    public static int ToStatusCode(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorType.RateLimited => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status500InternalServerError
    };
}
```

- [ ] **Step 3: Implement JSON conventions, bounded structured logging, exception handling, and eShop-style service defaults**

`AddFullNetJson()` calls `ConfigureHttpJsonOptions()` and starts from ASP.NET Core's Web defaults. It does not register Newtonsoft.Json and does not create `JsonSerializerOptions` per request. Module-specific generated contexts are inserted by each module in Task 9.

Create the exact logging options and observable snapshot contract:

```csharp
public sealed class LoggingOptions
{
    public const string SectionName = "FullNet:Logging";
    public int AsyncBufferSize { get; set; } = 10_000;
    public bool BlockWhenFull { get; set; }
}

public readonly record struct AsyncLogBufferSnapshot(
    int Count,
    int BufferSize,
    long DroppedMessagesCount);
```

`FullNetAsyncLogMonitor` implements `IAsyncLogEventSinkMonitor` and `IDisposable`. `StartMonitoring()` stores the supplied inspector with `Volatile.Write`; `StopMonitoring()` clears only the same inspector; `Snapshot` returns zeros when no sink is active and otherwise copies `Count`, `BufferSize`, and `DroppedMessagesCount`. Register observable gauges named `fullnet.logging.queue.depth`, `fullnet.logging.queue.capacity`, and `fullnet.logging.events.dropped` on a `Meter("Full.NET.Logging")`, and dispose the meter at shutdown.

Configure Serilog once from `AddFullNetServiceDefaults()`:

```csharp
var loggingOptions = new LoggingOptions();
builder.Configuration.GetSection(LoggingOptions.SectionName).Bind(loggingOptions);
if (loggingOptions.AsyncBufferSize <= 0)
{
    throw new OptionsValidationException(
        LoggingOptions.SectionName,
        typeof(LoggingOptions),
        ["AsyncBufferSize must be greater than zero."]);
}

var logMonitor = new FullNetAsyncLogMonitor();
builder.Services.AddSingleton(logMonitor);
builder.Services.AddSerilog((services, configuration) => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", builder.Environment.ApplicationName)
    .WriteTo.Async(
        sink => sink.Console(new CompactJsonFormatter()),
        bufferSize: loggingOptions.AsyncBufferSize,
        blockWhenFull: loggingOptions.BlockWhenFull,
        monitor: logMonitor));
```

The default `BlockWhenFull` remains `false` so a failed sink cannot stop request processing. Buffer usage and dropped counts are operational alerts. Do not use this best-effort pipeline for later security/business audit persistence.

Create source-generated exception logging:

```csharp
internal static partial class HostingLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Error,
        Message = "Unhandled exception for {RequestPath}")]
    public static partial void UnhandledException(
        ILogger logger,
        Exception exception,
        string requestPath);
}
```

`FullNetExceptionHandler.TryHandleAsync()` calls `HostingLog.UnhandledException()`, maps the exception through `IApiResultMapper.MapException()`, executes the returned `IResult`, and returns `true`. It never logs request/response bodies or secrets.

`AddFullNetServiceDefaults()` must register:

```csharp
services.AddProblemDetails();
services.AddExceptionHandler<FullNetExceptionHandler>();
services.AddFullNetJson();
services.AddHealthChecks();
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("Full.NET.Logging")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation())
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation());
services.AddHttpClient().AddStandardResilienceHandler();
services.AddServiceDiscovery();
services.AddSingleton<IApiResultMapper, StandardApiResultMapper>();
```

If `OTEL_EXPORTER_OTLP_ENDPOINT` is present, add the OTLP exporter. `MapFullNetHealthEndpoints()` maps `/health/live`, `/health/ready`, and `/health/startup`; use health-check tags to keep liveness independent from external dependencies.

`UseFullNetRequestLogging()` wraps `UseSerilogRequestLogging()` and emits one completion event per request. Enrich it with request host, `Activity.Current.TraceId`, tenant ID from `HttpContext.Items["FullNet.TenantId"]` when available, and result status; do not log bodies, cookies, authorization headers, or query-string secrets. Reading the request item is intentional because tenant middleware clears its scoped accessor before the outer request logger writes the completion event.

- [ ] **Step 4: Run focused tests and commit**

```powershell
dotnet test tests/Full.NET.UnitTests --filter "FullyQualifiedName~StandardApiResultMapperTests|FullyQualifiedName~FullNetJsonOptionsTests|FullyQualifiedName~FullNetAsyncLogMonitorTests"
dotnet build Full.NET.slnx
git add src/BuildingBlocks/Full.NET.Hosting tests/Full.NET.UnitTests/Hosting tests/Full.NET.UnitTests/Observability
git commit -m "feat: add JSON and observability service defaults"
```

### Task 4: Implement explicit modules and tenant context

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Tenancy/TenantContext.cs`
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Tenancy/ICurrentTenant.cs`
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Tenancy/CurrentTenantAccessor.cs`
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Time/IClock.cs`
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Time/SystemClock.cs`
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Ids/IIdGenerator.cs`
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Ids/GuidV7IdGenerator.cs`
- Create: `src/BuildingBlocks/Full.NET.Modularity/Modules/IFullNetModule.cs`
- Create: `src/BuildingBlocks/Full.NET.Modularity/Modules/FullNetModuleRegistry.cs`
- Create: `src/BuildingBlocks/Full.NET.Modularity/Modules/ModuleExtensions.cs`
- Test: `tests/Full.NET.UnitTests/Tenancy/CurrentTenantAccessorTests.cs`
- Test: `tests/Full.NET.UnitTests/Modularity/ModuleRegistryTests.cs`

**Interfaces:**
- Produces: mutable scoped `CurrentTenantAccessor` implementing read-only `ICurrentTenant`, `IFullNetModule`, explicit module registry with dependency ordering.

- [ ] **Step 1: Write failing tenant-context tests**

Test these behaviors:

```csharp
var accessor = new CurrentTenantAccessor();
Assert.IsFalse(accessor.IsAvailable);

var tenantId = Guid.CreateVersion7();
accessor.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
Assert.IsTrue(accessor.IsAvailable);
Assert.AreEqual(tenantId, accessor.Id);
Assert.IsFalse(accessor.IsHost);

accessor.SetHost();
Assert.IsTrue(accessor.IsHost);
Assert.IsNull(accessor.Id);

accessor.Clear();
Assert.IsFalse(accessor.IsAvailable);
```

Run the focused test and expect a compile failure.

- [ ] **Step 2: Implement tenant, time, and ID primitives**

```csharp
public sealed record TenantContext(Guid Id, string Identifier, string Name);

public interface ICurrentTenant
{
    bool IsAvailable { get; }
    bool IsHost { get; }
    Guid? Id { get; }
    string? Identifier { get; }
    string? Name { get; }
}

public sealed class CurrentTenantAccessor : ICurrentTenant
{
    private TenantContext? _tenant;
    public bool IsAvailable => IsHost || _tenant is not null;
    public bool IsHost { get; private set; }
    public Guid? Id => _tenant?.Id;
    public string? Identifier => _tenant?.Identifier;
    public string? Name => _tenant?.Name;

    public void SetTenant(TenantContext tenant) { _tenant = tenant; IsHost = false; }
    public void SetHost() { _tenant = null; IsHost = true; }
    public void Clear() { _tenant = null; IsHost = false; }
}

public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
public interface IIdGenerator { Guid NewId(); }
public sealed class GuidV7IdGenerator : IIdGenerator { public Guid NewId() => Guid.CreateVersion7(); }
```

- [ ] **Step 3: Write failing module ordering tests**

Create three test modules: `BaseModule`, `DependentModule` depending on Base, and `CycleModule` depending on itself. Assert the registry orders Base before Dependent and throws `InvalidOperationException` containing `cycle` for CycleModule.

- [ ] **Step 4: Implement the module contract and registry**

```csharp
public interface IFullNetModule
{
    string Name { get; }
    IReadOnlyCollection<Type> Dependencies { get; }
    void AddServices(IServiceCollection services, IConfiguration configuration);
    void MapEndpoints(IEndpointRouteBuilder endpoints);
    Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

`FullNetModuleRegistry` stores explicitly added module instances, performs a depth-first topological sort, and throws on a temporary-mark revisit. `AddFullNetModule<TModule>()` creates the module, records it, and calls its `AddServices()`. `MapFullNetModules()` resolves the registry and calls `MapEndpoints()` in dependency order. Do not scan assemblies.

- [ ] **Step 5: Verify and commit**

```powershell
dotnet test tests/Full.NET.UnitTests --filter "FullyQualifiedName~CurrentTenantAccessorTests|FullyQualifiedName~ModuleRegistryTests"
dotnet build Full.NET.slnx
git add src/BuildingBlocks/Full.NET.Abstractions src/BuildingBlocks/Full.NET.Modularity tests/Full.NET.UnitTests
git commit -m "feat: add modularity and tenant context"
```

### Task 5: Implement Dapper data abstractions, scope enforcement, and transactions

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.Abstractions/DatabaseProvider.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Abstractions/DatabaseOptions.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Abstractions/SqlDataScope.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Abstractions/SqlStatement.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Abstractions/IQueryExecutor.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Abstractions/ICommandExecutor.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/DbConnectionFactory.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/DbSession.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/SqlScopeGuard.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/DapperSqlExecutor.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/DapperCommandTransaction.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/ServiceCollectionExtensions.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/Properties/AssemblyInfo.cs`
- Test: `tests/Full.NET.UnitTests/Data/SqlScopeGuardTests.cs`

**Interfaces:**
- Consumes: `ICurrentTenant` and `ICommandTransaction`.
- Produces: `IQueryExecutor.QueryAsync<T>()`, `QuerySingleOrDefaultAsync<T>()`, `ICommandExecutor.ExecuteAsync()`, and the scoped Dapper transaction implementation.

- [ ] **Step 1: Write failing SQL-scope tests**

Cover these exact cases:

```csharp
var tenantStatement = new SqlStatement(
    "tenant.read",
    "select * from fn_example where TenantId = @TenantId",
    SqlDataScope.TenantRequired);

Assert.ThrowsException<TenantContextMissingException>(() =>
    SqlScopeGuard.Validate(tenantStatement, new CurrentTenantAccessor()));

var accessor = new CurrentTenantAccessor();
accessor.SetTenant(new TenantContext(Guid.CreateVersion7(), "acme", "Acme"));
SqlScopeGuard.Validate(tenantStatement, accessor);

var missingPredicate = tenantStatement with { Text = "select * from fn_example" };
Assert.ThrowsException<TenantScopeViolationException>(() =>
    SqlScopeGuard.Validate(missingPredicate, accessor));

var hostStatement = new SqlStatement("host.read", "select 1", SqlDataScope.HostOnly);
Assert.ThrowsException<HostContextRequiredException>(() =>
    SqlScopeGuard.Validate(hostStatement, accessor));
```

Run the focused tests. Expected: compile failure for missing data types.

- [ ] **Step 2: Implement data contracts**

```csharp
namespace Full.NET.Data.Abstractions;

public enum DatabaseProvider { SqlServer, MySql }

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public DatabaseProvider Provider { get; set; }
    public string ConnectionName { get; set; } = "fullnet";
    public string ConnectionString { get; set; } = string.Empty;
    public int CommandTimeoutSeconds { get; set; } = 30;
}

public enum SqlDataScope { Global, TenantRequired, HostOnly }

public sealed record SqlStatement(string Name, string Text, SqlDataScope Scope);

public interface IQueryExecutor
{
    Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default);
}

public interface ICommandExecutor
{
    Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default);
}

public sealed class TenantContextMissingException(string statementName)
    : InvalidOperationException($"Tenant context is required for SQL statement '{statementName}'.");
public sealed class TenantScopeViolationException(string statementName)
    : InvalidOperationException($"SQL statement '{statementName}' must contain @TenantId.");
public sealed class HostContextRequiredException(string statementName)
    : InvalidOperationException($"Host context is required for SQL statement '{statementName}'.");
```

- [ ] **Step 3: Implement the guard and connection/session lifecycle**

```csharp
internal static class SqlScopeGuard
{
    public static void Validate(SqlStatement statement, ICurrentTenant tenant)
    {
        if (statement.Scope == SqlDataScope.TenantRequired)
        {
            if (tenant.Id is null)
            {
                throw new TenantContextMissingException(statement.Name);
            }

            if (!statement.Text.Contains("@TenantId", StringComparison.OrdinalIgnoreCase))
            {
                throw new TenantScopeViolationException(statement.Name);
            }
        }

        if (statement.Scope == SqlDataScope.HostOnly && !tenant.IsHost)
        {
            throw new HostContextRequiredException(statement.Name);
        }
    }
}
```

`DbConnectionFactory.Create()` returns `SqlConnection` for SQL Server and `MySqlConnection` for MySQL. `DbSession` is scoped, lazily opens one `DbConnection`, owns an optional `DbTransaction`, and implements `IAsyncDisposable`. It exposes internal `GetOpenConnectionAsync()`, `BeginAsync()`, `CommitAsync()`, and `RollbackAsync()` methods only to the Dapper assembly.

- [ ] **Step 4: Implement the Dapper executor**

Use one scoped `DapperSqlExecutor` for both public interfaces:

```csharp
private DynamicParameters CreateParameters(SqlStatement statement, object? values)
{
    SqlScopeGuard.Validate(statement, currentTenant);
    var parameters = values is null ? new DynamicParameters() : new DynamicParameters(values);
    if (statement.Scope == SqlDataScope.TenantRequired)
    {
        parameters.Add("TenantId", currentTenant.Id!.Value);
    }
    return parameters;
}
```

Each method creates a Dapper `CommandDefinition` with statement text, parameters, the current `DbSession.Transaction`, configured timeout, and cancellation token. `QueryAsync<T>()` materializes to an array before returning. `ExecuteAsync()` returns affected rows. Log statement name, provider, and elapsed milliseconds; never log parameter values.

- [ ] **Step 5: Implement transactional command execution**

```csharp
public sealed class DapperCommandTransaction(DbSession session) : ICommandTransaction
{
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (session.HasTransaction)
        {
            return await action(cancellationToken);
        }

        await session.BeginAsync(cancellationToken);
        try
        {
            var result = await action(cancellationToken);
            await session.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await session.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
```

`AddFullNetDapper()` binds `DatabaseOptions`, then post-configures `ConnectionString` from `configuration.GetConnectionString(options.ConnectionName)` when the direct value is empty. Register `DbConnectionFactory`, `DbSession`, `DapperSqlExecutor` as both executor interfaces, and `DapperCommandTransaction` as `ICommandTransaction`. Validate on start that `ConnectionName` and the final connection string are non-empty.

Keep `SqlScopeGuard`, `DbSession`, and provider details internal. Grant only the unit-test assembly access:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Full.NET.UnitTests")]
```

- [ ] **Step 6: Run tests and commit**

```powershell
dotnet test tests/Full.NET.UnitTests --filter FullyQualifiedName~SqlScopeGuardTests
dotnet build Full.NET.slnx
git add src/BuildingBlocks/Full.NET.Data.Abstractions src/BuildingBlocks/Full.NET.Data.Dapper tests/Full.NET.UnitTests/Data
git commit -m "feat: add scoped Dapper data access"
```

### Task 6: Add DbUp migrations and verify both database providers

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/IDatabaseMigrationRunner.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/DbUpMigrationRunner.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/MigrationAssembly.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/ServiceCollectionExtensions.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/001_Foundation.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/001_Foundation.sql`
- Modify: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Full.NET.Migrations.DbUp.csproj`
- Test: `tests/Full.NET.IntegrationTests/Migrations/SqlServerMigrationTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Migrations/MySqlMigrationTests.cs`

**Interfaces:**
- Consumes: `DatabaseOptions`.
- Produces: `IDatabaseMigrationRunner.MigrateAsync()` and the initial tenant/outbox schema.

- [ ] **Step 1: Write failing container migration tests**

For SQL Server, use:

```csharp
private readonly MsSqlContainer _container = new MsSqlBuilder()
    .WithPassword("FullNet_Test!123")
    .Build();
```

For MySQL, use:

```csharp
private readonly MySqlContainer _container = new MySqlBuilder()
    .WithDatabase("fullnet")
    .WithUsername("fullnet")
    .WithPassword("FullNet_Test!123")
    .Build();
```

Each test starts its container, runs the migration twice, then uses Dapper to assert `fn_tenant_tenant`, `fn_outbox_message`, and the DbUp journal table exist. Query provider metadata and assert the Outbox table contains `SchemaVersion`, `ContentType`, `TenantId`, and `TraceId`; assert `Payload` is `varbinary(max)` on SQL Server and `longblob` on MySQL, never a text/JSON column. The second run must report zero newly executed scripts. Mark both classes `[TestClass]` and implement `[TestInitialize]`/`[TestCleanup]` to start and dispose containers.

Run `dotnet test tests/Full.NET.IntegrationTests --filter FullyQualifiedName~MigrationTests`. Expected: compile failure because the migration runner and scripts do not exist. If Docker is unavailable, stop and report that prerequisite rather than skipping the tests.

- [ ] **Step 2: Create the SQL Server migration**

Create an idempotent script with these exact tables and indexes:

```sql
IF OBJECT_ID(N'dbo.fn_tenant_tenant', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_tenant_tenant
    (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        Identifier nvarchar(64) NOT NULL,
        Name nvarchar(128) NOT NULL,
        Domain nvarchar(255) NOT NULL,
        IsActive bit NOT NULL,
        CreatedAt datetimeoffset(7) NOT NULL,
        UpdatedAt datetimeoffset(7) NULL,
        Version int NOT NULL CONSTRAINT DF_fn_tenant_tenant_Version DEFAULT (1)
    );
    CREATE UNIQUE INDEX UX_fn_tenant_tenant_Identifier ON dbo.fn_tenant_tenant(Identifier);
    CREATE UNIQUE INDEX UX_fn_tenant_tenant_Domain ON dbo.fn_tenant_tenant(Domain);
END;

IF OBJECT_ID(N'dbo.fn_outbox_message', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_outbox_message
    (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        Type nvarchar(256) NOT NULL,
        SchemaVersion int NOT NULL,
        ContentType nvarchar(128) NOT NULL,
        TenantId uniqueidentifier NULL,
        TraceId varchar(32) NULL,
        Payload varbinary(max) NOT NULL,
        OccurredAt datetimeoffset(7) NOT NULL,
        ProcessedAt datetimeoffset(7) NULL,
        NextAttemptAt datetimeoffset(7) NULL,
        Attempts int NOT NULL CONSTRAINT DF_fn_outbox_message_Attempts DEFAULT (0),
        LockId uniqueidentifier NULL,
        LockedUntil datetimeoffset(7) NULL,
        Error nvarchar(2000) NULL
    );
    CREATE INDEX IX_fn_outbox_message_Pending
        ON dbo.fn_outbox_message(ProcessedAt, NextAttemptAt, LockedUntil, OccurredAt);
END;
```

- [ ] **Step 3: Create the MySQL migration**

```sql
CREATE TABLE IF NOT EXISTS fn_tenant_tenant
(
    Id char(36) NOT NULL PRIMARY KEY,
    Identifier varchar(64) NOT NULL,
    Name varchar(128) NOT NULL,
    Domain varchar(255) NOT NULL,
    IsActive boolean NOT NULL,
    CreatedAt datetime(6) NOT NULL,
    UpdatedAt datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    UNIQUE KEY UX_fn_tenant_tenant_Identifier (Identifier),
    UNIQUE KEY UX_fn_tenant_tenant_Domain (Domain)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_outbox_message
(
    Id char(36) NOT NULL PRIMARY KEY,
    Type varchar(256) NOT NULL,
    SchemaVersion int NOT NULL,
    ContentType varchar(128) NOT NULL,
    TenantId char(36) NULL,
    TraceId varchar(32) NULL,
    Payload longblob NOT NULL,
    OccurredAt datetime(6) NOT NULL,
    ProcessedAt datetime(6) NULL,
    NextAttemptAt datetime(6) NULL,
    Attempts int NOT NULL DEFAULT 0,
    LockId char(36) NULL,
    LockedUntil datetime(6) NULL,
    Error varchar(2000) NULL,
    KEY IX_fn_outbox_message_Pending (ProcessedAt, NextAttemptAt, LockedUntil, OccurredAt)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
```

- [ ] **Step 4: Implement the migration runner**

Embed all migration SQL:

```xml
<ItemGroup>
  <EmbeddedResource Include="Migrations\**\*.sql" />
</ItemGroup>
```

Implement the interface and result in `IDatabaseMigrationRunner.cs`:

```csharp
public sealed record MigrationResult(bool Successful, int ExecutedScriptCount);

public interface IDatabaseMigrationRunner
{
    Task<MigrationResult> MigrateAsync(CancellationToken cancellationToken = default);
}
```

Implement the runner with this control flow:

```csharp
public Task<MigrationResult> MigrateAsync(CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();
    var options = databaseOptions.Value;
    var providerSegment = options.Provider == DatabaseProvider.SqlServer
        ? ".Migrations.SqlServer."
        : ".Migrations.MySql.";

    UpgradeEngineBuilder builder;
    if (options.Provider == DatabaseProvider.SqlServer)
    {
        EnsureDatabase.For.SqlDatabase(options.ConnectionString);
        builder = DeployChanges.To.SqlDatabase(options.ConnectionString);
    }
    else
    {
        EnsureDatabase.For.MySqlDatabase(options.ConnectionString);
        builder = DeployChanges.To.MySqlDatabase(options.ConnectionString);
    }

    var upgrader = builder
        .WithScriptsEmbeddedInAssembly(
            MigrationAssembly.Value,
            name => name.Contains(providerSegment, StringComparison.Ordinal))
        .LogTo(loggerFactory)
        .Build();

    var result = upgrader.PerformUpgrade();
    if (!result.Successful)
    {
        throw new InvalidOperationException("Database migration failed.", result.Error);
    }

    return Task.FromResult(new MigrationResult(true, result.Scripts.Count()));
}
```

`MigrationAssembly.Value` is `typeof(MigrationAssembly).Assembly`. `AddFullNetMigrations()` registers the runner as a singleton and consumes the same validated `DatabaseOptions`. Return `ExecutedScriptCount` so the idempotence test can assert the second count is zero.

- [ ] **Step 5: Run provider tests and commit**

```powershell
dotnet test tests/Full.NET.IntegrationTests --filter "FullyQualifiedName~SqlServerMigrationTests|FullyQualifiedName~MySqlMigrationTests"
dotnet build Full.NET.slnx
git add src/BuildingBlocks/Full.NET.Migrations.DbUp tests/Full.NET.IntegrationTests/Migrations
git commit -m "feat: add SQL Server and MySQL migrations"
```

### Task 7: Implement secure MessagePack serialization, transactional tenant provisioning, and binary Outbox writes

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.Abstractions/IOutboxWriter.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Abstractions/IIntegrationEventSerializer.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/OutboxMessage.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperOutboxWriter.cs`
- Create: `src/BuildingBlocks/Full.NET.Serialization.MessagePack/MessagePackIntegrationEventSerializer.cs`
- Create: `src/BuildingBlocks/Full.NET.Serialization.MessagePack/ServiceCollectionExtensions.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Contracts/TenantSummary.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Contracts/ITenantProvisioningService.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Contracts/TenantProvisionedIntegrationEvent.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Domain/Tenant.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantSql.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Features/ProvisionTenant/Command.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Features/ProvisionTenant/Handler.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Features/ProvisionTenant/TenantProvisioningService.cs`
- Test: `tests/Full.NET.IntegrationTests/Tenancy/TenantProvisioningTests.cs`
- Test: `tests/Full.NET.UnitTests/Serialization/MessagePackIntegrationEventSerializerTests.cs`

**Interfaces:**
- Consumes: transactional command dispatcher, Dapper executors, clock, ID generator.
- Produces: secure `IIntegrationEventSerializer`, public `ITenantProvisioningService`, versioned `TenantProvisionedIntegrationEvent`, and atomic binary tenant/outbox persistence; the Command and Handler stay internal.

- [ ] **Step 1: Write a failing atomic provisioning test for both providers**

For each Testcontainer provider: migrate, create a service provider, set `CurrentTenantAccessor.SetHost()`, dispatch:

```csharp
var result = await provisioning.ProvisionAsync(
    new ProvisionTenantRequest("acme", "Acme Corporation", "acme.localhost"),
    cancellationToken);
```

Assert success, normalized identifier/domain, one tenant row, and one unprocessed Outbox row with type `fullnet.tenancy.tenant-provisioned`, schema version `1`, content type `application/x-msgpack`, and a non-empty binary payload. Deserialize that payload with `IIntegrationEventSerializer` and assert the tenant ID/domain. Dispatch the same identifier again and assert a Conflict result with code `tenancy.identifier-exists` and no second tenant/outbox row.

Run the focused test. Expected: compile failure for missing tenancy types.

- [ ] **Step 2: Implement the secure MessagePack codec and binary Outbox writer**

```csharp
public interface IIntegrationEventSerializer
{
    string ContentType { get; }
    byte[] Serialize<TEvent>(TEvent payload);
    TEvent Deserialize<TEvent>(ReadOnlyMemory<byte> payload);
}

public interface IOutboxWriter
{
    Task AddAsync<TEvent>(
        string eventType,
        int schemaVersion,
        TEvent payload,
        CancellationToken cancellationToken = default);
}

public sealed record OutboxMessage(
    Guid Id,
    string Type,
    int SchemaVersion,
    string ContentType,
    Guid? TenantId,
    string? TraceId,
    byte[] Payload,
    DateTimeOffset OccurredAt);
```

Implement `MessagePackIntegrationEventSerializer` with one immutable option set and no global mutable default:

```csharp
public sealed class MessagePackIntegrationEventSerializer : IIntegrationEventSerializer
{
    private static readonly MessagePackSerializerOptions SerializerOptions =
        MessagePackSerializerOptions.Standard
            .WithSecurity(MessagePackSecurity.UntrustedData);

    public string ContentType => "application/x-msgpack";

    public byte[] Serialize<TEvent>(TEvent payload) =>
        MessagePackSerializer.Serialize(payload, SerializerOptions);

    public TEvent Deserialize<TEvent>(ReadOnlyMemory<byte> payload) =>
        MessagePackSerializer.Deserialize<TEvent>(payload, SerializerOptions);
}
```

`AddFullNetMessagePack()` registers that concrete type once as `IIntegrationEventSerializer`. Do not set `MessagePackSerializer.DefaultOptions`, do not add Typeless/Contractless resolvers, and do not enable LZ4 in M1.

Add a unit test with a `[MessagePackObject]` fixture whose members use integer keys. Assert round-trip equality and `ContentType`; the architecture source rule in Task 11 verifies the production configuration and forbidden resolver APIs.

`DapperOutboxWriter.AddAsync()` rejects empty event type and schema versions below 1, serializes through `IIntegrationEventSerializer`, generates UUID v7, captures `ICurrentTenant.Id` when tenant context exists, captures `Activity.Current?.TraceId`, uses `IClock.UtcNow`, and executes this `Global` statement through `ICommandExecutor`:

```sql
INSERT INTO fn_outbox_message
    (Id, Type, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAt, Attempts)
VALUES
    (@Id, @Type, @SchemaVersion, @ContentType, @TenantId, @TraceId, @Payload, @OccurredAt, 0)
```

Because the command dispatcher opened the scoped transaction, the tenant and Outbox inserts share the same `DbSession.Transaction`.

- [ ] **Step 3: Implement tenant contracts, domain normalization, and SQL**

```csharp
public sealed record TenantSummary(
    Guid Id,
    string Identifier,
    string Name,
    string Domain,
    bool IsActive,
    int Version);

[MessagePackObject]
public sealed record TenantProvisionedIntegrationEvent(
    [property: Key(0)] Guid TenantId,
    [property: Key(1)] string Identifier,
    [property: Key(2)] string Domain);

public sealed record ProvisionTenantRequest(string Identifier, string Name, string Domain);

public interface ITenantProvisioningService
{
    Task<Result<TenantSummary>> ProvisionAsync(
        ProvisionTenantRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed record Tenant(
    Guid Id,
    string Identifier,
    string Name,
    string Domain,
    bool IsActive,
    DateTimeOffset CreatedAt,
    int Version);
```

`TenantSql` defines four named statements: `FindByIdentifier` and `FindByDomain` as `Global`, `Insert` as `HostOnly`, and `GetCurrent` as `TenantRequired`. `GetCurrent` must contain `WHERE Id = @TenantId AND IsActive = 1`; `Insert` supplies every non-null column explicitly.

- [ ] **Step 4: Implement the provisioning command and handler**

```csharp
internal sealed record ProvisionTenantCommand(string Identifier, string Name, string Domain)
    : ITransactionalCommand<TenantSummary>;
```

The handler trims values, lowercases identifier/domain invariantly, validates identifier against `^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$`, requires a non-empty name of at most 128 characters, and requires a domain of at most 255 characters. Return `ErrorType.Validation` with field errors when invalid. Query identifier and domain before insert; return `tenancy.identifier-exists` or `tenancy.domain-exists` Conflict for the matching duplicate. On success, insert the tenant, add event type `fullnet.tenancy.tenant-provisioned` with schema version `1` to Outbox, and return `TenantSummary`.

`TenantProvisioningService` is public only through `ITenantProvisioningService`; it converts the request into the internal command and calls `ICommandDispatcher`. Register the implementation in `TenancyModule`. Hosts and integration tests must use this Contract Service, never the internal command or handler.

- [ ] **Step 5: Prove rollback atomicity**

Add a test-only `IOutboxWriter` that throws. Replace the real writer, call `ITenantProvisioningService.ProvisionAsync()` for a new tenant, assert the exception is observed, then query the database and assert neither the tenant nor Outbox row exists. This verifies the command transaction wraps both operations.

- [ ] **Step 6: Run both providers and commit**

```powershell
dotnet test tests/Full.NET.UnitTests --filter FullyQualifiedName~MessagePackIntegrationEventSerializerTests
dotnet test tests/Full.NET.IntegrationTests --filter FullyQualifiedName~TenantProvisioningTests
dotnet build Full.NET.slnx
git add src/BuildingBlocks/Full.NET.Data.Abstractions src/BuildingBlocks/Full.NET.Data.Dapper/Outbox src/BuildingBlocks/Full.NET.Serialization.MessagePack src/Modules/Full.NET.Modules.Tenancy tests/Full.NET.UnitTests/Serialization tests/Full.NET.IntegrationTests/Tenancy
git commit -m "feat: add transactional MessagePack outbox"
```

### Task 8: Register FusionCache as the only cache and expose both abstractions

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Caching.Fusion/CacheOptions.cs`
- Create: `src/BuildingBlocks/Full.NET.Caching.Fusion/CacheKeyBuilder.cs`
- Create: `src/BuildingBlocks/Full.NET.Caching.Fusion/ServiceCollectionExtensions.cs`
- Test: `tests/Full.NET.UnitTests/Caching/FusionCacheRegistrationTests.cs`
- Test: `tests/Full.NET.UnitTests/Caching/CacheKeyBuilderTests.cs`

**Interfaces:**
- Produces: `AddFullNetCaching()`, one shared FusionCache instance exposed as `IFusionCache` and `HybridCache`, tenant-safe key/tag generation.

- [ ] **Step 1: Write failing cache registration and key tests**

Assert:

```csharp
var services = new ServiceCollection();
services.AddLogging();
services.AddFullNetCaching(new ConfigurationBuilder().Build(), "Development");
await using var provider = services.BuildServiceProvider();

var fusion = provider.GetRequiredService<IFusionCache>();
var hybrid = provider.GetRequiredService<HybridCache>();
var adapter = (FusionHybridCache)hybrid;

Assert.AreSame(fusion, adapter.InnerFusionCache);
```

Also assert `CacheKeyBuilder.ForTenant("prod", tenantId, "identity", "permissions", userId, "v1")` returns exactly:

```text
fullnet:prod:{tenantId}:identity:permissions:{userId}:v1
```

and throws when a tenant key is requested with an empty tenant ID. Run focused tests and expect compile failure.

- [ ] **Step 2: Implement options and tenant-safe keys**

```csharp
public sealed class CacheOptions
{
    public const string SectionName = "Cache";
    public TimeSpan DefaultDuration { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan Jitter { get; set; } = TimeSpan.FromSeconds(30);
    public string? RedisConnectionString { get; set; }
}

public static class CacheKeyBuilder
{
    public static string ForTenant(
        string environment,
        Guid tenantId,
        string module,
        string resource,
        object id,
        string version)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        return $"fullnet:{environment.ToLowerInvariant()}:{tenantId:D}:{module}:{resource}:{id}:{version}";
    }

    public static string ForGlobal(string environment, string module, string resource, object id, string version) =>
        $"fullnet:{environment.ToLowerInvariant()}:host:{module}:{resource}:{id}:{version}";

    public static string TenantTag(Guid tenantId) => $"tenant:{tenantId:D}";
    public static string DomainTag(string domain) => $"tenancy:domain:{domain.ToLowerInvariant()}";
}
```

- [ ] **Step 3: Implement FusionCache registration**

Bind `CacheOptions`. If `Cache:RedisConnectionString` is absent, also check `ConnectionStrings:redis`. When Redis is configured, register `AddStackExchangeRedisCache()` and `AddFusionCacheStackExchangeRedisBackplane()` with the same connection string.

Create the only cache with:

```csharp
var fusionBuilder = services.AddFusionCache()
    .WithDefaultEntryOptions(options =>
    {
        options.Duration = cacheOptions.DefaultDuration;
        options.JitterMaxDuration = cacheOptions.Jitter;
        options.IsFailSafeEnabled = false;
    })
    .WithSystemTextJsonSerializer()
    .TryWithRegisteredDistributedCache()
    .TryWithRegisteredBackplane()
    .AsHybridCache();
```

Do not call `AddHybridCache()`. Extend the existing OpenTelemetry builder with the exact FusionCache instrumentation:

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddFusionCacheInstrumentation())
    .WithMetrics(metrics => metrics.AddFusionCacheInstrumentation());
```

- [ ] **Step 4: Run tests and commit**

```powershell
dotnet test tests/Full.NET.UnitTests --filter "FullyQualifiedName~FusionCacheRegistrationTests|FullyQualifiedName~CacheKeyBuilderTests"
dotnet build Full.NET.slnx
git add src/BuildingBlocks/Full.NET.Caching.Fusion tests/Full.NET.UnitTests/Caching
git commit -m "feat: add FusionCache dual abstraction"
```

### Task 9: Resolve tenants, expose the first API slice, and add Admin.NET compatibility

**Files:**
- Create: `src/Modules/Full.NET.Modules.Tenancy/Persistence/ITenantResolver.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantResolver.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/TenantResolutionMiddleware.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/TenancyApplicationBuilderExtensions.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Properties/AssemblyInfo.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Features/GetCurrentTenant/Query.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Features/GetCurrentTenant/Handler.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Features/GetCurrentTenant/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Serialization/TenancyJsonSerializerContext.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/TenancyModule.cs`
- Create: `src/Compatibility/Full.NET.Compatibility.AdminNet/AdminNetEnvelope.cs`
- Create: `src/Compatibility/Full.NET.Compatibility.AdminNet/AdminNetApiResultMapper.cs`
- Create: `src/Compatibility/Full.NET.Compatibility.AdminNet/ServiceCollectionExtensions.cs`
- Test: `tests/Full.NET.UnitTests/Tenancy/TenantResolverTests.cs`
- Test: `tests/Full.NET.UnitTests/Tenancy/TenancyJsonSerializerContextTests.cs`
- Test: `tests/Full.NET.CompatibilityTests/AdminNetApiResultMapperTests.cs`

**Interfaces:**
- Consumes: Dapper executors, `HybridCache`, `IApiResultMapper`, query dispatcher, current tenant accessor.
- Produces: host/domain tenant resolution, source-generated JSON for `GET /api/v1/tenancy/current`, optional Admin.NET response envelope.

- [ ] **Step 1: Write failing resolver tests**

Use a fake `IQueryExecutor` returning a known tenant and a real in-memory FusionCache. Call the resolver twice for `Acme.LocalHost`; assert the executor was called once, the domain was normalized to `acme.localhost`, and both results contain the same tenant ID. Also assert a missing tenant returns null with a short one-minute negative cache entry.

- [ ] **Step 2: Implement cached global resolution**

```csharp
public interface ITenantResolver
{
    Task<TenantSummary?> ResolveByDomainAsync(string domain, CancellationToken cancellationToken = default);
}
```

`TenantResolver` normalizes the host, builds `CacheKeyBuilder.ForGlobal(environment, "tenancy", "domain", domain, "v1")`, and uses `HybridCache.GetOrCreateAsync()` around `TenantSql.FindByDomain`. Cache active tenants for five minutes and missing tenants for one minute. Tag entries with `CacheKeyBuilder.DomainTag(domain)` and `CacheKeyBuilder.TenantTag(id)` when an ID is available. Do not enable Fail-Safe.

- [ ] **Step 3: Implement request resolution middleware**

For paths outside `/api`, call the next middleware without resolving a tenant. For `/api` requests:

1. Read `HttpContext.Request.Host.Host`.
2. Resolve the tenant by domain.
3. If absent or inactive, map a NotFound error with code `tenancy.host-not-found` through `IApiResultMapper` and stop.
4. Set `HttpContext.Items["FullNet.TenantId"] = tenant.Id` and then call `CurrentTenantAccessor.SetTenant()`.
5. Invoke the next middleware in `try` and call `Clear()` in `finally`.

Do not accept `X-Tenant` in M1. Host-admin switching is added with authenticated authorization in M2.

Keep the middleware, resolver, queries, handlers, endpoints, and persistence types internal. Expose only this Host-facing extension outside Contracts:

```csharp
public static class TenancyApplicationBuilderExtensions
{
    public static IApplicationBuilder UseFullNetTenancy(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantResolutionMiddleware>();
}
```

Add `[assembly: InternalsVisibleTo("Full.NET.UnitTests")]` for the focused resolver tests. `TenancyModule` and `TenancyApplicationBuilderExtensions` are the only public non-Contracts entry types.

- [ ] **Step 4: Implement the source-generated JSON context, query, and endpoint**

```csharp
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(TenantSummary))]
internal partial class TenancyJsonSerializerContext : JsonSerializerContext;

internal sealed record GetCurrentTenantQuery : IQuery<TenantSummary>;
```

`TenancyModule.ConfigureServices()` inserts `TenancyJsonSerializerContext.Default` at index 0 of `HttpJsonOptions.SerializerOptions.TypeInfoResolverChain`. The unit test serializes a `TenantSummary` using `TenancyJsonSerializerContext.Default.TenantSummary`, asserts camel-case property names, deserializes it back, and asserts all members. It must not call a reflection-only `JsonSerializer.Serialize(object, Type, options)` overload.

The handler executes `TenantSql.GetCurrent`, maps no row to `tenancy.not-found` NotFound, and returns the DTO. Map the endpoint exactly once:

```csharp
group.MapGet("/current", async (
    IQueryDispatcher dispatcher,
    IApiResultMapper mapper,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var result = await dispatcher.SendAsync<GetCurrentTenantQuery, TenantSummary>(
        new GetCurrentTenantQuery(), cancellationToken);
    return mapper.Map(result, httpContext);
});
```

`TenancyModule` registers the resolver and both handlers and maps the group `/api/v1/tenancy`.

- [ ] **Step 5: Write failing Admin.NET compatibility tests**

Assert successful `Result<string>` maps to real status 200 and value:

```json
{"success":true,"code":"success","message":null,"data":"ok","traceId":"..."}
```

Assert Conflict maps to real status 409, `success:false`, code `tenancy.identifier-exists`, null data, and a non-empty trace ID. Assert `AddAdminNetCompatibility()` replaces `IApiResultMapper` but is never registered by `AddFullNetServiceDefaults()`.

- [ ] **Step 6: Implement the opt-in mapper**

```csharp
public sealed record AdminNetEnvelope<T>(
    bool Success,
    string Code,
    string? Message,
    T? Data,
    string TraceId);
```

`AdminNetApiResultMapper` returns `Results.Json(envelope, statusCode: actualStatus)`. Success uses code `success`; failure uses the Full.NET error code and `StandardApiResultMapper.ToStatusCode()`. `MapException()` returns code `common.unexpected` with status 500. `AddAdminNetCompatibility()` uses `services.Replace()` to replace only `IApiResultMapper`. It does not add middleware and therefore cannot wrap file, stream, SSE, SignalR, health, or `204` results.

- [ ] **Step 7: Run tests and commit**

```powershell
dotnet test tests/Full.NET.UnitTests --filter "FullyQualifiedName~TenantResolverTests|FullyQualifiedName~TenancyJsonSerializerContextTests"
dotnet test tests/Full.NET.CompatibilityTests --filter FullyQualifiedName~AdminNetApiResultMapperTests
dotnet build Full.NET.slnx
git add src/Modules/Full.NET.Modules.Tenancy src/Compatibility/Full.NET.Compatibility.AdminNet tests/Full.NET.UnitTests/Tenancy tests/Full.NET.CompatibilityTests
git commit -m "feat: add tenancy API and Admin.NET compatibility"
```

### Task 10: Implement Outbox processing and all runtime hosts

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.Abstractions/OutboxEnvelope.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Abstractions/IOutboxStore.cs`
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Messaging/IIntegrationEventHandler.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/OutboxSql.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperOutboxStore.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/TenantProvisionedCacheInvalidationHandler.cs`
- Create: `src/Hosts/Full.NET.Host.Worker/OutboxProcessor.cs`
- Replace: `src/Hosts/Full.NET.Host.Worker/Program.cs`
- Replace: `src/Hosts/Full.NET.Host.Migrator/Program.cs`
- Replace: `src/Hosts/Full.NET.Host.Api/Program.cs`
- Replace: `src/Hosts/Full.NET.AppHost/Program.cs`
- Create/Modify: the four hosts' `appsettings*.json`
- Test: `tests/Full.NET.UnitTests/Outbox/OutboxProcessorTests.cs`

**Interfaces:**
- Consumes: all prior M1 foundations.
- Produces: leased at-least-once Outbox processing, executable API/Worker/Migrator/AppHost.

- [ ] **Step 1: Write failing Outbox processor tests**

Use a fake store returning one binary `OutboxEnvelope`, a matching type/version recording handler, a handler with a different type, and a handler with the same type but a different schema version. Execute one processor iteration and assert only the exact type/version handler ran and `MarkProcessedAsync()` received the message ID and lock ID. Make the matching handler throw; assert `MarkFailedAsync()` receives a non-empty error and a future retry time, and `MarkProcessedAsync()` is not called.

- [ ] **Step 2: Define event and Outbox processing contracts**

```csharp
public interface IIntegrationEventHandler
{
    string EventType { get; }
    int SchemaVersion { get; }
    Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
}

public sealed record OutboxEnvelope(
    Guid Id,
    Guid LockId,
    string Type,
    int SchemaVersion,
    string ContentType,
    Guid? TenantId,
    string? TraceId,
    byte[] Payload,
    int Attempts,
    DateTimeOffset OccurredAt);

public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxEnvelope>> AcquireAsync(int batchSize, TimeSpan lease, CancellationToken cancellationToken);
    Task MarkProcessedAsync(Guid id, Guid lockId, CancellationToken cancellationToken);
    Task MarkFailedAsync(Guid id, Guid lockId, string error, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Implement provider-specific leasing SQL**

Use this SQL Server acquisition statement with `SqlDataScope.HostOnly`:

```sql
;WITH Pending AS
(
    SELECT TOP (@BatchSize) *
    FROM fn_outbox_message WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE ProcessedAt IS NULL
      AND (NextAttemptAt IS NULL OR NextAttemptAt <= @Now)
      AND (LockedUntil IS NULL OR LockedUntil <= @Now)
    ORDER BY OccurredAt
)
UPDATE Pending
SET LockId = @LockId,
    LockedUntil = @LockedUntil,
    Attempts = Attempts + 1
OUTPUT inserted.Id,
       inserted.LockId,
       inserted.Type,
       inserted.SchemaVersion,
       inserted.ContentType,
       inserted.TenantId,
       inserted.TraceId,
       inserted.Payload,
       inserted.Attempts,
       inserted.OccurredAt;
```

For MySQL, execute these two `HostOnly` statements on the same scoped transaction:

```sql
UPDATE fn_outbox_message
SET LockId = @LockId,
    LockedUntil = @LockedUntil,
    Attempts = Attempts + 1
WHERE ProcessedAt IS NULL
  AND (NextAttemptAt IS NULL OR NextAttemptAt <= @Now)
  AND (LockedUntil IS NULL OR LockedUntil <= @Now)
ORDER BY OccurredAt
LIMIT @BatchSize;
```

```sql
SELECT Id, LockId, Type, SchemaVersion, ContentType, TenantId, TraceId, Payload, Attempts, OccurredAt
FROM fn_outbox_message
WHERE LockId = @LockId
ORDER BY OccurredAt;
```

Create a fresh UUID v7 `LockId` per acquisition and set `LockedUntil = Now + lease`.

`MarkProcessed` uses:

```sql
UPDATE fn_outbox_message
SET ProcessedAt = @Now,
    LockId = NULL,
    LockedUntil = NULL,
    Error = NULL
WHERE Id = @Id AND LockId = @LockId AND ProcessedAt IS NULL
```

`MarkFailed` uses:

```sql
UPDATE fn_outbox_message
SET NextAttemptAt = @NextAttemptAt,
    LockId = NULL,
    LockedUntil = NULL,
    Error = @Error
WHERE Id = @Id AND LockId = @LockId AND ProcessedAt IS NULL
```

Store only the first 2000 error characters. Both statements are `HostOnly` and must affect exactly one row; otherwise throw a concurrency exception. `DapperOutboxStore` uses Host context because Outbox is global; Worker sets `CurrentTenantAccessor.SetHost()` when starting each scope.

- [ ] **Step 4: Implement processor behavior and cache invalidation handler**

`OutboxProcessor` is a `BackgroundService`. Every second it creates a scope, acquires up to 20 messages with a 30-second lease, rejects content types other than `application/x-msgpack`, finds exactly one handler by `(EventType, SchemaVersion)`, and passes the binary payload without parsing or transcoding. Missing or duplicate handlers, unsupported schema versions, and unsupported content types are failures and leave the message retryable. Backoff is `min(300 seconds, 2^Attempts seconds)`. Use `[LoggerMessage]` methods for leased/processed/failed events; log event ID/type/version/attempts but never the payload.

`TenantProvisionedCacheInvalidationHandler` handles type `fullnet.tenancy.tenant-provisioned`, schema version `1`, uses `IIntegrationEventSerializer.Deserialize<TenantProvisionedIntegrationEvent>()`, then calls `HybridCache.RemoveByTagAsync(CacheKeyBuilder.TenantTag(event.TenantId))` and `HybridCache.RemoveByTagAsync(CacheKeyBuilder.DomainTag(event.Domain))`.

- [ ] **Step 5: Implement Host.Api**

Use this composition order:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddFullNetServiceDefaults();
builder.Services.AddOpenApi();
builder.Services.AddFullNetModularity();
builder.Services.AddFullNetDapper(builder.Configuration);
builder.Services.AddFullNetMessagePack();
builder.Services.AddFullNetCaching(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddFullNetMigrations(builder.Configuration);
builder.Services.AddFullNetModule<TenancyModule>(builder.Configuration);

var app = builder.Build();
app.UseFullNetRequestLogging();
app.UseExceptionHandler();
app.UseFullNetTenancy();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapFullNetHealthEndpoints();
app.MapFullNetModules();
app.Run();

public partial class Program;
```

`AddFullNetDapper()` must use `Database:ConnectionString` when present, otherwise resolve `ConnectionStrings:{Database:ConnectionName}` where the default connection name is `fullnet`.

- [ ] **Step 6: Implement Host.Migrator and Host.Worker**

Migrator builds a generic host with service defaults, modularity, Dapper, migrations, and Tenancy services. It sets Host context, runs `MigrateAsync()`, and exits non-zero on failure. When passed `--seed-local`, resolve `localhost`; if absent, call `ITenantProvisioningService.ProvisionAsync(new("local", "Full.NET Local", "localhost"))`. Treat an existing local tenant as success.

Worker builds a generic host with service defaults, Dapper, `AddFullNetMessagePack()`, FusionCache, Outbox store, Tenancy event handler, and `OutboxProcessor`. It never runs migrations. Migrator also registers `AddFullNetMessagePack()` because `--seed-local` invokes tenant provisioning and therefore writes the same binary Outbox contract; no Host may add a JSON fallback.

- [ ] **Step 7: Implement the Aspire AppHost**

Use `appsettings.json`:

```json
{
  "UseMySql": false
}
```

Implement:

```csharp
var builder = DistributedApplication.CreateBuilder(args);
var redis = builder.AddRedis("redis");
var useMySql = builder.Configuration.GetValue<bool>("UseMySql");

IResourceBuilder<IResourceWithConnectionString> database = useMySql
    ? builder.AddMySql("mysql").AddDatabase("fullnet")
    : builder.AddSqlServer("sql").AddDatabase("fullnet");

var provider = useMySql ? "MySql" : "SqlServer";
var migrator = builder.AddProject<Projects.Full_NET_Host_Migrator>("migrator")
    .WithReference(database)
    .WithEnvironment("Database__Provider", provider)
    .WithArgs("--seed-local")
    .WaitFor(database);

builder.AddProject<Projects.Full_NET_Host_Api>("api")
    .WithReference(database)
    .WithReference(redis)
    .WithEnvironment("Database__Provider", provider)
    .WaitForCompletion(migrator);

builder.AddProject<Projects.Full_NET_Host_Worker>("worker")
    .WithReference(database)
    .WithReference(redis)
    .WithEnvironment("Database__Provider", provider)
    .WaitForCompletion(migrator);

builder.Build().Run();
```

If generated project metadata uses a different valid identifier, use the generated `Projects` type shown by the compiler rather than suppressing the error.

- [ ] **Step 8: Run focused tests, start AppHost, and commit**

```powershell
dotnet test tests/Full.NET.UnitTests --filter FullyQualifiedName~OutboxProcessorTests
dotnet build Full.NET.slnx
dotnet run --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
```

Expected in Aspire: database and Redis become healthy, Migrator exits with code 0, then API and Worker start; `/health/ready` is healthy. Stop AppHost after verification.

```powershell
git add src/BuildingBlocks src/Modules src/Hosts tests/Full.NET.UnitTests/Outbox
git commit -m "feat: add runtime hosts and outbox worker"
```

### Task 11: Add full vertical integration, architecture gates, CI, and operator documentation

**Files:**
- Create: `tests/Full.NET.IntegrationTests/Api/TenancyApiSqlServerTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Api/TenancyApiMySqlTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Api/FullNetApiFactory.cs`
- Create: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`
- Create: `tests/Full.NET.ArchitectureTests/DataAccessRulesTests.cs`
- Create: `tests/Full.NET.ArchitectureTests/SerializationRulesTests.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Program.cs`
- Create: `benchmarks/Full.NET.Benchmarks/SerializationBenchmarks.cs`
- Create: `.github/workflows/ci.yml`
- Create: `README.md`
- Create: `docs/development/getting-started.md`
- Modify: `THIRD-PARTY-NOTICES`

**Interfaces:**
- Consumes: the complete M0-M1 implementation.
- Produces: reproducible end-to-end proof, architecture enforcement, focused serialization benchmark evidence, CI/package-audit gate, and local operator instructions.

- [ ] **Step 1: Write failing API integration tests for both providers**

`FullNetApiFactory` subclasses `WebApplicationFactory<Program>`, overrides configuration with provider and container connection string, runs migrations, sets Host context, provisions tenant `acme`/`acme.localhost`, and creates a client whose `Host` header is `acme.localhost`.

For each provider assert:

```csharp
var response = await client.GetAsync("/api/v1/tenancy/current");
Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

var tenant = await response.Content.ReadFromJsonAsync<TenantSummary>();
Assert.IsNotNull(tenant);
Assert.AreEqual("acme", tenant.Identifier);
Assert.AreEqual("acme.localhost", tenant.Domain);
```

Create a second client with Host `missing.localhost`; assert 404 ProblemDetails, code `tenancy.host-not-found`, and non-empty trace ID. Assert the success JSON has no `success`, `code`, or `data` envelope properties.

Run both tests. Expected: failures reveal any missing host wiring before implementation is considered complete.

- [ ] **Step 2: Run the vertical tests**

```powershell
dotnet test tests/Full.NET.IntegrationTests --filter FullyQualifiedName~TenancyApiSqlServerTests
dotnet test tests/Full.NET.IntegrationTests --filter FullyQualifiedName~TenancyApiMySqlTests
```

Expected: both pass. If either fails, invoke `superpowers:systematic-debugging`, preserve the expected status codes/direct DTO/tenant isolation/ProblemDetails assertions, correct the implementation defect, and rerun both commands.

- [ ] **Step 3: Add architecture tests**

Use NetArchTest to assert:

```csharp
Types.InAssemblies(buildingBlockAssemblies)
    .ShouldNot().HaveDependencyOn("Full.NET.Modules").GetResult().IsSuccessful;

Types.InAssemblies(allProductionAssemblies.Except(dataDapperAssembly))
    .ShouldNot().HaveDependencyOn("Dapper").GetResult().IsSuccessful;
```

Enforce module visibility with reflection instead of relying on a name predicate:

```csharp
var unexpectedPublicTypes = tenancyAssembly.GetExportedTypes()
    .Where(type => type.Namespace != "Full.NET.Modules.Tenancy.Contracts")
    .Where(type => type.Name is not "TenancyModule" and not "TenancyApplicationBuilderExtensions")
    .Select(type => type.FullName)
    .ToArray();

CollectionAssert.IsEmpty(unexpectedPublicTypes);
```

Add explicit reflection assertions that no production type contains methods or properties named `GetService`, `GetRequiredService`, or `RootServices` except the two dispatcher implementations, where resolving the closed handler type is the implementation's single responsibility.

`SerializationRulesTests` enumerates production `.cs` files from the repository root and fails with the offending relative paths when a file contains `TypelessFormatter`, `TypelessContractlessStandardResolver`, `ContractlessStandardResolver`, `MessagePackSerializer.DefaultOptions`, or `Newtonsoft.Json`. It asserts `MessagePackIntegrationEventSerializer.cs` contains `WithSecurity(MessagePackSecurity.UntrustedData)` and does not contain a forbidden token. It also reflects over `TenantProvisionedIntegrationEvent`, asserts `[MessagePackObject]` is present, and asserts its public serialized members have unique integer `KeyAttribute` values `0`, `1`, and `2`. Do not ban System.Text.Json: it remains the public HTTP format.

- [ ] **Step 4: Add representative serialization benchmarks**

Add `[assembly: InternalsVisibleTo("Full.NET.Benchmarks")]` to the Tenancy assembly metadata so the benchmark exercises the actual generated context without exporting it as framework API.

Create this benchmark shape:

```csharp
[MemoryDiagnoser]
public class SerializationBenchmarks
{
    private readonly TenantSummary _summary = new(
        Guid.Parse("018f3f78-4d7a-7c16-9f0f-8a7ce6d5a001"),
        "acme",
        "Acme Corporation",
        "acme.localhost",
        true,
        1);

    private readonly TenantProvisionedIntegrationEvent _event = new(
        Guid.Parse("018f3f78-4d7a-7c16-9f0f-8a7ce6d5a001"),
        "acme",
        "acme.localhost");

    private readonly IIntegrationEventSerializer _messagePack =
        new MessagePackIntegrationEventSerializer();

    private byte[] _messagePackPayload = [];

    [GlobalSetup]
    public void Setup() => _messagePackPayload = _messagePack.Serialize(_event);

    [Benchmark(Baseline = true)]
    public byte[] SystemTextJsonSourceGenerated() =>
        JsonSerializer.SerializeToUtf8Bytes(
            _summary,
            TenancyJsonSerializerContext.Default.TenantSummary);

    [Benchmark]
    public byte[] MessagePackSerialize() => _messagePack.Serialize(_event);

    [Benchmark]
    public TenantProvisionedIntegrationEvent MessagePackDeserialize() =>
        _messagePack.Deserialize<TenantProvisionedIntegrationEvent>(_messagePackPayload);
}
```

`Program.cs` calls `BenchmarkRunner.Run<SerializationBenchmarks>()`. Run the short job and retain the generated Markdown report as a local/CI artifact, not as a claim that unlike payloads are directly comparable:

```powershell
dotnet run --configuration Release --project benchmarks/Full.NET.Benchmarks -- --job short --filter "*SerializationBenchmarks*"
```

Expected: all three benchmarks execute without errors and report allocation/throughput data. M1 records the baseline; hard regression thresholds are introduced in M4 after stable runners exist.

- [ ] **Step 5: Create CI**

Create `.github/workflows/ci.yml`:

```yaml
name: ci
on:
  push:
    branches: [main]
  pull_request:

jobs:
  build-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - run: dotnet restore Full.NET.slnx
      - run: dotnet list Full.NET.slnx package --vulnerable --include-transitive
      - run: dotnet build Full.NET.slnx --configuration Release --no-restore
      - run: dotnet test Full.NET.slnx --configuration Release --no-build
```

GitHub hosted runners provide Docker for Testcontainers. Do not mark SQL Server/MySQL tests optional.

- [ ] **Step 6: Document local development and provider switching**

`README.md` must state project purpose, MIT license, current M0-M1 scope, prerequisites, and these commands:

```powershell
dotnet restore Full.NET.slnx
dotnet build Full.NET.slnx
dotnet test Full.NET.slnx
dotnet run --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
```

`docs/development/getting-started.md` must explain Docker, Aspire dashboard, default SQL Server, setting `UseMySql=true`, Migrator-before-API deployment ordering, `localhost` seed behavior, Redis optionality outside AppHost, real HTTP/ProblemDetails, source-generated JSON contexts, MessagePack Outbox schema/version rules, asynchronous log buffer/drop monitoring, and how to opt into `AddAdminNetCompatibility()` in a custom Host. Include no real secrets. Add a communication matrix stating: in-process typed contracts now; gRPC + Protobuf only after a real service split; SignalR/Realtime in M2; AI/MCP/AG-UI in separate M5+ plans.

- [ ] **Step 7: Run the complete M0-M1 gate**

```powershell
dotnet restore Full.NET.slnx
dotnet list Full.NET.slnx package --vulnerable --include-transitive
dotnet build Full.NET.slnx --configuration Release --no-restore
dotnet test Full.NET.slnx --configuration Release --no-build
dotnet run --configuration Release --project benchmarks/Full.NET.Benchmarks -- --job short --filter "*SerializationBenchmarks*"
git diff --check
git status --short
```

Expected: restore reports no package-audit warnings, the vulnerability command reports no vulnerable packages, build/test succeed, both database suites run, all three serialization benchmarks execute, `git diff --check` is silent, and status contains only the intended Task 11 changes plus ignored benchmark artifacts.

- [ ] **Step 8: Commit and tag the plan milestone**

```powershell
git add .github README.md THIRD-PARTY-NOTICES docs/development tests/Full.NET.IntegrationTests tests/Full.NET.ArchitectureTests benchmarks
git commit -m "test: verify Full.NET foundation end to end"
git tag -a m1-foundation -m "Full.NET M1 foundation"
```

Do not push the tag or create a remote release without explicit user authorization.

## Completion Gate

M0-M1 is complete only when all of the following are true:

- `dotnet build Full.NET.slnx --configuration Release` succeeds with zero warnings.
- SQL Server and MySQL migration tests execute, including the idempotent second run.
- Tenant provisioning and Outbox insertion are atomic on both providers.
- Outbox payload is binary MessagePack with `SchemaVersion`, `ContentType`, tenant, and trace metadata; no Outbox processing path parses JSON.
- `/api/v1/tenancy/current` returns a direct `TenantSummary` for a known host.
- `TenantSummary` HTTP serialization is covered by the module's generated `JsonSerializerContext`.
- An unknown host returns 404 ProblemDetails with code `tenancy.host-not-found`.
- Admin.NET compatibility returns an envelope only when explicitly registered and preserves 409/500 statuses.
- `HybridCache` and `IFusionCache` share the same FusionCache instance.
- Worker leases, dispatches, retries, and marks Outbox messages.
- Operational logs use `ILogger<T>`, generated hot-path messages, and a bounded Serilog async sink with observable queue/drop state; no request body or secret is logged.
- Package audit reports no known vulnerable direct or transitive packages, and the short serialization benchmark completes all configured methods.
- API never runs production migrations; AppHost waits for Migrator completion locally.
- Architecture tests prevent BuildingBlocks-to-Modules references and Dapper use outside `Full.NET.Data.Dapper`.
- The independent Full.NET worktree is clean after the final commit.
