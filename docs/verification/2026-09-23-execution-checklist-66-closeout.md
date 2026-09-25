# 执行清单 66 — ImportExport 分批执行、错误回执、断点/重试 UI closeout（2026-09-23）

**范围**：清单 §7.C 编号 **66**（依赖 **65**；`BatchSize` 分批写入、`ImportExportTaskRunner` 租约与检查点、`execute` / `resume` / `retry` / `error-receipt` API；`ImportExportTasksView` 执行统计与抽屉操作；模块内 Organization 职位消费者写入，**不**跨模块本地事务）。

## 选定切片

| 项 | 值 |
|----|-----|
| Schema | `organization.tenant_positions` / `positions`（与 65 同消费者） |
| 执行配置 | `FullNet:ImportExport:BatchSize`（默认 50，1–200）；`RunSynchronously`（集成/开发可选，驱动 API 同步跑批） |
| Worker | `ImportExportTaskHostedProcessor` + `ImportExportTaskRunner` |
| 状态机 | `preview_succeeded` → `queued` → `executing` → `execution_succeeded` / `execution_partial` / `execution_failed` |

## 交付锚点

| 层 | 位置 |
|----|------|
| 执行 API | `POST …/tasks/{id}/execute`、`…/resume`、`…/retry` |
| 错误回执 | `GET …/tasks/{id}/error-receipt`（`ImportExportErrorReceiptRenderer` xlsx） |
| 服务 | `ImportExportTaskExecutionService`；持久化 `ImportExportTaskSql` 租约/检查点 |
| Vue | `ImportExportTasksView`（`executionStats`、execute/resume/retry/下载回执） |
| 权限 | `import_export.import_tasks.execute` |
| 集成 | `ImportExportTaskAssertions`（预览 + **execute** 至 `execution_succeeded`）；`ImportExportTaskClaimPersistenceAssertions`（双库领取） |
| 单元 | `ImportExportTaskRecoveryTests`（租约重领、取消不写失败）；`ImportExportErrorReceiptRendererTests` |
| 契约 | `import-export-v1.json`（`OpenApiImportExportContractAssertions`） |

## 清单 66 验收结论

- **已有**：分批 `ExecuteBatchAsync`；部分成功保留 `NextLineNumber` / `SucceededRowCount`；失败行生成 `ErrorReceiptFileId`；`resume` 不重置已成功行，`retry` 重置执行态。
- **本槽**：`phase-c-66-import-export-execute-error-receipt.spec.mjs`；`import-export-real-stack.mjs` 执行/回执辅助。
- **real-stack 说明**：默认 Host **未**开启 `RunSynchronously` 时 `execute` 可能返回 `queued`/`executing`，终态依赖 Worker；集成矩阵使用 `RunSynchronously=true` 断言全成功路径。
- **未验**：故意构造 `execution_partial` + UI resume/retry 点击（需可控失败行夹具或大批量断点）；Magicodes 级通用导出。

## 停止边界

- **67**：Reporting 数据源安全配置/测试 API（与 ImportExport 无耦合）。
- B-24 `/organization/positions/import` 固定 Excel 与平台 ImportExport **并行**，不互相替代。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~ImportExport` | **10/10**（`d40de4e6`） |
| `pnpm exec vitest run` `ImportExportTasksView.test.ts` | **3/3** |
| `node --test` `import-export-contract.test.mjs` | **1/1** |
| 集成 | `ImportExportApiSqlServerTests` / `ImportExportApiMySqlTests` + Claim 持久化测试 |
| real-stack | `phase-c-66-import-export-execute-error-receipt.spec.mjs`（需 Host + 租户 + 可选 Worker/同步配置） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
