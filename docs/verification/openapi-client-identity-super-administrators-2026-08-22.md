# OpenAPI 客户端迁移：Identity Super Administrators 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`identity-super-administrators`（`ui/admin/src/api/superAdministrators.ts`）
- 计划：[`2026-08-22-openapi-client-identity-super-administrators.md`](../superpowers/plans/2026-08-22-openapi-client-identity-super-administrators.md)
- 比较基线：`2e92a929`（计划提交）
- 完成提交基线：`fc768bfc`（Task 3 薄适配）
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Identity remaining 第 8 slice（Super Administrators，敏感动作）已通过完整门禁。4 个 Operation 具备稳定 `operationId` 与主 Tag `IdentitySuperAdministrators`；权限、速率限制、密码/TOTP 仅 JSON 正文传递与最后一名保护语义未改。标准快照与生成物零漂移，`superAdministrators.ts` 已收缩为薄适配层。清单由 `pilot` 提升为 `generated`（现共 66 条）。

至此 Identity remaining 队列中的 `src/api` 模块均已 `Slice-passed`。下一默认项为独立计划 **auth/session**（logout `204`，不在 `src/api`）；禁止并行迁移其他资源组，禁止修改 `ui/admin-layui`。

## 范围与提交

| 提交 | 内容 |
| --- | --- |
| `2e92a929` | 建立 Super Administrators 迁移计划 |
| `7aa31972` | 稳定 operationId、主 Tag、Produces 与 ProblemDetails |
| `0bc35d87` | 冻结标准快照、登记 4 条 pilot、同步生成物 |
| `fc768bfc` | 将 `superAdministrators.ts` 收缩为生成 Operation 薄适配层 |
| （本 Verification） | 4 条清单升为 `generated` 并记录 `Slice-passed` |

| operationId | Vue 导出 |
| --- | --- |
| `identityListSuperAdministrators` | `getSuperAdministrators` |
| `identityListSuperAdministratorAudits` | `getSuperAdministratorAudits` |
| `identityGrantSuperAdministrator` | `grantSuperAdministrator` |
| `identityRevokeSuperAdministrator` | `revokeSuperAdministrator` |

## 验证环境

Windows NT 10.0.19045.0、Node.js 24.12.0、pnpm 10.26.0、.NET SDK 10.0.400。

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 110/110，通过 |
| `pnpm --filter @fullnet/client-contracts test` | 137/137，通过 |
| `pnpm --filter @fullnet/client-contracts build` | 退出码 0 |
| `pnpm --filter @fullnet/admin exec vitest run src/api/superAdministrators.test.ts` | 2/2，通过 |
| `pnpm --filter @fullnet/admin exec vitest run --pool=forks --maxWorkers=2` | 127 文件 / 461 项，全部通过 |
| `pnpm --filter @fullnet/admin build` | 退出码 0 |
| `pnpm audit:clients` | 退出码 0 |
| `pnpm test:naming` | 30/30，通过 |
| `pnpm test:governance` | 38/38，通过 |
| `dotnet build Full.NET.slnx -c Release` | 退出码 0，0 warning、0 error |
| `pnpm test:integration:affected -- --base 2e92a929 --phase slice` | Identity 30/30，SQL Server/MySQL 双 Provider，通过 |

说明：verify snapshot 在 Task 3 提交后创建，无可验证代码 diff；切片 Integration 使用计划比较基线 `2e92a929`。受影响选择器本轮仅命中 Identity。

## 边界与未验证项

- 页面导出签名未改；密码与可选 TOTP 仍只经 JSON 正文发送。
- 审计查询固定 `limit=50`；列表 GET 无尾斜杠。
- 手写列表/变更守卫保留在薄适配层。
- 未迁移 auth/session；未修改 `ui/admin-layui`。

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
