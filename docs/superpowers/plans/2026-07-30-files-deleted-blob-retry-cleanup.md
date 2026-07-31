# Files 软删除 Blob 重试清理实施计划

> **For agentic workers:** 本计划在当前共享脏工作区内按
> `fullnet-module-delivery`、`superpowers:systematic-debugging` 与
> `superpowers:test-driven-development` 逐项执行。任务快照为
> `files-deleted-blob-retry-20260730`。

**Goal:** Worker 小批量重试删除已经软删除但首次物理删除失败的 Host Blob，并且仅在确认 Blob
不存在后清除对应墓碑。

**Architecture:** `FilesModule.AddBackgroundServices` 注册默认关闭的清理器。Runner 以
`DeletedAtUtc + Id` 游标从双库读取 Host 软删除墓碑，逐个调用幂等 Blob 删除；成功后以
`Id + StorageKey + DeletedAtUtc IS NOT NULL` 硬删除墓碑，单个 Blob 失败只计入结果并继续推进，
数据库失败和宿主取消继续向上传播。无需新增数据库对象或迁移。

**Tech Stack:** .NET 10、Dapper 抽象、SQL Server/MySQL、`BackgroundService`、MSTest。

## Global Constraints

- 只修改 Files 模块、Files Unit/既有双库聚焦断言、Files 运维/验证文档和本计划。
- 不修改 Worker `Program.cs`/`appsettings.json`、Jobs、Realtime、Notifications、
  CodeGeneration、共享路线图或 `eng/testing/test-matrix.json`。
- 清理默认关闭；启用时必须同时提供有效 `Files:Local:RootPath`。
- Blob 删除必须先于墓碑硬删除；Blob 删除失败、数据库失败或取消不得丢失可重试墓碑。
- 单次运行只处理 `BatchSize * MaxBatchesPerRun` 个候选，禁止无界扫描。
- 同一轮使用 `DeletedAtUtc + Id` 游标越过失败项，避免一个坏 Blob 阻塞后续候选；失败项留到下一轮。
- 多 Worker 重复选择同一墓碑必须保持幂等：Blob 缺失视为成功，墓碑已被并发删除时不报业务失败。
- 本切片只处理数据库已知的软删除 Host Blob，不扫描无元数据的历史文件系统孤儿，不支持租户文件。

---

### Task 1: 配置与 Worker Profile 注册

**Files:**

- Create: `src/Modules/Full.NET.Modules.Files/Cleanup/DeletedHostFileBlobCleanupOptions.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/FilesModule.cs`
- Create: `tests/Full.NET.UnitTests/Files/DeletedHostFileBlobCleanupTests.cs`

**Interfaces:**

- Produces: `DeletedHostFileBlobCleanupOptions`，配置节 `Files:Cleanup`。
- Produces: `FilesModule.AddBackgroundServices(IServiceCollection, IConfiguration)`。

- [x] **Step 1: 写入默认值与启动校验 RED**

  测试 `Enabled=false`、`BatchSize=100`、`MaxBatchesPerRun=10`、
  `PollSeconds=300`；越界值必须被 `IStartupValidator` 拒绝，启用但未配置
  `Files:Local:RootPath` 也必须被拒绝。

- [x] **Step 2: 运行聚焦测试确认 RED**

  Run:
  `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~DeletedHostFileBlobCleanupTests"`

  Expected: 因清理 Options 与后台注册尚不存在而编译失败。

- [x] **Step 3: 实现最小配置与后台注册**

  Options 范围：

  - `BatchSize`: `1..1000`
  - `MaxBatchesPerRun`: `1..100`
  - `PollSeconds`: `5..86400`

  `AddBackgroundServices` 只注册 Files 本地存储、清理 Runner 和 Hosted Processor；API
  `AddServices` 不启动后台循环。

- [x] **Step 4: 运行配置聚焦 GREEN**

  Expected: 配置与注册测试通过，Worker Profile 不需要修改宿主入口。

### Task 2: 双库墓碑查询与失败隔离 Runner

**Files:**

- Create: `src/Modules/Full.NET.Modules.Files/Cleanup/DeletedHostFileBlobCleanupRunner.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Persistence/HostFileRecord.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Persistence/HostFileSql.cs`
- Modify: `tests/Full.NET.UnitTests/Files/DeletedHostFileBlobCleanupTests.cs`

**Interfaces:**

- Consumes: `IQueryExecutor`、`ICommandExecutor`、`IHostFileBlobStorage`、
  `IOptions<DatabaseOptions>`。
- Produces:
  `Task<DeletedHostFileBlobCleanupResult> RunOnceAsync(DeletedHostFileBlobCleanupOptions, CancellationToken)`。

