# OpenAPI 客户端迁移：Identity Host API Keys 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`identity-host-api-keys`（`ui/admin/src/api/api-keys.ts`）
- 计划：[`2026-08-22-openapi-client-identity-host-api-keys.md`](../superpowers/plans/2026-08-22-openapi-client-identity-host-api-keys.md)
- 比较基线：`91e16e49`（Task 1 代码变更前）
- 完成提交基线：`47e45730`（Task 3 薄适配）
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Identity remaining 第 3 slice（Host API Keys）已通过完整门禁。4 个 Operation 具备稳定 `operationId` 与主 Tag `IdentityHostApiKeys`，标准快照与生成物零漂移，`api-keys.ts` 已收缩为薄适配层。清单条目由 `pilot` 提升为 `generated`（现共 54 条）。允许按队列新建下一个单模块计划（默认 `online-sessions.ts`）；禁止并行迁移其他资源组，禁止修改 `ui/admin-layui`。

## 范围与提交

| 提交 | 内容 |
| --- | --- |
| `91e16e49` | 建立 Identity Host API Keys 迁移计划 |
| `66965b03` | 稳定 API Keys 的 operationId、主 Tag、Produces 与 ProblemDetails |
| `e1b1b43e` | 冻结标准快照、登记 4 条 pilot、同步生成物 |
| `47e45730` | 将 `api-keys.ts` 收缩为生成 Operation 薄适配层 |
| （本 Verification） | 4 条清单升为 `generated` 并记录 `Slice-passed` |

| operationId | Vue 导出 |
| --- | --- |
| `identityListHostApiKeys` | `listHostApiKeys` |
| `identityCreateHostApiKey` | `createHostApiKey` |
| `identityDisableHostApiKey` | `disableHostApiKey` |
| `identityRotateHostApiKey` | `rotateHostApiKey` |

## 验证环境

Windows NT 10.0.19045.0、Node.js 24.12.0、pnpm 10.26.0、.NET SDK 10.0.400。

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 109/109，通过 |
| `pnpm --filter @fullnet/client-contracts test` | 137/137，通过 |
| `pnpm --filter @fullnet/client-contracts build` | 退出码 0 |
| `pnpm --filter @fullnet/admin exec vitest run src/api/api-keys.test.ts` | 5/5，通过 |
| `pnpm --filter @fullnet/admin test` | 125 文件 / 455 项，全部通过 |
| `pnpm --filter @fullnet/admin build` | 退出码 0 |
| `pnpm audit:clients` | 退出码 0 |
| `pnpm test:naming` | 30/30，通过 |
| `pnpm test:governance` | 38/38，通过 |
| `dotnet build Full.NET.slnx -c Release` | 退出码 0，0 warning、0 error |
| `pnpm test:integration:affected -- --base 91e16e49 --phase slice` | Identity 30/30，SQL Server/MySQL 双 Provider，通过 |

说明：Task 4 的 verify snapshot 在 Task 3 已提交后创建，相对该 snapshot 无可验证代码 diff；切片 Integration 改用计划比较基线 `91e16e49`（Task 1 前）执行。受影响选择器本轮仅命中 Identity。

## 边界与未验证项

- 页面导出签名未改；一次性 `secret` 仍经手写守卫要求非空，因生成守卫仅校验 `string`。
- 未迁移 online-sessions、TOTP、Super Administrators、`me`、`module-catalog` 或 auth/session。
- 未修改 `ui/admin-layui`。
- 完整生成式 SDK / 全部 Vue API 迁移仍未完成。

## 规则与 Skill 复盘

本轮风险由 ADR-0007、OpenAPI 兼容门禁与既有测试覆盖；未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
