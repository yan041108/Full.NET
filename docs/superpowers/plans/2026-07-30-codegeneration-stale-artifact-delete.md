# CodeGeneration Stale Artifact Delete Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 生成目标收缩时，仅删除仍与上一版清单哈希一致的陈旧产物，并对人工修改、路径别名和并发变化失败关闭。

**Architecture:** `GenerationWorkspaceStore.CaptureAsync` 先读取上一版清单，再捕获“期望路径＋旧清单路径”的磁盘文本。`GenerationWritePlanner` 为仍存在且哈希匹配的陈旧产物生成 `Delete`，为已修改的陈旧产物生成 `Conflict`，缺失文件则只从下一版清单撤销所有权。`ApplyAsync` 在工作区锁内二次校验所有动作，进入不可取消提交阶段后执行创建、更新和删除，最后线性提交清单。

**Tech Stack:** .NET 10、C#、MSTest、Microsoft Testing Platform。

## Global Constraints

- 不覆盖或删除未被上一版清单按相同大小写路径拥有的文件。
- 删除前当前 SHA-256 必须与上一版清单 SHA-256 完全一致。
- 任一冲突令 `NextManifest` 为空并阻止整批应用。
- `.fullnet/` 内部状态路径不得成为生成产物或删除目标。
- 首个产物提交之后不再响应调用方取消，必须继续完成清单提交。
- 只运行 CodeGeneration 聚焦单元测试和任务快照命中的本地影响集；完整集成测试保留给 `main` CI。

---

### Task 1: Plan stale artifact ownership transitions

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWriteAction.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWritePlanner.cs`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/GenerationWritePlannerTests.cs`

**Interfaces:**
- Consumes: `GenerationManifest.Artifacts`、`GenerationManifest.TryGetSha256`、当前文件快照。
- Produces: `GenerationWriteActionKind.Delete`；删除动作的 `Content` 与 `DesiredSha256` 为 `null`，`ExistingSha256` 为计划时磁盘摘要。

- [ ] **Step 1: Write failing planner tests**

```csharp
[TestMethod]
public void Plan_unmodified_stale_manifest_entry_creates_delete_action()
{
    var manifest = GenerationManifest.Create(
        [new("backend/stale.g.cs", Hash("stale"))]);
    var plan = GenerationWritePlanner.Plan(
        [],
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["backend/stale.g.cs"] = "stale",
        },
        manifest);

    var action = plan.Actions.Single();
    Assert.AreEqual(GenerationWriteActionKind.Delete, action.Kind);
    Assert.IsNull(action.Content);
    Assert.AreEqual(Hash("stale"), action.ExistingSha256);
    Assert.IsNull(action.DesiredSha256);
}
```

同时覆盖：陈旧文件被人工修改时为 `Conflict`；陈旧文件已缺失时无动作；旧清单与期望路径仅大小写不同时为 `Conflict`；所有动作按 ordinal 路径稳定排序。

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~GenerationWritePlannerTests"
```

Expected: FAIL because `GenerationWriteActionKind.Delete` and stale-entry planning do not exist.

- [ ] **Step 3: Implement minimal planner behavior**

```csharp
public enum GenerationWriteActionKind
{
    Create = 1,
    Update = 2,
    Unchanged = 3,
    Conflict = 4,
    Delete = 5,
}

public sealed record GenerationWriteAction(
    string RelativePath,
    GenerationWriteActionKind Kind,
    string? Content,
    string? ExistingSha256,
    string? DesiredSha256);
```

期望产物规划完成后遍历旧清单中不再期望的条目：路径别名或哈希变化生成 `Conflict`，哈希一致生成 `Delete`，磁盘缺失不生成动作。组合动作按 `RelativePath` ordinal 排序。

- [ ] **Step 4: Run planner tests and verify GREEN**

Run the command from Step 2.

Expected: all `GenerationWritePlannerTests` pass.

### Task 2: Capture and apply stale deletions safely

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWorkspaceStore.cs`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/GenerationWorkspaceStoreTests.cs`

**Interfaces:**
- Consumes: Task 1 `Delete` action shape and previous manifest entries.
- Produces: capture includes existing stale owned files; apply revalidates their hashes, deletes them, validates absence, then commits the next manifest.

- [ ] **Step 1: Write failing workspace tests**

```csharp
[TestMethod]
public async Task Capture_and_apply_deletes_only_unmodified_stale_owned_file()
{
    using var workspace = TemporaryWorkspace.Create();
    workspace.Write("backend/stale.g.cs", "stale");
    workspace.Write(
        GenerationWorkspaceStore.ManifestRelativePath,
        GenerationManifest.Create(
            [new("backend/stale.g.cs", Hash("stale"))]).ToJson());

    var snapshot = await GenerationWorkspaceStore.CaptureAsync(
        workspace.RootPath,
        []);
    var plan = GenerationWritePlanner.Plan(
        [],
        snapshot.ExistingFiles,
        snapshot.PreviousManifest);
    await GenerationWorkspaceStore.ApplyAsync(workspace.RootPath, plan);

    Assert.IsFalse(File.Exists(workspace.PathOf("backend/stale.g.cs")));
    Assert.AreEqual(
        GenerationManifest.Create([]).ToJson(),
        workspace.Read(GenerationWorkspaceStore.ManifestRelativePath));
}
```

同时覆盖：捕获后并发修改删除目标时，应用在任何写入前冲突；旧清单声明 `.fullnet/` 路径时捕获失败。

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~GenerationWorkspaceStoreTests"
```

