# OpenAPI 客户端迁移：Identity Me 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`identity-me`（`ui/admin/src/api/me.ts`）
- 计划：[`2026-08-22-openapi-client-identity-me.md`](../superpowers/plans/2026-08-22-openapi-client-identity-me.md)
- 比较基线：`c0acbc23`（计划提交）
- 完成提交基线：`df9dc0c1`（Task 3 薄适配）
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Identity remaining 第 6 slice（Me / `GET /api/v1/me`）已通过完整门禁。1 个 Operation 具备稳定 `operationId`（由历史夹具 `getCurrentUser` 收敛为 `identityGetCurrentUser`）与主 Tag `IdentityMe`，标准快照与生成物零漂移，`me.ts` 已收缩为薄适配层并补齐独立 Vue 单测。清单由 `pilot` 提升为 `generated`（现共 59 条）。允许按队列新建下一个单模块计划（默认 `totpEnrollment.ts`）；禁止并行迁移其他资源组，禁止修改 `ui/admin-layui`。

## 范围与提交

| 提交 | 内容 |
| --- | --- |
| `c0acbc23` | 建立 Me 迁移计划 |
| `9e7b77bc` | 稳定 operationId、主 Tag、Produces 与 ProblemDetails；夹具命名收敛 |
| `fa6cebe6` | 冻结标准快照、登记 1 条 pilot、同步生成物 |
| `df9dc0c1` | 补 RED 单测并将 `me.ts` 收缩为生成 Operation 薄适配层 |
| （本 Verification） | 1 条清单升为 `generated` 并记录 `Slice-passed` |

| operationId | Vue 导出 |
| --- | --- |
| `identityGetCurrentUser` | `getCurrentUser` |

## 验证环境

Windows NT 10.0.19045.0、Node.js 24.12.0、pnpm 10.26.0、.NET SDK 10.0.400。

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 110/110，通过 |
| `pnpm --filter @fullnet/client-contracts test` | 137/137，通过 |
| `pnpm --filter @fullnet/client-contracts build` | 退出码 0 |
| `pnpm --filter @fullnet/admin exec vitest run src/api/me.test.ts` | 2/2，通过 |
| `pnpm --filter @fullnet/admin test` | 127 文件 / 460 项，全部通过（默认并发曾出现多处 5s 超时，`--pool=forks --maxWorkers=2` 重跑通过；与本 slice 无关） |
| `pnpm --filter @fullnet/admin build` | 退出码 0 |
| `pnpm audit:clients` | 退出码 0 |
| `pnpm test:naming` | 30/30，通过 |
| `pnpm test:governance` | 38/38，通过 |
| `dotnet build Full.NET.slnx -c Release` | 退出码 0，0 warning、0 error |
| `pnpm test:integration:affected -- --base c0acbc23 --phase slice` | Identity 30/30，SQL Server/MySQL 双 Provider，通过 |

说明：verify snapshot 在 Task 3 提交后创建，无可验证代码 diff；切片 Integration 使用计划比较基线 `c0acbc23`。受影响选择器本轮仅命中 Identity。

## 边界与未验证项

- 页面导出签名未改；手写 `isCurrentUserResponse`（SupportedLocale、`profileVersion>0`）保留在薄适配层。
- 未纳入 `/api/v1/me/locale`、TOTP、Super Administrators 或 auth/session。
- 未修改 `ui/admin-layui`。

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
