# Identity 运行时数据范围并集验证记录

- 日期：2026-07-21
- 切片：多角色数据范围并集、`IUserDataScopeResolver`、租户机构只读查询过滤

## 交付范围

| 层级 | 内容 |
|---|---|
| 契约 | `EffectiveUserDataScope`、`IUserDataScopeResolver`、`IDataScopeSqlFilterBuilder` |
| Identity | `UserDataScopeResolver`、`DataScopeSqlFilterBuilder`、`BuildUnionOrganizationUnitFilter` |
| Organization | `TenantScopedSqlComposer`、机构列表/详情只读过滤 |
| 测试 | Unit +3 并集投影；Integration +2（双库 custom 范围过滤） |

## 门槛（本切片后）

| 套件 | 数量 |
|---|---|
| UnitTests | **322**（+3 并集投影） |
| Integration 双库 | **101**（+2 数据范围过滤 SQL Server/MySQL） |
| `pnpm test:openapi` | **14/14**（不变） |
| `pnpm test:clients` | **158**（不变） |
| `pnpm test:e2e` | **40**（不变） |

## 本地验证

| 命令 | 结果 |
|---|---|
| `dotnet build` | **通过** |
| UnitTests `--minimum-expected-tests 322` | **322/322** |
| Integration 双库 | 未在本地 Docker 执行；门槛 **101**（+2） |

## 非目标（按计划冻结）

- 全业务模块机构过滤
- 用户-机构隶属列表过滤
