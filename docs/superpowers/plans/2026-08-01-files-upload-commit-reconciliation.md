# Files 上传提交不确定性对账状态机实施计划

> **For Codex:** 在共享工作区内按本计划串行执行；任务快照固定为 `files-upload-commit-reconciliation-20260801`，不提交、不暂存、不覆盖其它窗口变更。

**Goal:** 消除上传事务提交结果不确定时“数据库已经提交活动元数据、服务却补偿删除 Blob”的永久断链风险，并让所有中间状态可由 Worker 幂等收敛。

**Architecture:** `fn_files_file.StorageState` 使用稳定机器码 `pending` / `publishing` / `ready`。上传先提交不可见的 `pending` 元数据，再以条件事务取得 `publishing` 发布所有权，随后原子发布 Blob，最后转为 `ready` 并回读；任何事务异常都不再触发 Blob 删除。Worker 扫描超过最小年龄的中间态：未认领的 `pending` 可按对象证据提升或清理，`publishing` 有对象时提升、无对象时保留，禁止把慢上传误判为失败。所有外部读、列表、下载和软删除只允许 `ready`。

**Tech Stack:** .NET 10、MSTest、Dapper 显式 SQL、DbUp、SQL Server、MySQL、Testcontainers。

## 范围与不变量

- 仅修改 Files 模块、`048_FilesUploadState` 双库迁移、对应 Unit/Integration 与 Files 专属计划/验证记录。
- 不修改公共 HTTP/JSON 响应，不引入外部对象存储实现，不修改 047，不接管 Admin Task7、Jobs、Realtime 或 CodeGeneration 文件。
- 第一次提交异常时不得调用 Blob 保存；即使数据库实际提交，最多留下不可见 `pending`。
- Blob 保存前必须以条件事务取得 `publishing` 所有权；未取得所有权不得发布对象。
- Blob 保存失败、请求取消或最后一次提交异常时不得猜测删除数据库行；`publishing` 无对象记录必须保留，等待人工判定或后续安全协议。
- 对账更新/删除必须同时匹配 `Id + ProviderKey + StorageKey + pending`，受影响行只能为 0 或 1；异常计数 fail-closed。
- 最小年龄必须阻止 Worker 与仍在执行的上传竞争；未知 Provider 或探测异常保留记录等待下轮，不得回退默认 Provider。

## Task 1：以 RED 固定上传提交边界

**Files:**
- Modify: `tests/Full.NET.UnitTests/Files/HostFileManagementServiceTests.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Features/ManageHostFiles/HostFileManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Persistence/HostFileSql.cs`

1. 新增事务替身：执行 action 后抛异常，用于模拟服务端可能已提交但客户端未收到确认。
2. RED：首次 `InsertPending` 提交异常时，断言 `SaveAsync` 从未发生。
3. RED：最终 `MarkReady` action 已执行后提交异常时，断言 Blob 仍存在且未调用 `DeleteAsync`。
4. RED：插入或状态转换受影响行不为 1 时抛出，并且不越过当前阶段边界。
5. 最小实现上传顺序：提交 pending → 条件取得 publishing → 保存 Blob → 提交 ready + ready 回读；删除旧的上传异常 Blob 补偿。
6. 运行 Files 管理服务聚焦测试，确认 RED 原因准确后转 GREEN。

## Task 2：数据库可见性与 048 双库恢复

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/048_FilesUploadState.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/048_FilesUploadState.sql`
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration048FilesUploadStateRecoveryTests.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Persistence/HostFileSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Persistence/HostFileRecord.cs`
- Modify: `tests/Full.NET.UnitTests/Files/HostFileQueryServiceTests.cs`

1. RED：活动列表、计数、详情和软删除 SQL 必须精确过滤 `StorageState = 'ready'`。
2. SQL Server/MySQL 048 增加 ASCII/BIN2、非空 `StorageState`，存量及半完成空值回填 `ready`，并建立只允许 `pending/publishing/ready` 的检查约束。
3. 双库恢复测试覆盖：正常升级、列已存在但可空/空值、约束缺失的未记账半完成状态；重跑后列形状、回填、约束和 SchemaVersions 均收敛。
4. 运行 naming、迁移 048 SQL Server/MySQL 聚焦测试并记录新鲜结果。

## Task 3：实现过龄 pending 对账 Worker

**Files:**
- Create: `src/Modules/Full.NET.Modules.Files/Reconciliation/PendingHostFileReconciliationOptions.cs`
- Create: `src/Modules/Full.NET.Modules.Files/Reconciliation/PendingHostFileReconciliationRunner.cs`
- Create: `src/Modules/Full.NET.Modules.Files/Reconciliation/PendingHostFileReconciliationHostedProcessor.cs`
- Create: `tests/Full.NET.UnitTests/Files/PendingHostFileReconciliationTests.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Storage/IFileStorageProvider.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Storage/LocalHostFileBlobStorage.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Persistence/HostFileSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Persistence/HostFileRecord.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/FilesModule.cs`

1. RED：禁用时零数据库/Provider 调用；未到最小年龄不入候选；`pending + Blob` 条件提升；`pending + 无 Blob` 条件清理；`publishing + 无 Blob` 必须保留。
2. RED：未知 Provider、探测异常保留 pending；取消向上传播；0 行视为并发完成，负数或多行 fail-closed。
3. 为 Provider 增加明确、可取消的存在性探测契约，本地实现只检查最终对象且不观察 staging 文件。
4. Runner 使用稳定 `(CreatedAtUtc, Id)` 游标和有界批次；HostedProcessor 设置 Host scope、记录无高基数标签的汇总日志。
5. 绑定并验证 `Files:UploadReconciliation` 的启用、批大小、轮数、轮询秒数和最小年龄；只在 Worker Profile 注册循环。
6. 运行对账、Provider 与 Files 全聚焦 Unit，确认全部 GREEN。

## Task 4：真实栈闭环与交付

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/Files/FilesHostFileManagementAssertions.cs`
- Create: `docs/verification/files-upload-commit-reconciliation-2026-08-01.md`
- Modify: `docs/verification/files-storage-provider-boundary-2026-08-01.md`
- Modify: `eng/testing/test-matrix.json`（仅在最终 fresh discovery 确认门槛变化时）

1. 双 Provider API/数据库断言覆盖：pending 对用户不可见、ready 可读、条件转换和清理不跨 Host/Provider/StorageKey 边界。
2. 运行 `pnpm test:integration:affected:plan -- --snapshot files-upload-commit-reconciliation-20260801 --phase inner`，执行选中 Files/048 影响集。
3. 运行 Files Unit、模块/Integration Release build、Architecture、naming、governance、`git diff --check`。
4. fresh Unit discovery 后只在实际变化时更新 `eng/testing/test-matrix.json`，再复跑门槛。
5. 运行 `pnpm test:integration:affected -- --snapshot files-upload-commit-reconciliation-20260801 --phase slice`；等待 Testcontainers/Ryuk 自然退出并确认 shared runner、Docker running/residual 均为 0。
6. 更新验证记录，执行 rule/skill evolution 检查；明确释放 shared .NET/Docker 与 048，并按队列交棒 Admin Task7。
