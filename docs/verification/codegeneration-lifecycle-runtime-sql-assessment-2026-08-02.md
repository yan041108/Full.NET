# CodeGeneration 生命周期 SQL 运行时语义矩阵评估建议稿

- 日期：2026-08-02
- 代码基线：`main` @ `7abfa48`
- 状态：**建议稿**（未经 Spec 批准，不得进入实施计划或生产代码）
- 上游证据：[Admin.NET 生命周期吸收验证](codegeneration-adminnet-lifecycle-2026-07-30.md)、[产品 Rollback 演进闭合](codegeneration-product-rollback-2026-08-02.md)

## 1. 结论

Admin.NET 生命周期吸收（Task 2）已在生成层确定性产出 `Single` 场景的 update、soft-delete、hard-delete SQL 与编译投影，并通过模板形状、SQL 安全与双 Provider 编译门禁。缺口是**尚未**把代表性生成 SQL **动态应用到隔离 SQL Server/MySQL 数据库**并执行运行时语义矩阵（乐观并发、软删过滤、硬删、审计字段由服务端赋值、租户隔离）。因此 CodeGeneration 生命周期能力仍保持 `Implemented`，不能标 `Verified`。

建议下一纵向切片交付 **双库运行时语义矩阵**：从固定 `FullNetCrudSchema` fixture 生成 SQL，在 Testcontainers 隔离库建表、执行 CRUD 序列并断言行状态；不扩展 HTTP 产品面、不引入 Tree/MasterDetail 可执行产物。

## 2. 建议纳入

1. **Fixture**：复用或最小扩展 `CatalogProduct` 类样例，显式 `SoftDelete` + `Version` + 审计列；双 Provider 各一套迁移 DDL + 生成 DML。
2. **矩阵**：Create → Update（`Version` 递增）→ SoftDelete（`IsDeleted=1` 后不可 Update）→ HardDelete（若生成）→ 并发冲突（陈旧 `Version` 零行影响）。
3. **断言**：租户参数绑定、组织列不可由客户端写入（生成 SQL 不含开放列）、软删查询默认过滤。
4. **测试**：Integration 双库各 1 类（或参数化）；RED 先行；不修改 legacy tenant fixture 行为。
5. **文档**：独立验证记录；`codegeneration-adminnet-lifecycle` 状态可升至 `Build-verified`（非 `Verified`，仍缺更广 E2E）。

## 3. 明确排除

- Tree / MasterDetail / ManyToMany 可执行生成
- Host 模板 Apply/Rollback 产品路径变更
- 新公共 HTTP API 或双端 UI
- EF Core 或通用 Repository

## 4. 未决问题（Spec 前）

1. 矩阵是否覆盖 `Immutable` 与 `HardDelete-only` 变体，或首切片仅 `SoftDelete`。
2. 是否在 Integration 内直接执行生成 SQL，还是通过小型 `SqlScriptRunner` 辅助（须遵守 Dapper 边界）。
3. 与现有 `ModuleIntegrationCompilationTests` 的 schema 文档复用程度。

## 5. 规则/Skill

未触发规则或 Skill 升级条件；实施时沿用 `fullnet-module-delivery` RED 先行与双库门禁。