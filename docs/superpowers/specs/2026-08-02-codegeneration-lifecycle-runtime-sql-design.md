# CodeGeneration 生命周期 SQL 运行时语义矩阵设计

**状态：** Approved — delivered  
**日期：** 2026-08-02  
**基线：** `main` @ `3e250fa`  
**上游建议稿：** [codegeneration-lifecycle-runtime-sql-assessment-2026-08-02.md](../../verification/codegeneration-lifecycle-runtime-sql-assessment-2026-08-02.md)  
**适用范围：** `Full.NET.Data.CodeGeneration` 生成 SQL、Integration Testcontainers 双库

## 1. 决策摘要

Admin.NET 生命周期吸收已在生成层产出 `Single` 场景多 DeleteMode 的 SQL。本设计在**不扩展 HTTP 产品面**的前提下，用固定 fixture 生成产物，在隔离 SQL Server/MySQL 库执行 DDL 与 DML 矩阵，证明乐观并发、软删过滤（含 Count/List）、物理删除与 Immutable 只读语义在运行时成立。

已交付：`SoftDelete`、`HardDelete`、`Immutable`。Tree/聚合场景继续 fail-closed。

## 2. 矩阵语义

### SoftDelete

Create → Update → Stale Version → SoftDelete → FindById 过滤 → Count/List 过滤 → Post-delete Update 零行 → 租户隔离。

### HardDelete

Create → Update → Stale Version → 物理 DELETE → FindById 为空 → 行计数为零。

### Immutable

生成 SQL 无 Update/Delete；Create → FindById 可读。

## 3. 测试边界

- 使用 `CrudArtifactGenerator.Generate` 产物为唯一 SQL 来源。
- DDL 使用生成迁移模板；Integration 内 Dapper 直接执行。
- 复用 `SharedDatabaseFixture` 隔离库。

## 4. 验收

- 双库矩阵 GREEN（6 项 Integration）。
- `codegeneration-adminnet-lifecycle` 升至 `Build-verified`。

## 5. 非目标

HTTP API、双端 UI、Tree/MasterDetail 可执行生成、OrganizationUnit 所有权运行时矩阵。