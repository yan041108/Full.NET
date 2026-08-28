# Organization Native AOT Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 Host.Api 可达的 Organization SQL 参数与结果投影在 Native AOT 下形成静态闭包，并由 SQL Server/MySQL 原生产物真实验证。

**Architecture:** 保留现有 `IQueryExecutor`、`ICommandExecutor`、SQL、事务、Outbox 和机构数据范围边界，只把匿名参数替换为模块内固定键名字典，并为现有 typed insert record 注册 AOT 参数绑定。原生 E2E 在进入 `local` 租户后创建机构、职级和职位，分页与按 ID 读回，避免空结果假绿。

**Tech Stack:** .NET 10、Dapper 自有执行边界、MSTest、SQL Server、MySQL、Linux Native AOT。

**Baseline:** `066bcbf3e15e0cf8d3c560f5b6317cf311542432`

**Task snapshot:** `native-aot-organization-20260828`

## Global Constraints

- SQL Server 与 MySQL 必须同时验证，禁止改变 SQL、事务、租户隔离、数据范围或公开 API 语义。
- Host.Api 可达参数必须使用稳定键名字典或已注册参数类型，结果 DTO 必须使用静态 ordinal materializer。
- 原生运行证据必须读取本次创建的非空行；普通 JIT 构建不能替代 Linux 原生产物。
- 仅提交 Organization、对应 Architecture/Integration 测试和本计划/验证记录；保留无关工作区状态（含 Auditing 空 diff）。

---

### Task 1: 建立 Organization 静态闭包 RED 门禁

**Files:**
- Modify: `tests/Full.NET.ArchitectureTests/NativeAotStaticBindingRulesTests.cs`

- [x] **Step 1: 写入失败测试** `OrganizationModule_UsesAotSafeSqlParameters`、`OrganizationModule_RegistersAllNativeAotRowMaterializers`
- [x] **Step 2: 运行 RED**，确认因匿名参数与缺失物化器/insert binder 失败

### Task 2: 最小化修复参数与物化闭包

**Files:**
- Create: `src/Modules/Full.NET.Modules.Organization/Persistence/OrganizationSqlParameters.cs`
- Modify: Organization 查询/管理服务、数据范围投影、目录 Port
- Modify: `OrganizationDapperAotMaterializerContributor.cs`

- [x] **Step 1:** 固定键参数工厂；数据范围 `DataScopeUserId` 改为字典
- [x] **Step 2:** 替换全部 `new { ... }` SQL 参数
- [x] **Step 3:** 注册全部行物化器与五个 Insert record 绑定器
- [x] **Step 4:** Architecture GREEN

### Task 3: 原生双库非空运行证据并提交

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeApiE2EAssertions.cs`
- Create: `docs/verification/2026-08-28-native-aot-organization-module-closure.md`

- [x] 创建唯一机构/职级/职位，重复编码冲突，列表与按 ID 命中
- [x] analyzer、Linux publish、双库原生 5/5、inner、governance、审查后提交
