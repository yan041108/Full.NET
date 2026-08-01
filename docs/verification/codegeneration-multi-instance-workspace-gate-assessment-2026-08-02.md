# CodeGeneration 多实例工作区互斥评估建议稿

- 日期：2026-08-02
- 代码基线：`main` @ `b2c80a3`
- 状态：**建议稿**（待用户确认边界后进入 Spec）
- 上游证据：[产品 Rollback 验证](codegeneration-product-rollback-2026-08-02.md)、[检查点保留清理验证](codegeneration-checkpoint-retention-2026-08-02.md)

## 1. 结论

`CodeGenerationApplyGate` 目前仅为 **单进程 `SemaphoreSlim`**，注释已声明跨实例留给后续。当 Kubernetes 多副本 API 或 API 与 Worker 共用同一 `WorkspaceRoot`（如 PVC/NFS）时，Apply、Rollback 与检查点清理可能并发写盘，破坏 Manifest-last 与检查点不可覆盖不变量。

下一合理切片是在 **不改动产品 HTTP 契约** 的前提下，为 Apply/Rollback（及可选的检查点清理）引入 **跨进程/跨实例的分布式互斥**，与现有 fail-closed 语义一致。

## 2. 现状与风险

| 项 | 边界 |
| --- | --- |
| 单进程 Gate | `CodeGenerationApplyGate` 仅 API 进程内串行 |
| 共享工作区 | `CodeGeneration:Apply:WorkspaceRoot` 为绝对本地路径；多 Pod 可挂载同一 PVC |
| Rollback | 与 Apply 共享 Gate；多实例下 Gate 不生效 |
| 检查点清理 | Worker `HostedProcessor` 无与 Apply/Rollback 的跨进程锁 |
| 检查点不变量 | 同一 `applyRunId` 禁止覆盖；并发 Apply 可能产生竞态 |

## 3. 建议的首切片范围

### 纳入

1. **锁边界**：以规范化 `WorkspaceRoot` 为锁键（稳定哈希/前缀），覆盖 `CodeGenerationApplyService` 与 `CodeGenerationRollbackService` 的写盘段；首切片可将 Worker 清理排除在锁外，但必须在 Spec 中写清与 Apply/Rollback 的 happens-before（推荐：清理仍依赖 Manifest 一致性跳过，不抢锁）。
2. **实现**：复用仓库既有 Redis/Backplane 或 FusionCache 双抽象下的分布式锁模式（若已有标准 helper）；禁止 ad-hoc 新中间件。
3. **超时与 fail-closed**：获取锁失败映射现有 `apply_busy` / `rollback_busy`；不得阻塞无限等待。
4. **配置**：`CodeGeneration:Apply:DistributedGateEnabled`（默认 `false`）；启用时要求 Backplane/Redis 可用。
5. **测试**：Unit 模拟锁竞争；Integration 双“实例”顺序 Apply（或锁替身）证明零并发写盘。

### 明确排除

- 远程 Git、生产默认启用
- 链式/重复 Rollback 产品化
- 多工作区根目录、跨租户路径
- NFS 语义专项硬化（仅文档声明需要 POSIX 原子 rename）
- Vue/Layui 变更

## 4. 未决问题（Spec 前需确认）

1. 锁实现选型：Redis `SET NX` + TTL、RedLock 包装，还是 DB 租约表？
2. Worker 检查点清理是否纳入同一锁（推荐首切片不纳入，靠 Manifest 跳过）。
3. 锁租约 TTL 与 Apply/Rollback 最长执行时间关系。

## 5. 验收与文档流转

用户确认 → Spec（Approved）→ Plan → RED 先行。不得标产品 Rollback `Verified`。

## 6. 规则/Skill

未触发规则或 Skill 升级条件；本文件仅为评估建议稿。
