# OpenAPI 客户端迁移：Identity Auth Session 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`identity-auth-session`（`packages/client-contracts/src/identity-session.ts`）
- 计划：[`2026-08-22-openapi-client-identity-auth-session.md`](../superpowers/plans/2026-08-22-openapi-client-identity-auth-session.md)
- 比较基线：`c4b590ce`（计划提交）
- 完成提交基线：`b5c419e3`（Task 3 薄适配）
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Identity Auth Session（login / refresh / logout `204` / locale，外加会话加载路径的已有 `identityGetCurrentUser`）已通过完整门禁。4 个 Operation 具备稳定 `operationId` 与主 Tag `IdentityAuthSession`；AllowAnonymous 显式 `security: []`、manifest `publicOperationIds`、生成器可选 `RequestOptions` 透传与 HttpClient `headers` 合并均已落地。CSRF、`retryUnauthorized: false`、Cookie 会话与 ProblemDetails 语义未退化。标准快照与生成物零漂移，`identity-session.ts` 已收缩为薄适配层。清单由 `pilot` 提升为 `generated`（现共 70 条）。

Identity remaining（含 `src/api` 队列与本 auth/session）均已 `Slice-passed`。下一默认项为其余 Vue API 模块按单模块 slice 迁移；禁止并行批量改写，禁止修改 `ui/admin-layui`。

## 范围与提交

| 提交 | 内容 |
| --- | --- |
| `c4b590ce` | 建立 Auth Session 迁移计划 |
| `1abba505` | 稳定 operationId、主 Tag、Produces、AllowAnonymous `security: []` 与夹具 |
| `5b85a8fc` | 冻结标准快照、登记 `publicOperationIds` 与 4 条 pilot、透传 RequestOptions |
| `b5c419e3` | 将 `identity-session.ts` 收缩为生成 Operation 薄适配层 |
| （本 Verification） | 4 条清单升为 `generated` 并记录 `Slice-passed` |

| operationId | 适配点 |
| --- | --- |
| `identityLogin` | `createIdentitySession.login` |
| `identityRefreshSession` | `refreshAccessToken` |
| `identityLogout` | `logout`（`204`） |
| `identityUpdatePreferredLocale` | `changeLocale` |

## 验证环境

Windows NT 10.0.19045.0、Node.js 24.12.0、pnpm 10.26.0、.NET SDK 10.0.400。

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 111/111，通过 |
| `pnpm --filter @fullnet/client-contracts test` | 138/138，通过 |
| `pnpm --filter @fullnet/client-contracts build` | 退出码 0 |
| `pnpm --filter @fullnet/admin exec vitest run --pool=forks --maxWorkers=2` | 127 文件 / 462 项，全部通过 |
| `pnpm --filter @fullnet/admin build` | 退出码 0 |
| `pnpm audit:clients` | 退出码 0 |
| `pnpm test:naming` | 30/30，通过 |
| `pnpm test:governance` | 38/38，通过 |
| `dotnet build Full.NET.slnx -c Release` | 退出码 0，0 warning、0 error |
| `pnpm test:integration:affected -- --base c4b590ce --phase slice` | smoke 8/8 + Identity 30/30，SQL Server/MySQL 双 Provider，通过 |

## 边界与未验证项

- 适配权威在 `packages/client-contracts`；未新建 Vue `src/api` 文件，未改 vue-client-coverage 45 模块 1:1。
- 手写 Token / Locale / CurrentUser 守卫保留在会话层；生成守卫较弱处不降级。
- refresh/logout 经 `RequestOptions.headers` 注入 CSRF；login/refresh/logout 禁用 401 自动刷新。
- 未迁移 navigation / tenancy；未修改 `ui/admin-layui`。

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
