# CodeGeneration 产品 Rollback 首切片设计

**状态：** Approved for implementation  
**日期：** 2026-08-02  
**基线：** `main` @ `b2bba13`  
**上游建议稿：** [codegeneration-product-rollback-assessment-2026-08-02.md](../../verification/codegeneration-product-rollback-assessment-2026-08-02.md)  
**适用范围：** Host 代码生成运行目录、本地 Apply 工作区、双库迁移、Vue/Layui 管理端

## 1. 决策摘要

Full.NET 已具备 Apply 前置不可变检查点与内部 `GenerationRollbackWorkspace` 逆向执行器。本设计把它们编排为**产品 Rollback**：以数据库中成功收敛的 Apply 运行为唯一资格权威，通过独立权限与摘要化 HTTP 契约暴露给 Host 管理员，并在 Vue/Layui 同步提供确认操作。

本切片只吸收“受控回滚已成功 Apply”的产品意图，不引入保留清理、多实例调度、远程仓库或生产默认启用。总体能力在双端真实栈验收前保持 `Build-verified`。

## 2. 资格与失败关闭

1. 仅当 `fn_codegeneration_run` 存在 `Id = applyRunId AND OperationKind = 'apply' AND Status = 'succeeded'` 时允许发起 Rollback。
2. 必须能通过 `GenerationRollbackCheckpointStore.ReadAsync(WorkspaceRoot, applyRunId)` 读取完整检查点；缺失、损坏、摘要漂移均返回稳定错误码，零工作区写入。
3. `CodeGeneration:Apply:Enabled` 必须为 true，且使用与 Apply 相同的绝对本地 `WorkspaceRoot`；客户端不得指定路径。
4. 同一 `applyRunId` 若已存在 `OperationKind = 'rollback' AND Status = 'succeeded'`，拒绝再次 Rollback（首切片不交付链式/重复回滚）。
5. 工作区当前 Manifest 必须逐字等于检查点 `AppliedManifest`；文件缺失、摘要漂移、大小写别名、reparse point 在进入写盘前失败关闭。
6. 本地仅有检查点但 DB 非 succeeded，不得回滚。

## 3. HTTP 与权限

| 项 | 约定 |
| --- | --- |
| 方法/路径 | `POST /api/v1/code-generation/runs/rollback` |
| 权限 | 新增 `codegen.runs.rollback`（Host scope）；不得复用 `apply`/`execute` |
| 请求 | `{ "applyRunId": "<uuid>" }`；禁止额外成员 |
| 成功响应 | `{ "runId", "applyRunId", "artifactCount", "changedArtifactCount", "manifestSha256" }`；不返回路径、Schema、源码、异常正文 |
| 查询 | 既有 `GET /runs` 与 `GET /runs/{id}` 须能列出/展示 `operationKind=rollback` 摘要 |

稳定错误码（扩展 `CodeGenerationRunErrorCodes`）：

- `codegen.run.rollback_disabled` — Apply/Rollback 配置未启用
- `codegen.run.invalid_rollback_apply` — 目标不是 succeeded Apply
- `codegen.run.rollback_already_applied` — 该 Apply 已有成功 Rollback
- `codegen.run.rollback_checkpoint_missing` — 检查点不可用
- `codegen.run.rollback_conflict` — 工作区漂移或写盘冲突
- `codegen.run.rollback_busy` — 与 Apply 共享互斥门占满
- `codegen.run.rollback_failed` — 其他受控失败

## 4. 数据模型（迁移 051）

继续使用表 `fn_codegeneration_run`，不新建回滚表。

1. 扩展 `OperationKind` 允许 `'rollback'`。
2. 新增可空列 `SourceApplyRunId uniqueidentifier/BINARY(16)`：
   - `preview`/`apply` 必须为 NULL；
   - `rollback` 必须非空，并引用既有 Apply 运行 Id（应用层校验；可不建跨库外键若双库迁移成本过高，但必须有索引与唯一成功约束）。
3. 成功 Rollback 行保留摘要字段（`ModuleKey`/`EntityKey`/`SchemaSha256`/`ArtifactCount`/`ManifestSha256`），语义为**回滚后**工作区 Manifest；允许 `ArtifactCount = 0`（首个 Apply 回滚到空 Manifest）。
4. 失败 Rollback 与 Apply 相同：清空摘要，写入 `ErrorCode`。
5. 唯一过滤约束（或等价双库实现）：同一 `SourceApplyRunId` 至多一条 `OperationKind='rollback' AND Status='succeeded'`。
6. 原 Apply 行保持 `succeeded` 只读证据，不被改写为 failed。

状态机：`Insert(running)` → 工作区 `RestoreAsync` → `Complete`/`Fail`；终态 SQL 必须 `WHERE OperationKind='rollback' AND Status='running'` 且 affected = 1。

## 5. 编排

`CodeGenerationRollbackService`：

1. 解析 actor 与请求 `applyRunId`。
2. 校验配置启用与 DB 资格（含“尚未成功回滚”）。
3. 进入与 Apply **共享**的 `CodeGenerationApplyGate`。
4. 分配新 `runId`，插入 `operationKind=rollback/status=running`，写入 `SourceApplyRunId`。
5. `ReadAsync` → `RestoreAsync`；冲突计划不得调用写盘，映射为 `rollback_conflict`。
6. 成功则 `Complete`（摘要取自恢复后 Manifest）；失败用不可取消令牌收敛 `failed`。
7. 不删除检查点目录；不写 Outbox；不查询其它模块表。

复用：`GenerationRollbackCheckpointStore`、`GenerationRollbackWorkspace`、`GenerationWorkspaceStore`、现有 Options/Validator。禁止复制锁/暂存/Manifest-last 算法。

## 6. 双管理端

- `packages/client-contracts` 增加 Rollback 请求/响应与权限常量。
- Vue / Layui 在 succeeded Apply 详情或列表操作区提供“回滚”确认；仅当当前用户具备 `codegen.runs.rollback` 时可见。
- 两端一致处理 403/409/稳定错误码；不展示服务器路径。
- 真实栈 E2E：启用 Apply 工作区 → Apply → Rollback → 断言产物回到检查点旧状态且 DB 有 rollback/succeeded 行。

## 7. 审计与安全

- 使用既有 Host 操作日志/领域审计边界；审计正文不得包含工作区绝对路径、Schema JSON、生成源码或异常堆栈。
- OpenAPI 与 Architecture 权限门禁必须覆盖新 Endpoint。
- Rollback 默认随 `CodeGeneration:Apply:Enabled` 关闭；生产启用须运维显式配置。

## 8. 验收

必须证明：

- 非 succeeded Apply、重复成功 Rollback、检查点缺失、Manifest 漂移均为零写入；
- 混合 create/update/delete Apply 可字节级恢复；首个 Apply Rollback 留下规范空 Manifest；
- SQL Server/MySQL 051 半完成恢复；
- 独立权限 403 矩阵；双端确认流与真实栈 E2E；
- 文档区分内部执行器与产品 Rollback，能力在 E2E 前不标 `Verified`。

## 9. 非目标

检查点保留/清理、容量配额、加密备份、多实例共享盘、Worker 调度、远程 Git、链式回滚、Rollback 后删除检查点、默认生产启用、G4 大型模块。