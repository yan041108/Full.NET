# Identity Native AOT Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 Host.Api 可达的 Identity SQL 参数与结果投影在 Native AOT 下形成静态闭包，并由 SQL Server/MySQL 原生产物真实验证。

**Architecture:** 保留现有 `IQueryExecutor`、`ICommandExecutor`、SQL、事务、Outbox、超级管理员与字段投影边界，只把匿名参数替换为模块内固定键名字典，并为现有 typed 命令注册 AOT 参数绑定。原生 E2E 在 Host 作用域创建用户，重复用户名冲突，分页与按 ID 读回，避免空结果假绿。

**Tech Stack:** .NET 10、Dapper 自有执行边界、MSTest、SQL Server、MySQL、Linux Native AOT。

**Baseline:** `f1d2c48350b6581bc3eff2d92b478753299a9258`

**Task snapshot:** `native-aot-identity-20260828`

## Global Constraints

- SQL Server 与 MySQL 必须同时验证，禁止改变 SQL、事务、租户隔离、权限或公开 API 语义。
- Host.Api 可达参数必须使用稳定键名字典或已注册参数类型，结果 DTO 必须使用静态 materializer。
- 原生运行证据必须读取本次创建的非空行；普通 JIT 构建不能替代 Linux 原生产物。
- 仅提交 Identity、对应 Architecture/Integration 测试和本计划/验证记录；保留无关工作区状态（含 Auditing 空 diff）。

---

### Task 1: 建立 Identity 静态闭包 RED 门禁

**Files:**
- Modify: `tests/Full.NET.ArchitectureTests/NativeAotStaticBindingRulesTests.cs`

- [x] **Step 1: 写入失败测试** `IdentityModule_UsesAotSafeSqlParameters`、`IdentityModule_RegistersAllNativeAotRowMaterializers`
- [x] **Step 2: 运行 RED**，确认因匿名参数与缺失 materializer 失败

### Task 2: 最小化修复参数与物化闭包

**Files:**
- Create: `src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySqlParameters.cs`
- Modify: Identity 查询/管理服务、认证、投影与目录
- Modify: `IdentityDapperAotMaterializerContributor.cs`

- [x] **Step 1:** 固定键参数工厂
- [x] **Step 2:** 替换全部 `new { ... }` SQL 参数；创建路径空 `UpdatedAtUtc` 改为 SQL `NULL`
- [x] **Step 3:** 注册全部行物化器；可变投影按列名读取
- [x] **Step 4:** Architecture GREEN

### Task 3: 原生双库非空运行证据并提交

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeApiE2EAssertions.cs`
- Create: `docs/verification/2026-08-28-native-aot-identity-module-closure.md`

- [x] 创建唯一 Host 用户，重复用户名冲突，列表与按 ID 命中
- [x] analyzer、Linux publish、双库原生 5/5、inner、governance、审查后提交
