# CodeGeneration 安全写盘计划器实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 为 CRUD 生成产物建立纯内存、可重复验证的安全写盘计划层，只允许创建新文件、保持相同文件，或更新由上一版清单证明未被用户修改的文件；其余情况必须报告冲突。

**Architecture:** 本切片只在 `Full.NET.Data.CodeGeneration` 内新增路径校验、SHA-256 内容摘要、版本化生成清单和写盘动作规划，不读取或写入真实文件系统。计划器接收“期望生成产物、当前工作区文本快照、上一版生成清单”，输出按路径稳定排序的 `Create / Update / Unchanged / Conflict` 动作；只要存在冲突，就不提供可提交的新清单。删除陈旧文件、磁盘原子替换和 CLI 集成留给后续切片。

**Tech Stack:** .NET 10、C#、System.Security.Cryptography、System.Text.Json、MSTest。

## 全局约束

- 所有相对路径必须使用 `/`，不得为空、绝对化、包含盘符、反斜杠、空段、`.` 或 `..` 段。
- 路径必须使用 Unicode NFC 规范形式，并拒绝 Windows 保留名、尾随点/空格和不可移植字符；生成产物、当前工作区快照和清单中不得出现忽略大小写后冲突的路径别名。
- 内容摘要固定为严格 UTF-8 字节的 SHA-256 小写十六进制；非法 UTF-16 必须失败，不得使用替换字符产生有损摘要。
- 当前文件不存在时为 `Create`；当前路径拼写和文本内容均按 Ordinal 精确等于期望值时为 `Unchanged`，仅大小写不同的文件系统别名必须冲突。
- 当前内容与期望内容不同时，仅当上一版清单记录的摘要等于当前内容摘要时为 `Update`，否则为 `Conflict`。
- 任一冲突必须令 `CanApply == false` 且 `NextManifest == null`，避免调用方误提交不完整所有权状态。
- 当前切片不删除清单中的陈旧条目，也不删除磁盘文件；下一版清单仅包含本次期望产物。
- 当前切片不访问真实文件系统，不实现 CLI、覆盖确认、备份或原子提交。

---

### Task 1：清单、路径与内容摘要原语

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationArtifactPath.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationContentHash.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationManifest.cs`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/GenerationWritePlannerTests.cs`

- [x] **Step 1：写入 RED**

新增测试覆盖：

1. 清单无论输入顺序和当前文化如何都输出相同 JSON。
2. 清单 JSON 能往返并保持稳定排序。
3. 非法路径、重复路径、未知清单版本和非法摘要必须失败。

- [x] **Step 2：运行定向测试并确认失败**

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter FullyQualifiedName~GenerationWritePlannerTests
```

Expected: 因新增类型尚不存在而编译失败。

- [x] **Step 3：实现最小原语**

实现统一路径校验、UTF-8 SHA-256 摘要和 `schemaVersion: 1` 的确定性 JSON 清单。清单条目必须排序、只读且可按路径查询。

- [x] **Step 4：运行定向测试并确认通过**

运行 Task 1 Step 2 的命令，预期所有新用例通过。

---

### Task 2：安全写盘动作计划器

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWriteAction.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWritePlan.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWritePlanner.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/GenerationWritePlannerTests.cs`
- Modify: `eng/testing/test-matrix.json`

- [x] **Step 1：写入动作分类 RED**

新增测试覆盖：

1. 缺失文件生成按路径排序的 `Create` 动作和下一版清单。
2. 内容相同的无主文件标记为 `Unchanged` 并被安全纳入下一版清单。
3. 上一版清单证明未修改的文件允许 `Update`。
4. 用户修改过的已生成文件和无清单所有权的已有文件均为 `Conflict`。
5. 任一冲突阻止整个计划应用且不产生下一版清单。
6. 陈旧清单条目不会触发删除，也不会进入下一版清单。

- [x] **Step 2：运行定向测试并确认失败**

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter FullyQualifiedName~GenerationWritePlannerTests
```

Expected: 因动作与计划器类型尚不存在而编译失败。

- [x] **Step 3：实现最小计划器**

计划器只消费内存输入，按 `StringComparer.Ordinal` 稳定排序动作，并在动作中保留期望内容、当前摘要（若存在）和期望摘要，供后续真实写盘适配层再次校验。

- [x] **Step 4：运行定向测试并确认通过**

运行 Task 2 Step 2 的命令，预期所有新用例通过。

- [x] **Step 5：更新唯一测试矩阵**

根据完整单元测试的新鲜输出更新 `eng/testing/test-matrix.json` 的 Unit 最低数量，不在其他文档复制测试门槛。

---

### Task 3：验证与审查

**Files:**
- Modify: `docs/superpowers/plans/2026-07-29-codegeneration-safe-write-plan.md`

- [x] **Step 1：执行开发内循环影响集**

```powershell
pnpm test:integration:affected:plan -- --snapshot codegeneration-safe-write-plan-20260729 --phase inner
```

- [x] **Step 2：执行构建、定向测试和完整单元测试**

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter FullyQualifiedName~GenerationWritePlannerTests
pnpm test:dotnet:unit -- --no-build
pnpm test:integration:tooling
```

- [x] **Step 3：执行纵向切片影响集**

```powershell
pnpm test:integration:affected -- --snapshot codegeneration-safe-write-plan-20260729 --phase slice
```

- [x] **Step 4：执行静态工作区检查**

```powershell
git diff --check
git status --short
git branch --show-current
```

- [x] **Step 5：独立审查并记录结论**

审查重点为路径穿越、所有权误判、冲突时清单泄漏、摘要与序列化确定性。只修复本切片引入的问题，并把实际验证结果更新到本计划。

## 实际验证结果

- Release 构建：通过，0 warning、0 error。
- `GenerationWritePlannerTests`：10/10 通过。
- 完整 Unit：547/547 通过。
- Integration 工具链：30/30 通过；切片命令内的治理与选择器用例另有 37/37 通过。
- `git diff --check`：通过；仅输出共享工作区既有的 LF/CRLF 提示。
- 纵向切片 Smoke：6/8，通过用例之外的两个失败均为迁移夹具旧断言期望首次执行 1 个脚本，而共享工作区当前已有 5 个待执行脚本；失败位于 SQL Server/MySQL migration idempotency 测试，与本切片无文件交集。
- 独立审查发现并修复路径别名、Windows 设备名、非法 UTF-16 有损摘要三个安全边界；最终复审为 Critical=0、Important=0、无新增 Minor。
