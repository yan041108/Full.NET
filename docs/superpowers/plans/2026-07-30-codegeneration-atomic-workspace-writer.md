# CodeGeneration 原子工作区写盘器实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把安全写盘计划接入真实工作区，以严格 UTF-8 快照、根目录与链接防护、协作锁、提交前二次校验、同目录临时文件替换和清单最后提交，安全落地生成产物。

**Architecture:** 在 `Full.NET.Data.CodeGeneration` 内新增工作区存储适配器，不创建新项目或 CLI。适配器先捕获目标产物与上一版清单，再由现有计划器分类；应用阶段持有工作区锁，先完成全量预检和临时文件暂存，再二次校验所有目标，最后逐个原子替换并在全部产物完成后提交清单。已有清单通过无覆盖 rename 先 claim 到唯一 recovery 文件，严格校验该真实版本后再把新清单无覆盖移入空位；编辑器在 claim 后重新保存时生成器绝不覆盖。多文件提交不伪装成文件系统事务；常规异常路径会恢复旧清单，无法无覆盖恢复时保留 recovery 证据。

**Tech Stack:** .NET 10、C#、System.IO、严格 UTF-8、SHA-256、MSTest。

## Global Constraints

- 工作区根目录必须已经存在，必须解析为绝对目录且自身不得是 reparse point。
- 产物路径继续服从 `GenerationArtifactPath` 的 NFC、可移植字符、Windows 设备名与大小写别名约束。
- 每个已存在的父目录和目标文件都不得是符号链接或 reparse point；任何实际大小写与计划路径不一致的别名都必须失败。
- 内部清单固定为 `.fullnet/codegeneration-manifest.json`，协作锁固定为 `.fullnet/codegeneration.lock`，生成产物不得占用这两个路径。
- 文本读取和写入统一使用无 BOM 严格 UTF-8；非法字节、输入 BOM、首字符 `U+FEFF` 或非法 UTF-16 必须失败。
- `Conflict` 计划不得产生目录、锁文件、临时文件或产物写入。
- `Create` 在提交前目标仍须不存在；`Update` 在提交前当前摘要仍须等于计划的 `ExistingSha256`；`Unchanged` 在提交前文本仍须按 Ordinal 等于期望内容。
- 上一版清单不存在时，提交前磁盘清单也必须不存在；上一版清单存在时，磁盘清单解析后的规范内容必须仍与计划一致。
- 所有临时文件必须创建在目标文件同一目录，刷新到磁盘后再通过同卷 rename/replace 提交。
- 生成清单必须最后提交；已有清单提交采用无覆盖 claim/校验/无覆盖 rename，任一并发保存都不得被生成器覆盖。无法安全恢复时必须保留 `.recovery` 文件，不得把未确认状态静默声明为新所有权。
- 协作锁和二次校验用于正常编辑器与并行生成进程的冲突保护，不构成抵御同权限恶意本地进程的文件系统 sandbox；调用方必须选择受信工作区。需要抵抗目录在系统调用间被恶意替换时，必须另建平台原生 `openat/no-follow` 适配层。
- 本切片不实现 CLI、删除陈旧生成文件、跨进程强制终止、备份历史或跨文件事务。

---

### Task 1：安全快照与清单并发期望

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWorkspaceConflictException.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWorkspaceSnapshot.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWorkspacePath.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWorkspaceStore.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWritePlan.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWritePlanner.cs`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/GenerationWorkspaceStoreTests.cs`

**Interfaces:**
- Produces: `GenerationWorkspaceSnapshot(IReadOnlyDictionary<string,string> ExistingFiles, GenerationManifest? PreviousManifest)`.
- Produces: `GenerationWorkspaceStore.CaptureAsync(string workspaceRoot, IReadOnlyList<GeneratedArtifact> artifacts, CancellationToken cancellationToken = default)`.
- Extends: `GenerationWritePlan.PreviousManifest`.
- Produces: `GenerationWorkspaceConflictException : IOException`.

- [x] **Step 1：写入快照 RED**

测试必须证明：

1. 捕获只读取期望产物，缺失产物不进入字典，并能解析固定位置的上一版清单。
2. 捕获拒绝不存在的根目录、严格 UTF-8 非法字节、路径大小写别名和 reparse point。
3. 计划保留上一版清单，供应用阶段执行清单 compare-and-swap 校验。

