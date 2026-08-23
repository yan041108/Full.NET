# OpenAPI 客户端迁移：Jobs Host Job Schedules 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`jobs-host-job-schedules`（`ui/admin/src/api/host-job-schedules.ts`）
- 计划：[`2026-08-22-openapi-client-jobs-host-job-schedules.md`](../superpowers/plans/2026-08-22-openapi-client-jobs-host-job-schedules.md)
- 比较基线：`dde01b32`
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Jobs Host Job Schedules 切片已通过 OpenAPI 客户端门禁。8 个 Operation 具备稳定 `operationId` 与主 Tag `JobsHostJobSchedules`；`host-job-schedules.ts` 已收缩为薄适配层。清单由 `pilot` 提升为 `generated`（现共 170 条）。

下一默认项为 `host-job-health.ts`；禁止并行批量改写。

## 范围

| operationId | Vue 导出 |
| --- | --- |
| `jobsListHostJobSchedules` | `listHostJobSchedules` |
| `jobsListHostJobScheduleDefinitionOptions` | `listHostJobScheduleDefinitionOptions` |
| `jobsPreviewHostJobScheduleCron` | `previewHostJobScheduleCron` |
| `jobsCreateHostJobSchedule` | `createHostJobSchedule` |
| `jobsUpdateHostJobSchedule` | `updateHostJobSchedule` |
| `jobsPauseHostJobSchedule` | `pauseHostJobSchedule` |
| `jobsResumeHostJobSchedule` | `resumeHostJobSchedule` |
| `jobsDeleteHostJobSchedule` | `deleteHostJobSchedule` |

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 通过 |
| `OpenApiOperationIdentityRulesTests` | 1/1，通过 |
| `npx vitest run src/api/host-job-schedules.test.ts` | 3/3，通过 |

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
