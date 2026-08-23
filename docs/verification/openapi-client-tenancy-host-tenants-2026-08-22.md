# OpenAPI 客户端迁移：Tenancy Host Tenants 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`tenancy-host-tenants`（`ui/admin/src/api/tenants.ts`）
- 计划：[`2026-08-22-openapi-client-tenancy-host-tenants.md`](../superpowers/plans/2026-08-22-openapi-client-tenancy-host-tenants.md)
- 比较基线：`f3017de9`（计划提交）
- 完成提交基线：`c77969ce`（Task 3 薄适配）
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Tenancy/Organization 首切片（Host Tenants）已通过完整门禁。6 个 Operation 具备稳定 `operationId` 与主 Tag `TenancyHostTenants`（含 201 Created 与 GET by id）；标准快照与生成物零漂移，`tenants.ts` 已收缩为薄适配层并保留手写 `isHostTenant`/`isHostTenantPage`。清单由 `pilot` 提升为 `generated`（现共 76 条）。

下一默认项为独立计划 **`org-units.ts`**（Organization）；禁止并行迁移其他资源组，禁止修改 `ui/admin-layui`。

## 范围与提交

| 提交 | 内容 |
| --- | --- |
| `f3017de9` | 建立 Tenancy Host Tenants 迁移计划 |
| `af7c7627` | 稳定 operationId、主 Tag、Produces 与 ProblemDetails |
| `53c68364` | 冻结标准快照、登记 6 条 pilot、同步生成物 |
| `c77969ce` | 将 `tenants.ts` 收缩为生成 Operation 薄适配层 |
| （本 Verification） | 6 条清单升为 `generated` 并记录 `Slice-passed` |

| operationId | Vue 导出 |
| --- | --- |
| `tenancyListHostTenants` | `listHostTenants` |
| `tenancyGetHostTenant` | （仅生成） |
| `tenancyCreateHostTenant` | `createHostTenant` |
| `tenancyUpdateHostTenant` | `updateHostTenant` |
| `tenancyDisableHostTenant` | `disableHostTenant` |
| `tenancyAssignHostTenantPackage` | `assignHostTenantPackage` |

## 验证环境

Windows NT 10.0.19045.0、Node.js 24.12.0、pnpm 10.26.0、.NET SDK 10.0.400。

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 111/111，通过 |
| `pnpm --filter @fullnet/client-contracts test` | 138/138，通过 |
| `pnpm --filter @fullnet/client-contracts build` | 退出码 0 |
| `pnpm --filter @fullnet/admin exec vitest run src/api/tenants.test.ts` | 4/4，通过 |
| `pnpm --filter @fullnet/admin exec vitest run --pool=forks --maxWorkers=2` | 127 文件 / 463 项，全部通过 |
| `pnpm --filter @fullnet/admin build` | 退出码 0 |
| `pnpm audit:clients` | 退出码 0 |
| `pnpm test:naming` | 30/30，通过 |
| `pnpm test:governance` | 38/38，通过 |
| `dotnet build Full.NET.slnx -c Release` | 退出码 0，0 warning、0 error |
| `pnpm test:integration:affected -- --base f3017de9 --phase slice` | Tenancy 10/10，SQL Server/MySQL 双 Provider，通过 |

## 边界与未验证项

- 页面导出签名未改；可选 `tenantPackageId` 仍仅在有值时写入创建正文。
- 手写 identifier/域名守卫保留在薄适配层；`tenancyGetHostTenant` 已生成但未新增 Vue 导出。
- 未迁移 `tenant-packages` / Organization；未修改 `ui/admin-layui`。

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
