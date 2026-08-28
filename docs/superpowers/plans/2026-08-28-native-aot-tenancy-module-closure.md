# Tenancy Native AOT Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 Host.Api 可达的 Tenancy SQL 参数与结果投影在 Native AOT 下形成静态闭包，并由 SQL Server/MySQL 原生产物真实验证。

**Architecture:** 保留现有 `IQueryExecutor`、`ICommandExecutor`、SQL、事务和租户缓存边界，只把匿名参数替换为模块内固定键名字典，并在现有 contributor 中补齐套餐投影。原生 E2E 通过 Host 管理 API 创建套餐、分页和按 ID 读回，避免空结果假绿。

**Tech Stack:** .NET 10、Dapper 自有执行边界、MSTest、SQL Server、MySQL、Linux Native AOT。

## Global Constraints

- SQL Server 与 MySQL 必须同时验证，禁止改变 SQL、事务、租户隔离或公开 API 语义。
- Host.Api 可达参数必须使用稳定键名字典，结果 DTO 必须使用静态 ordinal materializer。
- 原生运行证据必须读取本次创建的非空行；普通 JIT 构建不能替代 Linux 原生产物。
- 仅提交 Tenancy、对应 Architecture/Integration 测试和本计划/验证记录；保留无关工作区状态。

---

### Task 1: 建立 Tenancy 静态闭包 RED 门禁

**Files:**
- Modify: `tests/Full.NET.ArchitectureTests/NativeAotStaticBindingRulesTests.cs`

**Interfaces:**
- Consumes: `ContainsAnonymousSqlParameterObject(string source)` 与仓库根定位器。
- Produces: `TenancyModule_UsesAotSafeSqlParameters`、`TenancyModule_RegistersAllNativeAotRowMaterializers`。

- [ ] **Step 1: 写入失败测试**

```csharp
Assert.HasCount(0, offenders, "Native AOT Tenancy 模块不得向 SQL 执行器传递匿名参数。");
StringAssert.Contains(contributorSource, "registrar.Register<TenantPackageRecord>");
StringAssert.Contains(contributorSource, "registrar.Register<TenantPackageIdentityRecord>");
```

- [ ] **Step 2: 运行 RED**

Run: `dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --nologo`

Expected: 两个新增测试因 Tenancy 匿名参数与缺失套餐 materializer 失败。

### Task 2: 最小化修复参数与物化闭包

**Files:**
- Create: `src/Modules/Full.NET.Modules.Tenancy/Persistence/TenancySqlParameters.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Features/ProvisionTenant/Handler.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Features/ManageHostTenants/HostTenantQueryService.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Features/ManageHostTenants/HostTenantManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Features/ManageHostTenantPackages/HostTenantPackageQueryService.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Features/ManageHostTenantPackages/HostTenantPackageManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Persistence/TenancyDapperAotMaterializerContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Seeding/LocalTenantSeedContributor.cs`

**Interfaces:**
- Produces: `TenancySqlParameters.Create(params (string Name, object? Value)[] pairs)`。
- Produces: `TenantPackageRecord` 七列、`TenantPackageIdentityRecord` 六列与 `LocalTenantSeedSummary` 六列的 ordinal reader。

- [ ] **Step 1: 新增固定键参数工厂**

```csharp
internal static class TenancySqlParameters
{
    public static Dictionary<string, object?> Create(params (string Name, object? Value)[] pairs)
    {
        var parameters = new Dictionary<string, object?>(pairs.Length, StringComparer.Ordinal);
        foreach (var (name, value) in pairs)
        {
            parameters[name] = value;
        }

        return parameters;
    }
}
```

- [ ] **Step 2: 替换 Host.Api 可达匿名 SQL 参数**

每个调用按原键名和值改为 `TenancySqlParameters.Create(("Key", value), ...)`；`LocalTenantSeedContributor` 虽只由 Migrator 执行，但当前经 `TenancyModule.AddServices` 注册进 API 服务集合，因此同样纳入静态闭包，禁止目录豁免。

- [ ] **Step 3: 注册套餐物化器**

按 `TenantPackageSql` 的投影顺序读取 `Id, Code, Name, Description, IsActive, Version[, AssignedTenantCount]`，布尔和可空字符串复用 `AotDataReaderExtensions`。

- [ ] **Step 4: 运行 GREEN**

Run: `dotnet build src/Modules/Full.NET.Modules.Tenancy/Full.NET.Modules.Tenancy.csproj -c Release --nologo`

Run: `dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --filter "FullyQualifiedName~TenancyModule_UsesAotSafeSqlParameters|FullyQualifiedName~TenancyModule_RegistersAllNativeAotRowMaterializers" --minimum-expected-tests 2`

Expected: 构建 0 警告/0 错误，测试 2/2 通过。

### Task 3: 建立原生双库非空运行证据并提交

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeApiE2EAssertions.cs`
- Create: `docs/verification/2026-08-28-native-aot-tenancy-module-closure.md`

**Interfaces:**
- Consumes: Host 管理员令牌、`CreateTenantPackageRequest`、`TenantPackageSummary`、现有授权请求 helper。
- Produces: 创建套餐后列表和按 ID 命中的真实原生物化证据。

- [ ] **Step 1: 扩展原生 HTTP 流程**

通过 `/api/v1/tenancy/tenant-packages` 创建唯一套餐，断言 `201`，再分页并按 ID 获取，要求两次响应均包含创建 ID。

- [ ] **Step 2: 运行完整门禁**

Run: `pnpm test:aot:analyzers`

Run: `pnpm test:aot:publish:linux`

Run: Linux 原生核心 SQL Server/MySQL 5-test suite，并保留独立 TRX。

Run: `pnpm test:inner -- --snapshot native-aot-tenancy-20260828`

Run: `pnpm test:governance`

Run: `git diff --check`

Expected: 所有命令成功，原生套件 5/5，且无 Tenancy 新 AOT/Trim 告警。

- [ ] **Step 3: 独立审查并提交**

审查参数键、列序、双库类型、真实非空 E2E 与租户边界；修复全部 Critical/Important 后提交 `feat: close Tenancy Native AOT execution`。
