# SerialNumbers 模块 Native AOT 闭环实施计划

**Goal:** 让 Host.Api Native AOT 原生产物在 SQL Server/MySQL 上完成 SerialNumbers 规则创建、读取、列表与预览，并以静态门禁阻止匿名 SQL 参数和遗漏行物化器回归。

**Architecture:** 保持现有 SerialNumbers 单模块、Dapper 显式 SQL、事务和公共 HTTP 契约不变。模块内部新增 AOT 安全参数工厂，把匿名参数袋替换为稳定字典；在模块同步注册阶段为三个持久化记录注册显式 `DbDataReader` 物化器。现有核心 Native 外部进程门禁增加 Host 规则纵向流程，不创建新的宿主或运行时开关。原生 RED 进一步证明共享 `QueryMultipleAsync` 仍进入 Reflection.Emit，因此在既有 ADO.NET AOT 执行器内补充顺序多结果读取，而不改变上层抽象。

**Baseline:** `45ddd27738c92064811f6ae315b39fef7f7b787c`

**Task snapshot:** `native-aot-serialnumbers-20260828`

**Scope boundary:** 本切片不修改数据库结构、SQL 语义、公共 API、权限、租户规则、流水号分配并发算法或生产配置；不同时改造 Document/Auditing。

## Task 1: 静态闭包 RED 门禁

**Files:**
- Modify: `tests/Full.NET.ArchitectureTests/NativeAotStaticBindingRulesTests.cs`

**Interfaces:**
- Produces: `SerialNumbersModule_UsesAotSafeSqlParameters`。
- Produces: `SerialNumbersModule_RegistersAllNativeAotRowMaterializers`。

- [x] 枚举 SerialNumbers 源文件并拒绝传给 SQL 执行器的匿名参数对象。
- [x] 断言模块在 `FULLNET_AOT_COMPILE` 下注册 contributor，并覆盖 `SerialNumberRuleRecord`、`AllocatedCounterValue`、`SerialNumberAllocationRecord`。
- [x] 运行 Native AOT Architecture 过滤器，确认 RED 原因为当前匿名参数与缺失 contributor。

## Task 2: 参数与物化器 GREEN 实现

**Files:**
- Create: `src/Modules/Full.NET.Modules.SerialNumbers/Persistence/SerialNumbersSqlParameters.cs`
- Create: `src/Modules/Full.NET.Modules.SerialNumbers/Persistence/SerialNumbersDapperAotMaterializerContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.SerialNumbers/Features/ManageHostSerialRules/HostSerialRuleService.cs`
- Modify: `src/Modules/Full.NET.Modules.SerialNumbers/Features/AllocateSerialNumbers/SerialNumberAllocator.cs`
- Modify: `src/Modules/Full.NET.Modules.SerialNumbers/SerialNumbersModule.cs`

**Interfaces:**
- Produces: 固定键名的 `Dictionary<string, object?>` 参数工厂。
- Produces: 三个记录类型的显式 ordinal 物化器。

- [x] 把全部 SQL 匿名参数替换为参数工厂，保持占位符、空值和类型不变。
- [x] 按 SQL 投影顺序读取 Guid、可空 Guid、布尔、`DateTimeOffset`、可空 `DateTimeOffset` 与整数。
- [x] 在模块 `AddServices` 中仅于 `FULLNET_AOT_COMPILE` 同步注册 contributor，确保首个请求前完成。
- [x] 重跑 Architecture 过滤器并要求 GREEN。

## Task 3: 双库 Native 原生进程闭环

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeApiE2EAssertions.cs`
- Create: `docs/verification/2026-08-28-native-aot-serialnumbers-module-closure.md`

**Interfaces:**
- Consumes: 现有 Host 超级管理员 token 与双库 Native 核心 E2E。
- Produces: 创建规则、按 Id 读取、分页列表、纯函数预览的 HTTP/SQL/JSON 断言。

- [x] 在现有核心原生流程进入租户前执行 Host SerialNumbers 流程，使用每次运行唯一 RuleKey。
- [x] 断言 201、响应 Id/Version/RuleKey，随后 GET by id、列表包含该行，preview 返回确定结果。
- [x] 运行聚焦 Unit/Architecture、AOT analyzers、Linux Native publish，并在 Linux 环境执行 SQL Server/MySQL 原生产物 E2E；Windows discovery skip 不作为完成证据。
- [x] 运行受影响 Integration 计划/集合、Release 构建、governance、naming、`git diff --check` 与独立代码审查。
- [x] 记录精确命令、结果、未验证边界；只声明 SerialNumbers Host API 闭环，不外推为 Worker/Migrator Native AOT。
