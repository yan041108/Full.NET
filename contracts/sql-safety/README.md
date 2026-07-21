# SQL 破坏性变更豁免

本目录存放机器可检查的 SQL 安全豁免，供 [`scripts/sql/validate-sql-safety.mjs`](../../scripts/sql/validate-sql-safety.mjs) 使用。

## 规则

| 规则码 | 含义 |
|---|---|
| `FNSAFETY001` | `UPDATE`/`DELETE` 缺少 `WHERE` |
| `FNSAFETY002` | `TRUNCATE TABLE` |
| `FNSAFETY003` | `DROP TABLE` / `DROP COLUMN` |
| `FNSAFETY004` | 直接 `RENAME` |

命名类违规（`SELECT *`、对象命名、迁移配对）仍由 `pnpm test:naming` 负责，本门禁不重复实现。

## 豁免条目必填字段

`contracts/sql-safety/waivers.json` 每条必须包含：

- `ruleId`、`file`、`line`、`actual`
- `reason`、`risk`、`reviewer`、`removalMilestone`
- `backupVerified: true`（没有备份/验证证据不得登记）

匹配必须精确到同文件、同行号、同 `actual`；禁止目录级或通配豁免。

## 使用

```powershell
pnpm test:sql-safety
# 或
pwsh -File eng/sql-lint.ps1
```
