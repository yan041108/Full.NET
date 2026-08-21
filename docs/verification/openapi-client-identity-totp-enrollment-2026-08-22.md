# OpenAPI 客户端迁移：Identity TOTP Enrollment 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`identity-totp-enrollment`（`ui/admin/src/api/totpEnrollment.ts`）
- 计划：[`2026-08-22-openapi-client-identity-totp-enrollment.md`](../superpowers/plans/2026-08-22-openapi-client-identity-totp-enrollment.md)
- 比较基线：`636fb9fc`（计划提交）
- 完成提交基线：`b9902b5e`（Task 3 薄适配）
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Identity remaining 第 7 slice（TOTP Enrollment）已通过完整门禁。3 个 Operation 具备稳定 `operationId` 与主 Tag `IdentityTotpEnrollment`，标准快照与生成物零漂移，`totpEnrollment.ts` 已收缩为薄适配层。清单由 `pilot` 提升为 `generated`（现共 62 条）。允许按队列新建下一个单模块计划（默认 `superAdministrators.ts`，敏感动作需单独 Verification）；禁止并行迁移其他资源组，禁止修改 `ui/admin-layui`。

## 范围与提交

| 提交 | 内容 |
| --- | --- |
| `636fb9fc` | 建立 TOTP Enrollment 迁移计划 |
| `fde8725d` | 稳定 operationId、主 Tag、Produces 与 ProblemDetails |
| `b03acdd3` | 冻结标准快照、登记 3 条 pilot、同步生成物 |
| `b9902b5e` | 将 `totpEnrollment.ts` 收缩为生成 Operation 薄适配层 |
| （本 Verification） | 3 条清单升为 `generated` 并记录 `Slice-passed` |

| operationId | Vue 导出 |
| --- | --- |
| `identityGetTotpEnrollmentStatus` | `getTotpEnrollmentStatus` |
| `identityBeginTotpEnrollment` | `beginTotpEnrollment` |
| `identityConfirmTotpEnrollment` | `confirmTotpEnrollment` |

## 验证环境

Windows NT 10.0.19045.0、Node.js 24.12.0、pnpm 10.26.0、.NET SDK 10.0.400。

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 110/110，通过 |
| `pnpm --filter @fullnet/client-contracts test` | 137/137，通过 |
| `pnpm --filter @fullnet/client-contracts build` | 退出码 0 |
| `pnpm --filter @fullnet/admin exec vitest run src/api/totpEnrollment.test.ts` | 2/2，通过 |
| `pnpm --filter @fullnet/admin exec vitest run --pool=forks --maxWorkers=2` | 127 文件 / 461 项，全部通过 |
| `pnpm --filter @fullnet/admin build` | 退出码 0 |
| `pnpm audit:clients` | 退出码 0 |
| `pnpm test:naming` | 30/30，通过 |
| `pnpm test:governance` | 38/38，通过 |
| `dotnet build Full.NET.slnx -c Release` | 退出码 0，0 warning、0 error |
| `pnpm test:integration:affected -- --base 636fb9fc --phase slice` | Identity 30/30，SQL Server/MySQL 双 Provider，通过 |

说明：verify snapshot 在 Task 3 提交后创建，无可验证代码 diff；切片 Integration 使用计划比较基线 `636fb9fc`。受影响选择器本轮仅命中 Identity。

## 边界与未验证项

- 页面导出签名未改；手写非空 `sharedSecretBase32` / `otpAuthUri` 守卫保留在薄适配层。
- GET 客户端路径收敛为无尾斜杠 `/api/v1/identity/me/mfa/totp`（与 OpenAPI 路由一致）。
- 未迁移 Super Administrators 或 auth/session。
- 未修改 `ui/admin-layui`。

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