- [x] **Step 2：运行 RED**

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore
```

Expected: 因 `GenerationWorkspaceStore`、快照与冲突类型尚不存在而编译失败。

- [x] **Step 3：实现最小快照读取**

实现固定内部路径、根目录规范化、逐段实际路径检查、严格 UTF-8 读取和清单解析。路径解析必须返回根目录内的绝对路径，并在发现大小写别名或 reparse point 时抛出 `GenerationWorkspaceConflictException`。

- [x] **Step 4：运行定向测试**

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter FullyQualifiedName~GenerationWorkspaceStoreTests
```

Expected: Task 1 新用例全部通过。

---

### Task 2：锁定、二次校验与原子提交

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWorkspaceStore.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/GenerationWorkspaceStoreTests.cs`
- Modify: `eng/testing/test-matrix.json`

**Interfaces:**
- Consumes: `GenerationWritePlan.Actions`、`PreviousManifest`、`NextManifest`.
- Produces: `GenerationWorkspaceStore.ApplyAsync(string workspaceRoot, GenerationWritePlan plan, CancellationToken cancellationToken = default)`.

- [x] **Step 1：写入应用 RED**

测试必须证明：

1. `Create / Update / Unchanged` 正确落地，最终清单与 `NextManifest.ToJson()` 完全一致。
2. `Conflict` 计划在任何工作区写入前失败。
3. 计划创建后新增目标文件，或更新目标内容发生变化时，全量预检失败且其他产物保持原状。
4. 上一版清单被并发修改时拒绝提交。
5. 已取消调用不创建内部目录或产物。
6. 成功和失败路径均不遗留 `.tmp` 文件。

- [x] **Step 2：运行 RED**

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter FullyQualifiedName~GenerationWorkspaceStoreTests
```

Expected: 因 `ApplyAsync` 尚不存在或没有满足写盘不变量而失败。

- [x] **Step 3：实现最小原子写盘**

按以下顺序实现：

1. 校验计划可应用、取消状态和内部路径冲突。
2. 创建 `.fullnet` 后，以 `FileShare.None` 和 `DeleteOnClose` 获取协作锁。
3. 对全部动作和上一版清单执行第一次预检。
4. 为 `Create / Update` 和下一版清单在各自目标目录写入同目录临时文件，并执行 `FlushAsync` 与 `Flush(flushToDisk: true)`。
5. 对全部动作和上一版清单执行第二次预检。
6. 按稳定动作顺序提交产物；已有清单先无覆盖 claim 到 recovery，校验真实被 claim 版本后再无覆盖提交新清单。
7. 在 `finally` 中清理尚未提交的临时文件。

- [x] **Step 4：运行定向测试**

运行 Task 2 Step 2 的命令，预期全部通过。

- [x] **Step 5：更新唯一测试门槛**

依据完整 Unit 的新鲜发现数，只修改 `eng/testing/test-matrix.json` 的 Unit minimum。

---

### Task 3：验证与安全审查

**Files:**
- Modify: `docs/superpowers/plans/2026-07-30-codegeneration-atomic-workspace-writer.md`

- [x] **Step 1：执行开发内循环影响集计划**

```powershell
pnpm test:integration:affected:plan -- --snapshot codegeneration-atomic-write-20260730 --phase inner
```

- [x] **Step 2：执行构建、定向和完整 Unit**

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter FullyQualifiedName~GenerationWorkspaceStoreTests
pnpm test:dotnet:unit -- --no-build
pnpm test:integration:tooling
```

- [x] **Step 3：执行纵向切片影响集**

```powershell
pnpm test:integration:affected -- --snapshot codegeneration-atomic-write-20260730 --phase slice
```

- [x] **Step 4：执行静态检查**

```powershell
git diff --check
git status --short
git branch --show-current
```

- [x] **Step 5：独立审查**

审查根目录逃逸、大小写别名、链接/reparse point、清单并发覆盖、临时文件位置、二次校验窗口、取消与失败恢复。修复本切片引入的全部 Critical/Important 后记录实际验证结果。

## 实际验证结果

- Release 构建：0 警告，0 错误。
- `GenerationWorkspaceStoreTests`：8/8 通过；全部 CodeGeneration：55/55 通过。
- 完整 Unit：555/555 通过；Integration 工具链：30/30 通过。
- 影响集计划：`integration-matrix, smoke`。纵向切片的工具/治理与 Integration 构建通过；Smoke 6/8，两个迁移断言因共享工作区已有未跟踪迁移 033–036 使 `ExecutedScriptCount` 从 1 变为 5 而失败，与本切片无关。
- `git diff --check`：通过；分支 `codex/performance-hardening`，任务基线 `975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`。
- 独立安全复审：Critical 0，Important 0。剩余 Minor 为 Windows 8.3 短名识别、ACL/只读异常归一化，以及进程强制终止后的 orphan recovery 自动提示。
