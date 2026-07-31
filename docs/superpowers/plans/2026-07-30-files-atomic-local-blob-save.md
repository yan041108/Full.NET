# Files 本地 Blob 原子保存实施计划

> **For agentic workers:** 本计划在当前共享脏工作区内按
> `superpowers:systematic-debugging` 与 `superpowers:test-driven-development`
> 逐项执行。任务快照为 `files-orphan-blob-cleanup-20260730`。

**Goal:** 本地文件保存被取消或复制失败时不暴露部分最终 Blob，并清理本次写入的暂存文件。

**Architecture:** `LocalHostFileBlobStorage` 在最终对象所在目录创建唯一暂存文件，完整复制并
刷新后再用同目录 `File.Move(..., overwrite: false)` 发布最终对象。同目录移动保持既有
`CreateNew` 冲突语义；任一步骤失败都在 `finally` 中删除本次暂存文件，最终对象只会是完整内容
或不存在。

**Tech Stack:** .NET 10、异步 `FileStream`、MSTest。

## Global Constraints

- 只修改 Files 本地存储、Files 聚焦 Unit、Files 运维/验证文档和本计划。
- 不修改数据库、迁移、API、公共 JSON、Worker、Jobs、Realtime、CodeGeneration、双端路由或
  `eng/testing/test-matrix.json`。
- 保持对象键、最终路径、`FileMode.CreateNew` 冲突语义和调用方异常语义不变。
- 暂存文件必须与最终文件位于同一目录，禁止跨卷移动。
- 取消和复制异常时不得留下最终文件；正常文件系统条件下也不得留下暂存文件。
- 本切片不扫描或删除历史孤立 Blob，不把 Files 状态提升为 `Verified`。

---

### Task 1: 复现最终路径部分写入

**Files:**

- Create: `tests/Full.NET.UnitTests/Files/LocalHostFileBlobStorageTests.cs`

**Interfaces:**

- Consumes: `LocalHostFileBlobStorage.SaveAsync(string, Stream, CancellationToken)`
- Verifies: 取消发生后最终对象和本次暂存文件均不存在。

- [x] **Step 1: 写入取消 RED**

  使用独立临时根目录、预取消令牌和非空 `MemoryStream` 调用 `SaveAsync`，断言抛出
  `OperationCanceledException`，且根目录下没有任何文件。

- [x] **Step 2: 运行聚焦测试确认 RED**

  Run:
  `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~LocalHostFileBlobStorageTests"`

  Expected: 测试因当前实现已经创建零字节最终文件而失败，失败不是编译、环境或路径错误。

### Task 2: 暂存写入与原子发布

**Files:**

- Modify: `src/Modules/Full.NET.Modules.Files/Storage/LocalHostFileBlobStorage.cs`
- Modify: `tests/Full.NET.UnitTests/Files/LocalHostFileBlobStorageTests.cs`

**Interfaces:**

- Preserves: `IHostFileBlobStorage.SaveAsync` 签名和最终对象键。
- Produces: 同目录唯一 `*.uploading` 暂存文件，成功后使用
  `File.Move(stagingPath, fullPath, overwrite: false)` 发布。

- [x] **Step 1: 实现最小 GREEN**

  创建目标目录后生成同目录唯一暂存路径；只向暂存文件复制并刷新，关闭句柄后移动到最终路径；
  `finally` 删除仍存在的本次暂存文件。

- [x] **Step 2: 运行取消测试确认 GREEN**

  Run:
  `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~LocalHostFileBlobStorageTests"`

  Expected: 取消测试通过，根目录无文件。

- [x] **Step 3: 增加成功与冲突回归**

  增加成功保存内容完全一致且无暂存文件、最终对象已存在时不覆盖原内容且不残留暂存文件的测试。

- [x] **Step 4: 运行 Files 聚焦 GREEN**

  Run:
  `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~Full.NET.UnitTests.Files"`

  Expected: Files Unit 全部通过。

### Task 3: 运维事实与分层验证

**Files:**

- Modify: `docs/operations/files-local-storage.md`
- Create: `docs/verification/files-atomic-local-blob-save-2026-07-30.md`

**Interfaces:**

- Produces: 失败时最终对象不可见、历史孤立 Blob 仍需人工清单处理的准确运维边界。

- [x] **Step 1: 更新运维和验证记录**

  说明新上传使用同目录暂存与原子发布；上传中断不再产生部分最终对象，但历史孤立对象、软删除后
  物理删除失败及暂存删除自身失败仍归入现有清单流程。

- [x] **Step 2: 运行无容器验证**

  Run:
  `dotnet build src/Modules/Full.NET.Modules.Files/Full.NET.Modules.Files.csproj -c Release`

  Run:
  `pnpm test:naming`

  Run:
  `pnpm test:integration:affected:plan -- --snapshot files-orphan-blob-cleanup-20260730 --phase inner`

  Run:
  `git diff --check`

  Expected: Files 构建、聚焦 Unit、Naming 与静态差异检查通过；affected 只审查计划，不启动 Docker。

## Self-Review

- 需求覆盖：取消/复制失败不暴露部分最终 Blob；成功与已存在冲突语义均有回归。
- 占用边界：不修改并行窗口负责的 Jobs、Realtime、CodeGeneration 或共享路由。
- 非目标：未把历史孤立 Blob 自动清理写成已完成。
