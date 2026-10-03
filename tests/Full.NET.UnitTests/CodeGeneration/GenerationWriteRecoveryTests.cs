using Full.NET.Data.CodeGeneration.Generation;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class GenerationWriteRecoveryTests
{
    private string _root = null!;
    private const string Recovery = ".fullnet/codegeneration-write-recovery";
    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "fullnet-write-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }
    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Failure_restores_created_or_updated_files_and_previous_manifest(bool update, bool atManifest)
    {
        var plan = await PrepareAsync(update);
        var manifest = File.Exists(PathOf(GenerationWorkspaceStore.ManifestRelativePath))
            ? File.ReadAllBytes(PathOf(GenerationWorkspaceStore.ManifestRelativePath)) : null;
        await Assert.ThrowsExactlyAsync<IOException>(() => GenerationWorkspaceStore.ApplyForTestingAsync(
            _root, plan, () => atManifest ? Task.FromException(new IOException("清单提交故障")) : Task.CompletedTask,
            afterArtifactCommit: count => !atManifest && count == 1
                ? Task.FromException(new IOException("首个产物提交故障")) : Task.CompletedTask));
        foreach (var name in new[] { "backend/a.txt", "backend/b.txt" })
        {
            if (update) Assert.AreEqual("old-" + name + "\n", await File.ReadAllTextAsync(PathOf(name)));
            else Assert.IsFalse(File.Exists(PathOf(name)), name);
        }
        if (manifest is null) Assert.IsFalse(File.Exists(PathOf(GenerationWorkspaceStore.ManifestRelativePath)));
        else CollectionAssert.AreEqual(manifest, File.ReadAllBytes(PathOf(GenerationWorkspaceStore.ManifestRelativePath)));
        await GenerationWorkspaceStore.ApplyAsync(_root, plan);
        Assert.AreEqual("new-backend/a.txt\n", await File.ReadAllTextAsync(PathOf("backend/a.txt")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Concurrent_later_target_is_preserved_and_first_commit_is_recovered(bool update)
    {
        var plan = await PrepareAsync(update);
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => GenerationWorkspaceStore.ApplyForTestingAsync(
            _root, plan, () => Task.CompletedTask, afterArtifactCommit: count =>
            {
                if (count == 1) File.WriteAllText(PathOf("backend/b.txt"), "人工内容\n");
                return Task.CompletedTask;
            }));
        Assert.AreEqual("人工内容\n", await File.ReadAllTextAsync(PathOf("backend/b.txt")));
        if (update) Assert.AreEqual("old-backend/a.txt\n", await File.ReadAllTextAsync(PathOf("backend/a.txt")));
        else Assert.IsFalse(File.Exists(PathOf("backend/a.txt")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Concurrent_edit_of_committed_file_is_preserved_with_pending_evidence(bool update)
    {
        var plan = await PrepareAsync(update);
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => GenerationWorkspaceStore.ApplyForTestingAsync(
            _root, plan, () => Task.CompletedTask, afterArtifactCommit: count =>
            {
                if (count == 1)
                {
                    File.WriteAllText(PathOf("backend/a.txt"), "人工内容\n");
                    throw new IOException("并发修改后故障");
                }
                return Task.CompletedTask;
            }));
        Assert.AreEqual("人工内容\n", await File.ReadAllTextAsync(PathOf("backend/a.txt")));
        Assert.IsTrue(Directory.GetFiles(PathOf(Recovery)).Length > 0);
        if (update) Assert.IsTrue(Directory.GetFiles(PathOf(Recovery), "*.old")
            .Any(path => File.ReadAllText(path) == "old-backend/a.txt\n"));
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() =>
            GenerationWorkspaceStore.CaptureAsync(_root, []));
    }

    [TestMethod]
    public async Task Changed_old_backup_before_manifest_is_restored_instead_of_discarded()
    {
        var plan = await PrepareAsync(update: true);
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => GenerationWorkspaceStore.ApplyForTestingAsync(
            _root, plan, () => Task.CompletedTask, afterArtifactCommit: count =>
            {
                if (count == 1) File.WriteAllText(Directory.GetFiles(PathOf(Recovery), "*.old").Single(), "旧文件句柄人工修改\n");
                return Task.CompletedTask;
            }));
        Assert.AreEqual("旧文件句柄人工修改\n", await File.ReadAllTextAsync(PathOf("backend/a.txt")));
        Assert.AreEqual("old-backend/b.txt\n", await File.ReadAllTextAsync(PathOf("backend/b.txt")));
    }

    [TestMethod]
    public async Task Restore_conflict_does_not_skip_other_committed_updates()
    {
        var plan = await PrepareAsync(update: true);
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => GenerationWorkspaceStore.ApplyForTestingAsync(
            _root, plan, () => Task.CompletedTask, afterArtifactCommit: count =>
            {
                if (count == 2)
                {
                    File.WriteAllText(PathOf("backend/a.txt"), "人工内容\n");
                    throw new IOException("两个提交后故障");
                }
                return Task.CompletedTask;
            }));
        Assert.AreEqual("人工内容\n", await File.ReadAllTextAsync(PathOf("backend/a.txt")));
        Assert.AreEqual("old-backend/b.txt\n", await File.ReadAllTextAsync(PathOf("backend/b.txt")));
    }

    [TestMethod]
    public async Task Backup_edit_after_manifest_commit_is_retained_without_rolling_back_committed_state()
    {
        var plan = await PrepareAsync(update: true);
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => GenerationWorkspaceStore.ApplyForTestingAsync(
            _root, plan, () => Task.CompletedTask, beforeManifestRecoveryCleanup: () =>
            {
                File.WriteAllText(Directory.GetFiles(PathOf(Recovery), "*.old").First(), "旧文件清理前人工修改\n");
                return Task.CompletedTask;
            }));
        Assert.AreEqual(plan.NextManifest!.ToJson(), await File.ReadAllTextAsync(PathOf(GenerationWorkspaceStore.ManifestRelativePath)));
        Assert.AreEqual("new-backend/a.txt\n", await File.ReadAllTextAsync(PathOf("backend/a.txt")));
        Assert.IsTrue(Directory.GetFiles(PathOf(Recovery), "*.old")
            .Any(path => File.ReadAllText(path) == "旧文件清理前人工修改\n"));
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => GenerationWorkspaceStore.CaptureAsync(_root, []));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task Unreadable_committed_human_file_is_returned_to_original_path(bool update, bool invalidUtf8)
    {
        var plan = await PrepareAsync(update);
        byte[] expected = invalidUtf8 ? [0xff] : System.Text.Encoding.UTF8.GetBytes("保留人工写句柄\n");
        FileStream? handle = null;
        try
        {
            await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => GenerationWorkspaceStore.ApplyForTestingAsync(
                _root, plan, () => Task.CompletedTask, afterArtifactCommit: count =>
                {
                    if (count == 1)
                    {
                        if (invalidUtf8) File.WriteAllBytes(PathOf("backend/a.txt"), expected);
                        else
                        {
                            handle = new FileStream(PathOf("backend/a.txt"), FileMode.Open, FileAccess.ReadWrite,
                                FileShare.ReadWrite | FileShare.Delete);
                            handle.SetLength(0);
                            handle.Write(expected);
                            handle.Flush(flushToDisk: true);
                        }
                        throw new IOException("人工写入后故障");
                    }
                    return Task.CompletedTask;
                }));
        }
        finally
        {
            if (handle is not null) await handle.DisposeAsync();
        }
        CollectionAssert.AreEqual(expected, await File.ReadAllBytesAsync(PathOf("backend/a.txt")));
        Assert.IsTrue(Directory.GetFiles(PathOf(Recovery), "*.pending").Length > 0);
        if (update) Assert.IsTrue(Directory.GetFiles(PathOf(Recovery), "*.old").Length > 0);
    }

    [TestMethod]
    public async Task Backup_change_in_last_manifest_callback_restores_files_before_commit()
    {
        var plan = await PrepareAsync(update: true);
        var previous = await File.ReadAllTextAsync(PathOf(GenerationWorkspaceStore.ManifestRelativePath));
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => GenerationWorkspaceStore.ApplyForTestingAsync(
            _root, plan, () =>
            {
                File.WriteAllText(Directory.GetFiles(PathOf(Recovery), "*.old").First(), "最后检查前人工修改\n");
                return Task.CompletedTask;
            }));
        Assert.AreEqual(previous, await File.ReadAllTextAsync(PathOf(GenerationWorkspaceStore.ManifestRelativePath)));
        Assert.IsTrue(new[] { "backend/a.txt", "backend/b.txt" }
            .Any(path => File.ReadAllText(PathOf(path)) == "最后检查前人工修改\n"));
    }

    [TestMethod]
    public async Task Retained_old_write_handle_is_not_silently_deleted_after_manifest_commit()
    {
        var plan = await PrepareAsync(update: true);
        FileStream? handle = null;
        try
        {
            await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => GenerationWorkspaceStore.ApplyForTestingAsync(
                _root, plan, () => Task.CompletedTask, beforeManifestRecoveryCleanup: () =>
                {
                    handle = new FileStream(Directory.GetFiles(PathOf(Recovery), "*.old").First(),
                        FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                    handle.SetLength(0);
                    handle.Write(System.Text.Encoding.UTF8.GetBytes("保留写句柄人工内容\n"));
                    handle.Flush(flushToDisk: true);
                    return Task.CompletedTask;
                }));
        }
        finally
        {
            if (handle is not null) await handle.DisposeAsync();
        }
        Assert.IsTrue(Directory.GetFiles(PathOf(Recovery), "*.old")
            .Any(path => File.ReadAllText(path) == "保留写句柄人工内容\n"));
        Assert.AreEqual(plan.NextManifest!.ToJson(), await File.ReadAllTextAsync(PathOf(GenerationWorkspaceStore.ManifestRelativePath)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Change_at_first_commit_boundary_is_not_overwritten(bool update)
    {
        var plan = await PrepareAsync(update);
        await Assert.ThrowsAsync<IOException>(() => GenerationWorkspaceStore.ApplyForTestingAsync(
            _root, plan, () => Task.CompletedTask, beforeFirstArtifactCommit: () =>
            {
                File.WriteAllText(PathOf("backend/a.txt"), "声明边界人工内容\n");
                return Task.CompletedTask;
            }));
        Assert.AreEqual("声明边界人工内容\n", await File.ReadAllTextAsync(PathOf("backend/a.txt")));
        if (update) Assert.AreEqual("old-backend/b.txt\n", await File.ReadAllTextAsync(PathOf("backend/b.txt")));
        else Assert.IsFalse(File.Exists(PathOf("backend/b.txt")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Pending_evidence_blocks_manifest_reads(bool hasManifest)
    {
        if (hasManifest) await GenerationWorkspaceStore.ApplyAsync(_root, await PrepareAsync(update: false));
        Directory.CreateDirectory(PathOf(Recovery));
        await File.WriteAllTextAsync(PathOf(Recovery + "/orphan.pending"), "中断证据");
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() =>
            GenerationWorkspaceStore.ReadManifestOrEmptyAsync(_root));
    }

    [TestMethod]
    public async Task Orphan_write_recovery_blocks_capture_even_without_manifest()
    {
        Directory.CreateDirectory(PathOf(Recovery));
        await File.WriteAllTextAsync(PathOf(Recovery + "/orphan.pending"), "中断证据");
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() =>
            GenerationWorkspaceStore.CaptureAsync(_root, []));
    }

    [TestMethod]
    public async Task Successful_update_removes_write_recovery_and_allows_next_capture()
    {
        await GenerationWorkspaceStore.ApplyAsync(_root, await PrepareAsync(update: true));
        Assert.IsFalse(Directory.Exists(PathOf(Recovery)) && Directory.GetFiles(PathOf(Recovery)).Length > 0);
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(_root, Desired("new"));
        Assert.AreEqual("new-backend/a.txt\n", snapshot.ExistingFiles["backend/a.txt"]);
    }

    private async Task<GenerationWritePlan> PrepareAsync(bool update)
    {
        if (update)
        {
            var before = await GenerationWorkspaceStore.CaptureAsync(_root, Desired("old"));
            await GenerationWorkspaceStore.ApplyAsync(_root, GenerationWritePlanner.Plan(Desired("old"), before.ExistingFiles));
        }
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(_root, Desired("new"));
        return GenerationWritePlanner.Plan(Desired("new"), snapshot.ExistingFiles, snapshot.PreviousManifest);
    }
    private static GeneratedArtifact[] Desired(string prefix) => [
        new("backend/a.txt", GeneratedArtifactKind.Report, prefix + "-backend/a.txt\n"),
        new("backend/b.txt", GeneratedArtifactKind.Report, prefix + "-backend/b.txt\n")];
    private string PathOf(string relative) => Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
}
