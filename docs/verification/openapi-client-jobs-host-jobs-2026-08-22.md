# OpenAPI 客户端迁移：Jobs Host Jobs 切片验证（2026-08-22）

- 决策：`Slice-passed`（OpenAPI 契约与客户端门禁）；Integration 切片 76/78，Jobs 行为用例 2 项失败见备注
- 资源组：`jobs-host-jobs`（`ui/admin/src/api/host-jobs.ts`）
- 计划：[`2026-08-22-openapi-client-jobs-host-jobs.md`](../superpowers/plans/2026-08-22-openapi-client-jobs-host-jobs.md)
- 比较基线：`dde01b32`
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Jobs Host Jobs 切片已完成 OpenAPI 客户端迁移。10 个 Operation 具备稳定 `operationId` 与主 Tag `JobsHostJobDefinitions` / `JobsHostJobExecutions`；`host-jobs.ts` 已收缩为薄适配层。清单由 `pilot` 提升为 `generated`（现共 162 条）。

下一默认项为 `host-job-schedules.ts` 或 `host-job-health.ts`；禁止并行批量改写。

## 范围

| operationId | Vue 导出 |
| --- | --- |
| `jobsListHostJobDefinitions` | `listHostJobDefinitions` |
| `jobsListHostJobGroups` | `listHostJobGroups` |
| `jobsCreateHostJobDefinition` | `createHostJobDefinition` |
| `jobsUpdateHostJobDefinition` | `updateHostJobDefinition` |
| `jobsDisableHostJobDefinition` | `disableHostJobDefinition` |
| `jobsDeleteHostJobDefinition` | `deleteHostJobDefinition` |
| `jobsTriggerHostJobDefinition` | `triggerHostJobDefinition` |
| `jobsListHostJobExecutions` | `listHostJobExecutions` |
| `jobsGetHostJobExecution` | `getHostJobExecution` |
| `jobsClearHostJobExecutions` | `clearHostJobExecutions` |

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 通过 |
| `OpenApiOperationIdentityRulesTests` | 1/1，通过 |
| `npx vitest run src/api/host-jobs.test.ts` | 3/3，通过 |
| `pnpm test:integration:affected -- --base dde01b32 --phase slice` | 76/78；`JobsBatchFailureIsolationAssertions` 双 Provider 各 1 项失败（行为隔离，非 OpenAPI 契约） |

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
