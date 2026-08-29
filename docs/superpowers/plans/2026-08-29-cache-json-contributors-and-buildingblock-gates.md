# Cache JSON Contributors and BuildingBlock Gates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 消除 `Full.NET.Caching.Fusion` 对业务模块的反向依赖，并让所有 BuildingBlock 项目都受到可执行的依赖方向门禁保护。

**Architecture:** Caching 只定义 AOT 缓存 JSON 元数据贡献契约并在解析时聚合；Settings、Tenancy 等载荷所有者使用各自的源生成 `JsonSerializerContext` 注册贡献者。架构测试同时检查程序集依赖和 `ProjectReference`，避免手工程序集清单遗漏后静默形成 BuildingBlocks → Modules 反向依赖。

**Tech Stack:** .NET 10、System.Text.Json source generation、Microsoft DI、FusionCache 2.6、MSTest、NetArchTest、Native AOT。

## Global Constraints

- BuildingBlocks 不得引用 `src/Modules`，业务模块只能向稳定 BuildingBlock 依赖。
- Native AOT 缓存序列化必须使用源生成元数据；未知载荷必须失败关闭，禁止反射回退。
- 不改变缓存键、TTL、可靠性分类、Redis L2、Backplane 或 HybridCache 行为。
- 本切片不拆 Data.Dapper、Modularity、迁移或 CodeGeneration 项目。

---

### Task 1: 建立反向依赖和 AOT 元数据聚合红灯

**Files:**
- Modify: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`
- Modify: `tests/Full.NET.ArchitectureTests/NativeAotStaticBindingRulesTests.cs`
- Modify: `tests/Full.NET.UnitTests/Caching/FusionCacheRegistrationTests.cs`

**Interfaces:**
- Consumes: 现有 `AddFullNetCaching`、Settings/Tenancy 模块注册和源生成 Context。
- Produces: 对目录完整性、项目引用方向、Contributor 解析和未知类型失败关闭的回归测试。

- [ ] **Step 1: 写失败的架构测试**

  增加自动枚举 `src/BuildingBlocks/**/*.csproj` 的测试，拒绝指向 `src/Modules` 的 `ProjectReference`；并把 Messaging 两个程序集加入现有程序集门禁。

- [ ] **Step 2: 写失败的 AOT 序列化测试**

  测试 Settings 与 Tenancy 注册后，缓存序列化器能从模块 Context 解析 `DiagnosticPolicyDocument`、`GridPreferenceResponse`、`TenantResolutionCacheEntry`，且未登记类型抛出 `NotSupportedException`。

- [ ] **Step 3: 运行测试确认红灯原因**

  Run: `dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --no-restore --filter FullyQualifiedName~BuildingBlocks_DoNotDependOnModules`

  Expected: Caching → Settings.Contracts 反向依赖失败。

  Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --filter FullyQualifiedName~FusionCacheRegistrationTests`

  Expected: Contributor API 或模块登记尚不存在而失败。

### Task 2: 实现模块化 AOT 缓存 JSON Contributor

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Caching.Fusion/Serialization/ICacheJsonTypeInfoContributor.cs`
- Modify: `src/BuildingBlocks/Full.NET.Caching.Fusion/Serialization/FullNetFusionCacheJsonSerializer.cs`
- Delete: `src/BuildingBlocks/Full.NET.Caching.Fusion/Serialization/FusionCacheJsonSerializerContext.cs`
- Modify: `src/BuildingBlocks/Full.NET.Caching.Fusion/ServiceCollectionExtensions.cs`
- Modify: `src/BuildingBlocks/Full.NET.Caching.Fusion/Full.NET.Caching.Fusion.csproj`
- Create: `src/Modules/Full.NET.Modules.Settings/Serialization/SettingsCacheJsonTypeInfoContributor.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Serialization/TenancyCacheJsonTypeInfoContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Settings/SettingsModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenancyModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Serialization/TenancyJsonSerializerContext.cs`

**Interfaces:**
- Produces: `ICacheJsonTypeInfoContributor.GetTypeInfo(Type)`；由模块返回自身源生成 Context 的 `JsonTypeInfo`。
- Consumes: `IEnumerable<ICacheJsonTypeInfoContributor>`；按注册顺序解析，第一个非空元数据生效，全部为空时失败关闭。

- [ ] **Step 1: 添加最小 Contributor 契约和模块实现**

  契约只暴露 `JsonTypeInfo? GetTypeInfo(Type type)`；Settings/Tenancy Contributor 分别委托到现有源生成 Context，不增加反射或程序集扫描。

- [ ] **Step 2: 通过 DI 构造 AOT 缓存序列化器**

  `FullNetFusionCacheJsonSerializer` 在构造时冻结 Contributor 数组，序列化时只查询源生成元数据。FusionCache 使用 `WithSerializer(serviceProvider => ...)`，保留现有 Redis L2、Backplane 和 HybridCache 链。

- [ ] **Step 3: 删除集中式业务类型 Context 和反向项目引用**

  删除 Caching 内的 `FusionCacheJsonSerializerContext`，并从 Caching csproj 移除 Hosting、Settings.Contracts 引用。

- [ ] **Step 4: 运行聚焦测试确认绿灯**

  Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --filter FullyQualifiedName~FusionCacheRegistrationTests`

  Expected: PASS。

  Run: `dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --no-restore --filter "FullyQualifiedName~BuildingBlocks_DoNotDependOnModules|FullyQualifiedName~BuildingBlock_projects_do_not_reference_business_modules|FullyQualifiedName~FusionCacheL2_UsesSourceGeneratedJsonContext"`

  Expected: PASS。

### Task 3: Native AOT 与影响集验证

**Files:**
- Modify only if a verified contract or usage document is affected.

**Interfaces:**
- Consumes: 完成后的 DI、JSON Context 和架构门禁。
- Produces: 可复查的构建、测试、AOT 分析和 Git 证据。

- [ ] **Step 1: 运行受影响测试与 AOT 分析**

  Run: `pnpm test:inner -- --base 02358dcd3c6145126ce7483c1e5b9be6f0c90ba2`

  Run: `pnpm test:aot:analyzers`

  Expected: 所有选择器命中项通过且 AOT analyzer 为 0 error。

- [ ] **Step 2: 检查差异和工作区**

  Run: `git diff --check`

  Run: `git status --short --branch`

  Expected: 无空白错误，差异仅属于本计划影响集。

