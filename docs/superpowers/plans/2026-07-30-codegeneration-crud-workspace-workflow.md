# CodeGeneration CRUD 工作区执行入口实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把确定性 CRUD 产物生成器、安全写盘计划器和真实工作区存储器组合成一个可直接调用的计划/应用入口，并在发现中断提交留下的 manifest recovery 时失败关闭。

**Architecture:** 在现有 `Full.NET.Data.CodeGeneration` 项目内新增无 CLI、无依赖注入的薄编排入口。入口只串联 `CrudArtifactGenerator`、`GenerationWorkspaceStore` 与 `GenerationWritePlanner`；冲突计划原样返回且不写盘。工作区存储器在捕获和应用前识别自身命名的 `.recovery` 文件并抛出冲突，避免强制终止后的残留状态被误当作无清单工作区。

**Tech Stack:** .NET 10、C#、System.IO、MSTest。

## Global Constraints

- 不新增项目、CLI、配置系统、数据库反向工程或模板注册中心。
- `PlanAsync` 必须只读；`ApplyAsync` 只有在 `GenerationWritePlan.CanApply` 为真时才能调用存储器。
- 相同 Schema 重复应用必须得到全部 `Unchanged`，且产物与清单字节不漂移。
- 手写文件冲突必须返回不可应用计划，不得创建 `.fullnet`、临时文件或部分产物。
- `.fullnet/codegeneration-manifest-*.recovery` 存在时必须抛出 `GenerationWorkspaceConflictException`；本切片只发现并阻断，不自动选择恢复版本。
- 保留现有受信工作区威胁模型，不增加平台 P/Invoke 或跨文件事务承诺。

---

### Task 1：CRUD 工作区计划与应用入口

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudGenerationWorkspace.cs`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/CrudGenerationWorkspaceTests.cs`
- Modify: `eng/testing/test-matrix.json`

**Interfaces:**
- Consumes: `FullNetCrudSchema`、`CrudArtifactGenerator.Generate`、`GenerationWorkspaceStore.CaptureAsync`、`GenerationWritePlanner.Plan`、`GenerationWorkspaceStore.ApplyAsync`.
- Produces: `CrudGenerationWorkspace.PlanAsync(string workspaceRoot, FullNetCrudSchema schema, CancellationToken cancellationToken = default)`.
- Produces: `CrudGenerationWorkspace.ApplyAsync(string workspaceRoot, FullNetCrudSchema schema, CancellationToken cancellationToken = default)`.

- [x] **Step 1：写入工作流 RED**

测试必须证明：空工作区规划得到五个 `Create` 且不产生 `.fullnet`；应用后五个产物与 manifest 完整落地；重复应用得到五个 `Unchanged`；手写文件冲突返回 `CanApply == false` 且零写入。

- [x] **Step 2：运行 RED**

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore
```

Expected: 因 `CrudGenerationWorkspace` 尚不存在而编译失败。

- [x] **Step 3：实现最小编排入口**

`PlanAsync` 生成确定性产物、捕获目标快照并返回计划；`ApplyAsync` 调用 `PlanAsync`，不可应用时直接返回计划，可应用时调用工作区存储器后返回同一计划。

- [x] **Step 4：运行 GREEN 并更新测试门槛**

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter FullyQualifiedName~CrudGenerationWorkspaceTests
```

依据完整 Unit 的新鲜发现数只更新 `eng/testing/test-matrix.json`。

### Task 2：中断 manifest recovery 失败关闭

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWorkspaceStore.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/GenerationWorkspaceStoreTests.cs`

**Interfaces:**
- Consumes: 写盘器固定 recovery 命名 `codegeneration-manifest-{guid}.recovery`.
- Produces: 捕获或应用遇到 recovery 时抛出 `GenerationWorkspaceConflictException`.

- [x] **Step 1：写入 recovery RED**

在 `.fullnet` 写入有效旧 manifest recovery，断言 `CaptureAsync` 抛出冲突且异常路径指向该 recovery；原 manifest、recovery 与产物均保持不变。

- [x] **Step 2：运行 RED**

```powershell
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter FullyQualifiedName~GenerationWorkspaceStoreTests
```

Expected: 当前捕获会忽略 recovery，因此测试失败。

- [x] **Step 3：实现最小 fail-closed 检测**

捕获在读取产物前检查 `.fullnet`；应用在取得工作区锁后再次检查。只识别写盘器自己的精确前缀和 `.recovery` 后缀，逐项复用路径大小写与 reparse point 检查，不删除或修改 recovery。

- [x] **Step 4：执行切片验证**

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter FullyQualifiedName~CodeGeneration
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll
pnpm test:integration:tooling
pnpm test:integration:affected:plan -- --snapshot codegeneration-crud-apply-20260730 --phase inner
pnpm test:integration:affected -- --snapshot codegeneration-crud-apply-20260730 --phase slice
git diff --check
git status --short
git branch --show-current
```

独立审查组合入口的零写入冲突语义、重复应用、recovery 误报范围、链接/大小写复用与取消传播；修复本切片引入的全部 Critical/Important 后记录实际结果。

## 实际验证结果

- Release 构建：0 警告，0 错误。
- `CrudGenerationWorkspaceTests + GenerationWorkspaceStoreTests`：13/13 通过；全部 CodeGeneration：60/60 通过。
- 完整 Unit：560/560 通过；Integration 工具链：30/30 通过。
- 影响集计划：`integration-matrix, smoke`。工具/治理 37/37、Integration 构建及 207 项分区一致性通过。
- Smoke：6/8；SQL Server/MySQL 两项迁移断言因共享工作区已有未跟踪迁移 033–036 使首次执行数由 1 变为 5 而失败，与本切片无关。
- 独立复审：Critical 0，Important 0。提交阶段在首个不可逆产物前保留最后取消点，进入后以不可取消的有限持锁阶段完成产物与 manifest。
