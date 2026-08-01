# CodeGeneration 产品 Rollback 首切片评估建议稿

- 日期：2026-08-02
- 代码基线：`main` @ `de0080f`
- 状态：**已确认** → Spec [2026-08-02-codegeneration-product-rollback-design.md](../superpowers/specs/2026-08-02-codegeneration-product-rollback-design.md)（Approved for implementation）
- 上游证据：[回滚检查点与内部逆向执行验证](codegeneration-apply-rollback-checkpoint-2026-08-01.md)、[Host Apply 验证](codegeneration-host-apply-2026-07-31.md)

## 1. 结论

内部已验证检查点与 `GenerationRollbackWorkspace` 已具备，可在本地工作区对已读校验的 checkpoint 做 fail-closed 逆向写盘。**产品 Rollback 尚未开始**：无 HTTP/API、无独立权限、无 DB 回滚运行态、无 Vue/Layui 入口。

建议下一纵向切片交付「以 DB `apply/succeeded` 为资格权威、复用现有内部执行器、双管理端同步」的最小产品 Rollback；保留清理、多实例调度、远程仓库与生产默认启用继续排除。总体能力在切片关闭前仍保持 `Build-verified`；两端 E2E 通过前不得标 `Verified`。

## 2. 已具备的基础设施（不得重写）

| 能力 | 边界 |
| --- | --- |
| `GenerationRollbackCheckpointStore` | Apply 写盘前原子发布；路径 `{WorkspaceRoot}/.fullnet/codegeneration-rollback-checkpoints/{applyRunId:N}` |
| `GenerationRollbackWorkspace.PlanAsync/RestoreAsync` | 要求当前 Manifest ≡ `AppliedManifest`；无冲突时只调用 `GenerationWorkspaceStore.ApplyAsync`；不查 DB、不删检查点 |
| Host Apply | `CodeGeneration:Apply` opt-in、绝对本地 `WorkspaceRoot`、`CodeGenerationApplyGate` 单进程互斥、`fn_codegeneration_run` + 045/046 |
| 权限面（现有） | `codegen.runs.read` / `execute` / `apply`；无 `rollback` |

关键不变量：本地 checkpoint 存在 ≠ 可产品回滚；资格权威必须是数据库中成功收敛的 Apply 运行。

## 3. 建议的首切片范围

### 纳入

1. **资格**：仅 `fn_codegeneration_run` 中 `operationKind='apply' AND status='succeeded'` 可发起；checkpoint 缺失或 Manifest 漂移 fail-closed、零写入。
2. **HTTP**：`POST /api/v1/code-generation/runs/rollback`（请求仅 `applyRunId`）；响应摘要化（无路径/源码/异常正文）。
3. **权限**：新增 `codegen.runs.rollback`，Integration 覆盖 403 矩阵。
4. **编排**：`CodeGenerationRollbackService` — DB 资格 → `ReadAsync` → `RestoreAsync` → 写 rollback 运行终态；与 Apply **共享** `CodeGenerationApplyGate`。
5. **迁移**：成对扩展 `operationKind`（含 `rollback`）与 `running→succeeded/failed`；下一可用编号为 **051**（047–050 已占用）。
6. **双端**：`client-contracts`、Vue、Layui 在 succeeded Apply 上提供确认回滚；真实栈 E2E 对齐现有 Host Apply 预览路径。
7. **审计**：与现有 Host 操作日志/领域审计模式一致，不得把路径或源码写入审计正文。

### 明确排除

- 检查点保留期、容量上限、自动清理、加密/备份
- 多实例共享存储、Worker/队列、跨实例调度
- 远程 Git 操作、生产默认启用
- Rollback 后删除 checkpoint 证据
- 链式/重复回滚幂等产品化（可列后续切片）
- Document/Workflow 等 G4 大型模块

## 4. 推荐复制的既有模式

- 端点/权限/actor：`Features/ManageHostRuns/Endpoint.cs`
- 编排与互斥：`CodeGenerationApplyService` + `CodeGenerationApplyGate`
- 契约与错误码：`CodeGenerationRunContracts`
- OpenAPI / client-contracts / Vue / Layui：现有 `code-generation-runs` 面
- 双库恢复：参照 `Migration046CodeGenerationApplyRecoveryTests`

## 5. 验收与文档流转（批准后）

1. 用户确认本建议稿边界 → 写入带日期 Spec（批准状态显式）→ 再产生实施 Plan。
2. 实施必须 RED 先行；双库迁移/API、双端与真实栈 E2E 齐备前不得标 `Verified`。
3. Verification 须分层写清「内部执行器」与「产品 Rollback 首切片」，禁止把 checkpoint 存在写成产品已交付。

## 6. 未决问题（Spec 前需确认）

1. Rollback 运行是否写入同一 `fn_codegeneration_run`（推荐，扩展 `operationKind`），还是独立表。
2. 同一 Apply 是否允许第二次 Rollback（建议首切片拒绝已回滚或工作区已偏离的 Apply）。
3. Rollback 成功后原 Apply 行是否保持 `succeeded` 只读证据，另记 rollback run 关联 `applyRunId`（推荐）。

## 7. 规则/Skill

未触发规则或 Skill 升级条件；本文件仅为评估建议稿。