Expected: FAIL because capture ignores stale paths and apply does not support delete.

- [ ] **Step 3: Implement capture and apply behavior**

读取清单后构造 ordinal 去重的期望路径与旧清单路径并捕获现有文本；对旧清单路径再次执行内部路径拒绝。提交循环按计划动作执行，`Create/Update` 使用已暂存文本，`Delete` 先按 `ExistingSha256` 复验后调用 `File.Delete`。`ValidateDesiredStateAsync` 要求删除目标不存在，其他动作内容等于计划内容。

- [ ] **Step 4: Run workspace and complete CodeGeneration tests**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CodeGeneration"
```

Expected: all CodeGeneration tests pass.

### Task 3: Update the single test-count authority and verify the slice

**Files:**
- Modify: `eng/testing/test-matrix.json`

**Interfaces:**
- Consumes: final discovered Unit test count.
- Produces: the sole minimum-test threshold remains synchronized.

- [ ] **Step 1: Update the Unit minimum**

Increase `suites.unit.minimum` by the number of newly added test methods. Do not copy the number to README, CI, rules, or Skills.

- [ ] **Step 2: Run focused and affected verification**

```powershell
pnpm test:dotnet:unit -- --no-build
pnpm test:integration:affected:plan -- --snapshot codegeneration-stale-delete-20260730 --phase inner
pnpm test:integration:affected -- --snapshot codegeneration-stale-delete-20260730 --phase slice
git diff --check
git status --short
```

Expected: Unit threshold passes; affected selector reports only the snapshot impact set; any unrelated pre-existing integration failure is reported verbatim and not attributed to this slice.

### Task 4: Close destructive-write review findings

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWorkspaceStore.cs`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/GenerationWorkspaceStoreTests.cs`
- Modify: `eng/testing/test-matrix.json`

**Interfaces:**
- Consumes: `Delete` actions and manifest-last commit protocol from Tasks 1–2.
- Produces: atomic delete claims with paired recovery metadata, cancellation through the first commit boundary, and retained committed tombstones.

- [x] **Step 1: Reproduce the delete TOCTOU**

Use a deterministic hook immediately before the delete claim to replace the target. The original implementation must fail because it deletes the replacement.

- [x] **Step 2: Replace check-then-delete with claim-then-check**

Move the target without overwrite into `.fullnet/codegeneration-delete-recovery`, persist a paired `.path` metadata file, hash the claimed file, and restore without overwrite on mismatch. Pending recovery blocks later capture/apply for explicit audit.

- [x] **Step 3: Reproduce and fix cancellation before the first commit**

Cancel from a hook after first-action validation and before commit. Continue using the caller token until the first artifact commit; use `CancellationToken.None` only after that boundary.

- [x] **Step 4: Reproduce and fix manifest cleanup ambiguity**

Inject both `IOException` and `UnauthorizedAccessException` after the new manifest commit but before old-manifest recovery cleanup. Report an explicit conflict stating that the new manifest is already committed and retain recovery evidence.

- [x] **Step 5: Harden recovery and internal path edges**

Reject exact `.fullnet`, restore every claimed deletion even when one restoration fails, restore invalid UTF-8 replacements, and test interrupted delete-recovery detection.

- [x] **Step 6: Close retained-handle writes**

Revalidate every claimed recovery before manifest commit. The retained-handle experiment proved that pathname deletion cannot provide the same portable identity guarantee on Windows and Unix, so the default apply path must not physically unlink a committed recovery.

- [x] **Step 7: Commit retained tombstones instead of unlinking recovery**

Treat `.path` as an uncommitted recovery phase that blocks capture/apply. After the manifest commits, atomically rename `.path` to `.committed` and retain the paired `.recovery`; a valid pair does not block later generation, while pending, malformed or orphaned phase files fail closed. Physical cleanup is outside this slice and requires a separate explicit audited operation.

## Self-Review

- Spec coverage: safe deletion, missing-file ownership removal, modified-file conflict, path alias conflict, capture completeness, concurrent revalidation, cancellation boundary, manifest-last ordering and test-count authority are all assigned.
- Placeholder scan: no deferred implementation or unspecified test step remains.
- Type consistency: `Delete` uses nullable desired fields in planner, store validation and tests consistently.
