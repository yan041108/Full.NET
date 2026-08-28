# Dapper Infrastructure Native AOT Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 Host.Api 可达的 Dapper Outbox、会话锁和 producer fence SQL 参数与结果投影在 Native AOT 下形成静态闭包，并由 SQL Server/MySQL 原生产物真实验证。

**Architecture:** 保留现有 Outbox 状态机、Lease、积压快照、会话锁连接所有权和 fence 语义，只把匿名参数替换为固定键名字典或已注册绑定器，并为现有行类型注册物化器。原生 E2E 读取交付状态积压快照，避免空结果假绿。

**Tech Stack:** .NET 10、Dapper 自有执行边界、MSTest、SQL Server、MySQL、Linux Native AOT。

**Baseline:** `b6e6f3b50c9da2ca8a543f22d49d17c1e1554da8`

**Task snapshot:** `native-aot-dapper-infra-20260828`

## Global Constraints

- SQL Server 与 MySQL 必须同时验证，禁止改变 Outbox、租约、切流或公开 API 语义。
- Host.Api 可达参数必须使用稳定键名字典、`DynamicParameters` 或已注册参数类型，结果 DTO 必须使用静态 materializer。
- 原生运行证据必须真实执行积压查询；普通 JIT 构建不能替代 Linux 原生产物。
- 仅提交 Data.Dapper、对应 Architecture/Integration 测试和本计划/验证记录；保留无关工作区状态（含 Auditing/Messaging 空 diff）。

---

### Task 1: 建立 Dapper 基础设施静态闭包 RED 门禁

**Files:**
- Modify: `tests/Full.NET.ArchitectureTests/NativeAotStaticBindingRulesTests.cs`

- [x] **Step 1:** 写入失败测试，扫描 Outbox Store、会话锁、fence reader 的匿名参数，并要求基础设施注册行物化器
- [x] **Step 2:** 运行 RED，确认因匿名参数与缺失 materializer 失败

### Task 2: 最小化修复参数与物化闭包

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/DapperSqlParameters.cs`
- Modify: `DapperOutboxStore.cs`、`DapperDatabaseSessionLock.cs`、`DapperEventDeliveryProducerFencePositionReader.cs`、`DapperAotInfrastructureRegistration.cs`

- [x] **Step 1:** 固定键参数工厂；会话锁使用 `DynamicParameters`
- [x] **Step 2:** 替换全部匿名 SQL 参数；`OutboxAcquireParameters` 注册绑定器
- [x] **Step 3:** 嵌套行类型改为 internal，并在基础设施 Contributor 注册物化器
- [x] **Step 4:** Architecture GREEN

### Task 3: 原生双库非空运行证据并提交

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeApiE2EAssertions.cs`
- Create: `docs/verification/2026-08-28-native-aot-dapper-infrastructure-closure.md`

- [x] GET `/api/v1/messaging/delivery-status` 必须返回积压摘要与非空事件流列表
- [x] analyzer、Linux publish、双库原生 5/5、inner、governance、审查后提交
