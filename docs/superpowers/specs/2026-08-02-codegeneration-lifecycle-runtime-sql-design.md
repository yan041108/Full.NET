# CodeGeneration 生命周期 SQL 运行时语义矩阵设计

**状态：** Approved for implementation  
**日期：** 2026-08-02  
**基线：** `main` @ `4505e40`  
**上游建议稿：** [codegeneration-lifecycle-runtime-sql-assessment-2026-08-02.md](../../verification/codegeneration-lifecycle-runtime-sql-assessment-2026-08-02.md)  
**适用范围：** `Full.NET.Data.CodeGeneration` 生成 SQL、Integration Testcontainers 双库

## 1. 决策摘要

Admin.NET 生命周期吸收已在生成层产出 `Single` + `SoftDelete` 的 Insert/Update/Delete/Query SQL。本设计在**不扩展 HTTP 产品面**的前提下，用固定 `FullNetCrudSchema` fixture 生成产物，在隔离 SQL Server/MySQL 库执行 DDL 与 DML 矩阵，证明乐观并发、软删过滤与租户谓词在运行时成立。

首切片仅覆盖 `SoftDelete`；`Immutable`/`HardDelete-only` 变体与 Tree/聚合场景继续 fail-closed。

## 2. 矩阵语义

在共享 fixture（`TenantRequired` + `Version` + 创建/更新/删除审计列）上，双库各执行：

1. **Create** — Insert 成功写入 `Version=1`、`IsDeleted=0`。
2. **Update** — 匹配 `Id`/`TenantId`/`Version`/`IsDeleted=0` 时成功且 `Version` 递增。
3. **Stale Version** — 陈旧 `Version` 更新影响 0 行。
4. **SoftDelete** — `Delete` 语句置 `IsDeleted=1` 并递增 `Version`。
5. **Post-delete read** — `FindById`（含 `IsDeleted=0`）不再返回行。
6. **Post-delete update** — 对已软删行更新影响 0 行。

## 3. 测试边界

- 使用 `CrudArtifactGenerator.Generate` 的**生成产物**作为唯一 SQL 来源；禁止手写与生成器分叉的 DML。
- DDL 使用生成迁移模板；在 Integration 内通过 Dapper 直接执行，不新增公共 `SqlScriptRunner`。
- 测试置于 `tests/Full.NET.IntegrationTests/CodeGeneration/`；复用 `SharedDatabaseFixture` 隔离库。
- 不修改 legacy tenant `CatalogProduct` fixture 行为。

## 4. 验收

- SQL Server/MySQL 各 1 条矩阵测试 GREEN。
- `pnpm test:naming` 与 CodeGeneration affected integration inner 通过。
- 独立验证记录；`codegeneration-adminnet-lifecycle` 可升至 `Build-verified`（非 `Verified`）。

## 5. 非目标

HTTP API、双端 UI、新迁移编号、EF Core、Tree/MasterDetail 可执行生成、HardDelete-only 矩阵。