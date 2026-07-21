# SQL 破坏性变更静态门禁验证记录

- 日期：2026-07-21
- 切片：架构硬化 Task 2 — SQL 安全静态扫描与限期豁免

## 交付范围

| 层级 | 内容 |
|---|---|
| 扫描器 | `scripts/sql/validate-sql-safety.mjs`（`FNSAFETY001`–`004`） |
| 豁免 | `contracts/sql-safety/waivers.json`（精确文件/行号/`actual` + `backupVerified`） |
| 入口 | `eng/sql-lint.ps1`（命名 + 安全串联）、`pnpm test:sql-safety` |
| 命名补强 | 登记 `OrganizationSql` / `SeedExecutionStore`；修正 SQL Server `015` PK 同行命名；`015` 动态 SQL 债务 |

## 规则边界

- **不重复**命名门禁的 `SELECT *` / 对象命名 / 迁移配对（仍由 `pnpm test:naming`）
- 安全门禁覆盖：无 `WHERE` 的 `UPDATE`/`DELETE`、`TRUNCATE`、`DROP TABLE/COLUMN`、直接 `RENAME`
- CTE `UPDATE Pending`（Outbox 租约）不误报

## 本地验证

| 命令 | 结果 |
|---|---|
| `node --test tests/sql/sql-safety.test.mjs` | **5/5 通过** |
| `node --test tests/naming/sql-naming.test.mjs` | **6/6 通过** |

## 门槛

C# Unit/Integration 数量不变（**322 / 103**）。新增 Node 安全套件 5 项，由 CI `pnpm test:sql-safety` 强制。
