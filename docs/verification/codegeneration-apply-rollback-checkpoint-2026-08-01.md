# CodeGeneration Apply 回滚检查点验证记录

## 结论

Host Apply 现在会在首次修改生成工作区之前，原子发布不可覆盖的本地回滚检查点。检查点以 Apply 运行标识隔离，保存计划提交的 Manifest、Apply 前受管 Manifest 及其逐文件原始内容，并在读取时重新验证路径、Schema 与 SHA-256。

本切片只建立后续受控回滚所需的证据基础，不提供 Rollback API、权限、Vue/Layui 操作、保留清理、跨实例恢复或生产启用，因此代码生成总体能力仍保持 `Build-verified`。

## 范围与基线

- 日期：2026-08-01。
- 任务快照：`codegeneration-apply-rollback-checkpoint-20260801`。
- 基线 HEAD：`975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`。
- 检查点位置：`CodeGeneration:Apply:WorkspaceRoot` 下的 `.fullnet/codegeneration-rollback-checkpoints/{applyRunId:N}`。
- 未修改 HTTP/JSON 契约、权限、数据库结构、迁移、客户端或默认启用状态。

## 安全与一致性边界

- 仅无冲突的 `GenerationWritePlan` 可发布检查点；检查点成功发布后才允许工作区 Apply。
- 同一 Apply 运行标识不得覆盖或复用既有证据；临时目录未完整发布时不视为有效检查点。
- Apply 前受管文件缺失、内容摘要漂移、元数据损坏、大小写别名、符号链接或 reparse point 均 fail-closed。
- 检查点文件在原子目录发布前使用 write-through 并强制落盘；读取会再次验证计划 Manifest、旧 Manifest 的独立摘要、旧内容目录及逐文件 SHA-256，不信任磁盘上的既有内容。
- 运行记录写入 `running` 后，请求取消仍使用不可取消令牌收敛为 `failed`；若工作区已进入不可逆提交阶段，则先完成 Manifest，再以不可取消令牌收敛 `succeeded`。
- 数据库中成功收敛的 Apply 运行仍是未来回滚资格的权威；仅存在本地检查点不代表可执行回滚。

## 新鲜验证证据

| 验证 | 结果 |
| --- | --- |
| checkpoint Store 聚焦 Unit | 9/9 |
| Apply/Run/Workspace/Checkpoint 聚焦 Unit | 57/57 |
| fresh Unit discovery | 914，已同步唯一测试矩阵 |
| `pnpm test:naming` | 24/24 |
| Integration 工具链 / 治理契约 | 39/39 / 16/16 |
| Integration Release build | 0 warning / 0 error |
| Integration 分片发现覆盖 | 253（SQL Server API 43、MySQL API 43、migrations 84、infrastructure 83），无遗漏或重复 |
| affected inner | CodeGeneration + 047 + Realtime 39/39 |
| affected slice | CodeGeneration + Files + 047 + Realtime 41/41 |
| 独立安全/恢复复审 | Ready；Critical 0、Important 0 |

首轮 affected inner 精确捕获既有真实栈断言仍假定工作区只有产物与 Manifest，SQL Server/MySQL 均为 expected 14 / actual 15；断言改为读取并验证检查点运行标识、空旧状态、计划 Manifest 及精确文件数后，双库定向复跑 2/2，并由新鲜 inner/slice 完整复验。完整 Unit 与完整 Integration 集合未在本地重复执行，继续由 `main` CI 的互斥分片门禁负责。

## 未交付项

- Rollback HTTP API、独立权限、审计响应与 Vue/Layui 双端入口。
- 回滚状态机、并发互斥、重复回滚幂等和失败恢复。
- 检查点保留期、容量上限、加密/备份与安全清理。
- 多实例共享存储、Worker/队列调度、远程仓库写入及生产发布演练。
