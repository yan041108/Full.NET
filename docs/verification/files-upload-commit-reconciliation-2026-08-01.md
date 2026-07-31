# Files 上传提交不确定性对账验证记录（2026-08-01）

## 问题与关闭方式

旧上传顺序先发布 Blob，再提交活动元数据；`CommitAsync` 若已在数据库生效但客户端因断线未收到结果，外层异常补偿会删除 Blob，形成活动元数据永久指向缺失对象。该风险不能通过二次查询、回滚返回或吞异常证明提交结果。

本切片以可恢复状态机替换猜测性补偿：

1. 在独立事务写入对用户不可见的 `pending` 元数据；
2. 在独立事务按 `Id + ProviderKey + StorageKey + pending` 条件取得 `publishing` 发布所有权；
3. Provider 原子发布完整 Blob，再按 `publishing` 条件转为 `ready` 并在同一事务回读；
4. 任何事务异常都不再删除 Blob；所有列表、详情、下载和软删除只接受 `ready`；
5. Worker 对超过最小年龄的中间态做幂等对账：`pending` 按 Blob 证据提升或清理；`publishing` 有 Blob 时提升，无 Blob 时保留，避免慢上传与清理竞态。

因此首次或发布所有权提交结果不确定时尚未写 Blob；末次提交结果不确定时 Blob 被保留，数据库停在 `publishing` 或已成为 `ready` 都不会形成活动元数据断 Blob。

## Worker 与安全边界

- `Files:UploadReconciliation` 默认启用，默认批大小 100、单轮最多 10 批、最小年龄与轮询间隔均为 300 秒；范围均在启动时校验。
- Runner 使用 `(CreatedAtUtc, Id)` 稳定游标，只处理 Host、未删除、过龄的 `pending` / `publishing`。
- Provider 增加最终对象存在性探测；本地实现只观察最终路径，不把 `.uploading` 暂存文件视为已发布。
- 未知 Provider、探测异常或缺 Blob 的 `publishing` 保留到下轮/人工恢复，禁止回退默认 Provider 或误删元数据。
- 条件提升/清理受影响行只能为 0 或 1；0 计为并发完成，负数或多行直接 fail-closed。
- HostedProcessor 只由 Worker Profile 注册，并显式设置/清理 Host 租户上下文；日志只记录有界汇总值和数据库 Provider。
- 每轮日志显式输出 `RetainedPublishing`；该值持续大于零表示存在提交不确定或中断发布，运维必须按 Provider/StorageKey 核对最终对象后人工提升或清理，不得自动删除。

## 048 双库迁移

`048_FilesUploadState` 为 SQL Server/MySQL 增加 ASCII 二进制比较、非空 `StorageState`，把存量与半完成空值回填为 `ready`，并以检查约束只允许 `pending` / `publishing` / `ready`。未知非空状态不会被静默改写，约束建立会 fail-closed。

恢复测试分别从“列已存在但可空、空值、检查约束缺失且 SchemaVersions 未记录”的状态重跑；两库均收敛列形状、回填、约束和迁移记录。048 已登记到 affected migration selection，避免新迁移安全降级为本地全 migrations。

## TDD 与验证证据

- 上传提交边界 RED：首次提交不确定时旧实现已调用 Blob 保存；末次提交不确定时旧实现没有第二阶段事务。最小状态机实现后 2/2 GREEN。
- 对账器 RED：生产 Reconciliation 边界缺失导致编译失败；实现有界 Runner 后聚焦 4/4 GREEN。
- Files Unit：39/39；fresh 全 Unit：924/924，Release build 0 warning / 0 error。
- 048 SQL Server/MySQL 半完成恢复：2/2；047+048 affected inner：4/4。
- Files SQL Server/MySQL API 真实栈：2/2，覆盖 pending 不可见、存在对象提升、缺失对象清理、正常上传 ready、下载、列表、删除和墓碑清理。
- Integration Release build：0 warning / 0 error；fresh discovery 为 full 255，分片 43 / 43 / 86 / 83，无遗漏或重复。
- Architecture：50/50；naming：24/24；Integration tooling：39/39；governance：16/16。

最终 affected slice 为 Files + 047 + 048，SQL Server/MySQL 合并 6/6；Integration Release build 0 warning / 0 error。Architecture 50/50、naming 24/24、governance 16/16、`git diff --check` 均通过。Testcontainers/Ryuk 自然退出后 Docker running/residual=0、shared runner=0。独立最终复审 Ready，Critical=0、Important=0。完整 Integration 仍只由 `main` CI 的互斥并行分片执行。
