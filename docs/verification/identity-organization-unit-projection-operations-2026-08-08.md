# Identity 机构投影运维闭环验证记录（2026-08-08）

## 范围

- Snapshot：`cursor-review-org-projection-operations-20260808`
- 基线：`00ff9a6bdcadc9a7b7ab02d8ec2b2207ca511740`
- 交付：Host 对账端点（dry-run/apply）、keyset 分页、断点续跑、双库集成验收

## 验证命令与结果

| 门禁 | 命令 | 结果 |
|---|---|---|
| Unit | `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~OrganizationUnitProjection` | 3/3 通过 |
| Integration（双库） | `dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter FullyQualifiedName~projection_reconciliation_follows_contract` | 2/2 通过 |
| OpenAPI | `pnpm test:openapi` | 83/83 通过 |
| Naming | `pnpm test:naming` | 24/24 通过 |
| Whitespace | `git diff --check` | 无冲突标记；仅 CRLF 提示 |

## 行为摘要

- `POST /api/v1/identity/organization-unit-projections/reconcile` 支持 `dry-run` 与 `apply`，权限分别门禁。
- keyset 以 `UnitId` 递增分页，单页最多 100 行；`HasMore` 采用 `pageSize+1` 探测避免末页误判。
- 对账仅修复缺失/过期投影，不因局部页遗漏删除本地行。
- 集成测试在租户上下文切换后使用独立 Host reconcile token，避免会话作用域污染导致 401。

## 规则复盘

未命中 `rules/rule-evolution.md` 升级条件。