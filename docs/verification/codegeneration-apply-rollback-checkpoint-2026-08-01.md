# CodeGeneration Apply 回滚检查点与内部逆向执行验证记录

## 结论

Host Apply 会在首次修改生成工作区之前，原子发布不可覆盖的本地回滚检查点。在此基础上，`GenerationRollbackWorkspace` 现可对已通过 `GenerationRollbackCheckpointStore.ReadAsync` 验证的检查点执行只读逆向规划，并在无冲突时复用唯一的 `GenerationWorkspaceStore.ApplyAsync` 写盘内核。

本切片交付的是内部已验证检查点回滚执行器，不是产品 Rollback。不提供 Rollback API、权限、Vue/Layui 操作、数据库成功权威判断、保留清理、跨实例调度或生产启用，因此代码生成总体能力仍保持 `Build-verified`。

## 范围与基线

- 日期：2026-08-01（检查点）/ 2026-08-02（内部逆向执行器）。
- 任务快照：`codegeneration-apply-rollback-checkpoint-20260801`、`codegeneration-rollback-workspace-20260802`。
- 检查点基线 HEAD：`975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`。
- 逆向执行器基线 HEAD：`373e48fc0bb70910ca12dcd50f413f272bb3a7cb`。
- 检查点位置：`CodeGeneration:Apply:WorkspaceRoot` 下的 `.fullnet/codegeneration-rollback-checkpoints/{applyRunId:N}`。
- 未修改 HTTP/JSON 契约、权限、数据库结构、迁移、客户端或默认启用状态。

## 安全与一致性边界

- 仅无冲突的 `GenerationWritePlan` 可发布检查点；检查点成功发布后才允许工作区 Apply。
- 同一 Apply 运行标识不得覆盖或复用既有证据；临时目录未完整发布时不视为有效检查点。
- Apply 前受管文件缺失、内容摘要漂移、元数据损坏、大小写别名、符号链接或 reparse point 均 fail-closed。
- 检查点文件在原子目录发布前使用 write-through 并强制落盘；读取会再次验证计划 Manifest、旧 Manifest 的独立摘要、旧内容目录及逐文件 SHA-256，不信任磁盘上的既有内容。
- 逆向规划要求当前磁盘 Manifest 逐字等于检查点 `AppliedManifest`，并要求 Applied 拥有路径仍存在且摘要未漂移；人工编辑、后续 Apply、文件缺失、大小写别名与 reparse point 在首个写入前阻塞。
- 逆向写盘只调用 `GenerationWorkspaceStore.ApplyAsync`，不复制锁、暂存、删除恢复或 Manifest-last 算法；成功后不删除检查点证据。
- 首个 Apply 的回滚目标是 schemaVersion 兼容的空 Manifest，表示当前无受管产物，不声称恢复 `.fullnet` 内部文件的字节级缺席状态。
- 运行记录写入 `running` 后，请求取消仍使用不可取消令牌收敛为 `failed`；若工作区已进入不可逆提交阶段，则先完成 Manifest，再以不可取消令牌收敛 `succeeded`。
- 数据库中成功收敛的 Apply 运行仍是未来回滚资格的权威；仅存在本地检查点或内部执行器不代表可执行产品回滚。

## 新鲜验证证据

| 验证 | 结果 |
| --- | --- |
| checkpoint Store 聚焦 Unit | 9/9 |
| rollback workspace + planner + store + checkpoint 聚焦 Unit | 55/55 |
| fresh Unit discovery | 1014，已同步唯一测试矩阵 |
| `pnpm test:naming` | 登记 050 精确债务后通过 |
| Integration Release build | 0 warning / 0 error |
| affected inner（CodeGeneration） | 30/30 |
| affected slice（CodeGeneration） | 30/30 |
| 独立安全/恢复复审 | Ready；Critical 0、Important 0（内部执行器无公共 API/权限面；fail-closed 与零写入断言已覆盖） |

完整 Unit 与完整 Integration 集合未在本地重复执行，继续由 `main` CI 的互斥分片门禁负责。

## 未交付项（已由后续切片交付）

下列能力不在本记录（内部执行器）范围内；产品级交付见 [codegeneration-product-rollback-2026-08-02.md](codegeneration-product-rollback-2026-08-02.md) 及演进切片索引（多实例互斥、远程 Git、检查点保留/容量、链式回滚、双端 UI、生产 Helm）。

- ~~Rollback HTTP API、独立权限、审计响应与 Vue/Layui 双端入口~~ → 已交付
- ~~回滚状态机、并发互斥、重复回滚幂等和失败恢复~~ → 已交付
- ~~检查点保留期、容量上限与安全清理~~ → 已交付（opt-in 删除检查点另见专用切片）
- ~~多实例共享存储、远程仓库写入及生产发布演练~~ → Helm/分布式 Gate/远程 Git 已交付
- ~~数据库成功权威与内部执行器的产品级编排~~ → 已交付