- [x] **Step 1: 写入顺序、游标与失败隔离 RED**

  测试第一页包含失败 Blob 与成功 Blob、第二页包含后续 Blob。断言：

  1. 每个成功项先调用 Blob 删除，再执行墓碑硬删除；
  2. 失败 Blob 不执行墓碑删除，但游标仍推进到下一页；
  3. SQL Server/MySQL 分别选择自己的分页 Statement；
  4. 并发已删除导致受影响行数为 `0` 时保持幂等；
  5. 宿主取消传播且不删除墓碑。

- [x] **Step 2: 运行 Runner 测试确认 RED**

  Expected: 因 Runner、记录类型与 SQL Statement 尚不存在而失败。

- [x] **Step 3: 实现最小 Runner 与 SQL**

  双库查询固定筛选 `TenantId IS NULL AND DeletedAtUtc IS NOT NULL`，按
  `DeletedAtUtc ASC, Id ASC` 排序并使用 `@HasCursor`、`@AfterDeletedAtUtc`、
  `@AfterId` 和 `@BatchSize`。墓碑删除必须精确匹配 `Id`、`StorageKey` 且仍处于软删除状态。

- [x] **Step 4: 运行 Files Unit GREEN**

  Run:
  `Full.NET.UnitTests.exe --filter "FullyQualifiedName~Full.NET.UnitTests.Files" --minimum-expected-tests 1 --progress off`

  Expected: 新增清理测试与既有本地存储测试全部通过。

### Task 3: Hosted Processor 与真实双库恢复链

**Files:**

- Create: `src/Modules/Full.NET.Modules.Files/Cleanup/DeletedHostFileBlobCleanupHostedProcessor.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Properties/AssemblyInfo.cs`
- Modify: `tests/Full.NET.IntegrationTests/Files/FilesHostFileManagementAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/FilesApiSqlServerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/FilesApiMySqlTests.cs`

**Interfaces:**

- Consumes: Worker Scope、`CurrentTenantAccessor`、动态 Options、Runner。
- Verifies: SQL Server/MySQL 中首次 Blob 删除失败后，Runner 第二次删除 Blob 并硬删除墓碑。

- [x] **Step 1: 实现 Host Context 后台循环**

  默认关闭时不创建 Scope；启用时每轮建立 Scope、设置 Host Context、调用 Runner，并在
  `finally` 清理上下文。单轮失败写结构化日志后等待下一周期；宿主取消退出循环。

- [x] **Step 2: 扩展既有双库断言**

  既有 API 删除后根据墓碑重建“Blob 仍存在”的同步删除失败残态，随后由真实 Runner 重试。
  SQL Server/MySQL 两个既有测试方法均断言：

  - API 删除已提交且下载返回 `404`；
  - 重试清理实际删除 Blob；
  - 墓碑硬删除；
  - 第二次运行返回零处理，证明幂等。

- [x] **Step 3: 串行运行 Files 双库聚焦**

  Run:
  `dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~FilesApiSqlServerTests"`

  Run:
  `dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~FilesApiMySqlTests"`

  Expected: SQL Server/MySQL 各自现有 Files 测试方法通过；结束后数据库容器与 Ryuk 全部退出。

### Task 4: 运维事实与完成门禁

**Files:**

- Modify: `docs/operations/files-local-storage.md`
- Create: `docs/verification/files-deleted-blob-retry-cleanup-2026-07-30.md`
- Modify: `docs/superpowers/plans/2026-07-30-files-deleted-blob-retry-cleanup.md`

**Interfaces:**

- Produces: 启用配置、失败恢复、并发幂等、剩余历史孤儿边界与新鲜验证证据。

- [x] **Step 1: 更新运维和验证记录**

  明确 `Files:Cleanup` 默认关闭；启用后只清理已软删除 Host 墓碑。无数据库墓碑的历史孤儿仍按
  只读差异清单处理，不能写成已自动覆盖。

- [x] **Step 2: 运行完成门禁**

  Run:
  `dotnet build src/Modules/Full.NET.Modules.Files/Full.NET.Modules.Files.csproj -c Release --no-restore --nologo`

  Run:
  `pnpm test:naming`

  Run:
  `pnpm test:integration:affected:plan -- --snapshot files-deleted-blob-retry-20260730 --phase inner`

  Run:
  `git diff --check`

  Expected: Files 构建、聚焦 Unit、双库 Files、Naming 与静态差异检查通过；影响集只按 Files
  边界执行，其他窗口在快照后的改动不由本窗口接管。

## Self-Review

- 需求覆盖：首次物理删除失败可重试；成功后才清墓碑；单项失败不饿死后续候选；取消与数据库失败
  不丢失恢复证据；多实例重复处理幂等。
- 范围覆盖：未新增迁移、公共 API/JSON、Worker 宿主配置、前端或其它模块改动。
- 非目标：无墓碑历史孤儿扫描、S3/OSS、租户文件、生产默认启用和完整 Integration 矩阵仍开放。
- 本计划不授权暂存、提交或推送共享工作区变更。
