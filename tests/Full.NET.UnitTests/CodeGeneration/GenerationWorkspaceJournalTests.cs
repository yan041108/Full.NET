using Full.NET.Data.CodeGeneration.Generation;
using System.Diagnostics;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>整操作日志覆盖中断、人工漂移与跨实体全局顺序，不能仅以清单相同推断提交完成。</summary>
[TestClass]
public sealed class GenerationWorkspaceJournalTests
{
    [TestMethod]
    public async Task Unchanged_apply_completes_without_a_staging_directory_and_can_be_rolled_back()
    {
        var root = Directory.CreateTempSubdirectory("fullnet-codegen-noop-").FullName;
        try
        {
            await using var lease = await Acquire(root);
            var first = Guid.CreateVersion7();
            await GenerationWorkspaceJournal.PrepareAsync(lease, first, first, "apply", "cli", null, await Plan(root, ("a.txt", "same")));
            await GenerationWorkspaceJournal.CommitAsync(lease);
            await GenerationWorkspaceJournal.CompleteAsync(lease);
            var id = Guid.CreateVersion7();
            var plan = await Plan(root, ("a.txt", "same"));
            await GenerationRollbackCheckpointStore.CreateAsync(root, id, plan);
            await GenerationWorkspaceJournal.PrepareAsync(lease, id, id, "apply", "cli", null, plan);
            await GenerationWorkspaceJournal.CommitAsync(lease);
            await GenerationWorkspaceJournal.CompleteAsync(lease);
            Assert.IsNull(await GenerationWorkspaceJournal.ReadPendingAsync(lease));
            Assert.AreEqual("completed", (await GenerationWorkspaceJournal.ReadOperationAsync(lease, id))!.Phase);
            var rollback = await GenerationRollbackWorkspace.PlanAsync(root, await GenerationRollbackCheckpointStore.ReadAsync(root, id));
            Assert.IsTrue(rollback.CanApply);
            Assert.AreEqual("same", await File.ReadAllTextAsync(Path.Combine(root, "a.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task Missing_active_pointer_does_not_allow_a_new_plan_to_overtake_an_unfinished_receipt()
    {
        var root = Directory.CreateTempSubdirectory("fullnet-codegen-orphan-order-").FullName;
        try
        {
            await using var lease = await Acquire(root);
            var id = Guid.CreateVersion7();
            await GenerationWorkspaceJournal.PrepareAsync(lease, id, id, "apply", "cli", null, await Plan(root, ("a.txt", "a")));
            GenerationWorkspaceMetadata.Delete(root, ".fullnet/codegeneration-active-operation.json");
            Assert.AreEqual(id, (await GenerationWorkspaceJournal.ReadPendingAsync(lease))!.Id);
            var next = Guid.CreateVersion7();
            await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(async () =>
                await GenerationWorkspaceJournal.PrepareAsync(lease, next, next, "apply", "cli", null, await Plan(root, ("b.txt", "b"))));
            Assert.IsFalse(File.Exists(Path.Combine(root, "b.txt")));
            await GenerationWorkspaceJournal.CommitAsync(lease);
            await GenerationWorkspaceJournal.CompleteAsync(lease);
            Assert.AreEqual("a", await File.ReadAllTextAsync(Path.Combine(root, "a.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task Reviewed_plan_is_bound_to_revision_and_durable_key_replays_only_the_same_plan()
    {
        var root = Directory.CreateTempSubdirectory("fullnet-codegen-reviewed-plan-").FullName;
        try
        {
            await using var lease = await Acquire(root);
            var id = Guid.CreateVersion7();
            var key = Guid.CreateVersion7();
            var first = await GenerationWorkspaceJournal.FreezeAsync(lease, id, id, "apply", "cli", null, await Plan(root, ("a.txt", "a")));
            var secondId = Guid.CreateVersion7();
            var stale = await GenerationWorkspaceJournal.FreezeAsync(lease, secondId, secondId, "apply", "cli", null, await Plan(root, ("b.txt", "b")));
            Assert.IsNull(await GenerationWorkspaceJournal.ReadPendingAsync(lease));
            Assert.AreEqual(0L, (await GenerationWorkspaceJournal.ReadHeadAsync(lease)).Revision);
            Assert.IsFalse(File.Exists(Path.Combine(root, "a.txt")));
            first = GenerationWorkspaceJournal.ParsePlan(lease, GenerationWorkspaceJournal.SerializePlan(first)) with { IdempotencyKey = key };
            await GenerationWorkspaceJournal.ActivateAsync(lease, first);
            Assert.AreEqual(first.PlanSha256, (await GenerationWorkspaceJournal.FindByIdempotencyKeyAsync(lease, key))!.PlanSha256);
            await GenerationWorkspaceJournal.CommitAsync(lease);
            Assert.AreEqual("locally_applied", (await GenerationWorkspaceJournal.ReadPendingAsync(lease))!.Phase);
            await GenerationWorkspaceJournal.CompleteAsync(lease);
            var replay = await GenerationWorkspaceJournal.ActivateAsync(lease, first);
            Assert.AreEqual("completed", replay.Phase);
            Assert.AreEqual(id, replay.Id);
            Assert.IsNull(await GenerationWorkspaceJournal.ReadPendingAsync(lease));
            await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => GenerationWorkspaceJournal.ActivateAsync(lease, stale));
            await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => GenerationWorkspaceJournal.ActivateAsync(lease, stale with { IdempotencyKey = key }));
            await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => GenerationWorkspaceJournal.ActivateAsync(lease, first with { IdempotencyKey = Guid.CreateVersion7() }));
            Assert.IsFalse(File.Exists(Path.Combine(root, "b.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task Recovery_budget_refuses_before_product_files_or_active_operation_are_written()
    {
        var root = Directory.CreateTempSubdirectory("fullnet-codegen-journal-budget-").FullName;
        try
        {
            await using var lease = await Acquire(root);
            using (var evidence = new FileStream(Path.Combine(root, ".fullnet", "retained-evidence.bin"), FileMode.CreateNew))
                evidence.SetLength(GenerationWorkspaceRecoveryBudget.MaximumBytes - 512 * 1024);
            var plan = await Plan(root, ("a.txt", "new"));
            var id = Guid.CreateVersion7();
            await Assert.ThrowsExactlyAsync<IOException>(() => GenerationWorkspaceJournal.PrepareAsync(lease, id, id, "apply", "cli", null, plan));
            Assert.IsFalse(File.Exists(Path.Combine(root, "a.txt")));
            Assert.IsNull(await GenerationWorkspaceJournal.ReadPendingAsync(lease));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task Killed_writer_releases_file_authority_and_another_process_recovers_the_partial_plan()
    {
        const string workerVariable = "FULLNET_CODEGEN_JOURNAL_CRASH_ROOT";
        var workerRoot = Environment.GetEnvironmentVariable(workerVariable);
        if (workerRoot is not null)
        {
            await using var workerLease = await Acquire(workerRoot);
            var workerId = Guid.CreateVersion7();
            await GenerationWorkspaceJournal.PrepareAsync(workerLease, workerId, workerId, "apply", "cli", null,
                await Plan(workerRoot, ("a.txt", "first-new"), ("b.txt", "second-new")));
            await GenerationWorkspaceJournal.CommitCoreAsync(workerLease, CancellationToken.None, async index =>
            {
                if (index != 0) return;
                await File.WriteAllTextAsync(Path.Combine(workerRoot, "worker.ready"), "ready");
                await Task.Delay(Timeout.InfiniteTimeSpan);
            });
            throw new InvalidOperationException("测试子进程应在第一个文件提交后被终止。");
        }
        var root = Directory.CreateTempSubdirectory("fullnet-codegen-journal-kill-").FullName;
        using var process = new Process();
        var started = false;
        try
        {
            process.StartInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { typeof(GenerationWorkspaceJournalTests).Assembly.Location, "--filter",
                         "FullyQualifiedName~GenerationWorkspaceJournalTests.Killed_writer_releases_file_authority_and_another_process_recovers_the_partial_plan",
                         "--no-ansi", "--progress", "off" }) process.StartInfo.ArgumentList.Add(argument);
            process.StartInfo.Environment[workerVariable] = root;
            Assert.IsTrue(process.Start());
            started = true;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!File.Exists(Path.Combine(root, "worker.ready")) && !process.HasExited) await Task.Delay(50, timeout.Token);
            if (process.HasExited) Assert.Fail("恢复测试子进程提前退出：" + await stderr + await stdout);
            Assert.IsNull(await GenerationWorkspaceLease.TryAcquireAsync(root));
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr);
            Assert.AreEqual("first-new", await File.ReadAllTextAsync(Path.Combine(root, "a.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(root, "b.txt")));
            await using var recoveryLease = await Acquire(root);
            await GenerationWorkspaceJournal.CommitAsync(recoveryLease);
            await GenerationWorkspaceJournal.CompleteAsync(recoveryLease);
            Assert.AreEqual("second-new", await File.ReadAllTextAsync(Path.Combine(root, "b.txt")));
        }
        finally
        {
            if (started && !process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Prepared_operation_survives_lease_release_and_replays_frozen_files()
    {
        var root = Directory.CreateTempSubdirectory("fullnet-codegen-journal-").FullName;
        try
        {
            var id = Guid.CreateVersion7();
            var lease = await Acquire(root);
            var plan = await Plan(root, ("a.txt", "frozen-a"), ("b.txt", "frozen-b"));
            var operation = await GenerationWorkspaceJournal.PrepareAsync(lease, id, id, "apply", "cli", null, plan);
            Assert.IsFalse(File.Exists(Path.Combine(root, "a.txt")));
            await lease.DisposeAsync();
            await using var resumed = await Acquire(root);
            Assert.AreEqual(operation.PlanSha256, (await GenerationWorkspaceJournal.ReadPendingAsync(resumed))!.PlanSha256);
            await GenerationWorkspaceJournal.CommitAsync(resumed);
            await GenerationWorkspaceJournal.CommitAsync(resumed);
            await GenerationWorkspaceJournal.CompleteAsync(resumed);
            Assert.AreEqual("frozen-a", await File.ReadAllTextAsync(Path.Combine(root, "a.txt")));
            Assert.AreEqual("frozen-b", await File.ReadAllTextAsync(Path.Combine(root, "b.txt")));
            Assert.IsNull(await GenerationWorkspaceJournal.ReadPendingAsync(resumed));
            var head = await GenerationWorkspaceJournal.ReadHeadAsync(resumed);
            Assert.AreEqual(id, head.TipOperationId);
            Assert.AreEqual(1L, head.Revision);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task Partial_new_files_are_recovered_but_manual_drift_blocks_all_remaining_writes()
    {
        var root = Directory.CreateTempSubdirectory("fullnet-codegen-journal-partial-").FullName;
        try
        {
            await using var lease = await Acquire(root);
            var id = Guid.CreateVersion7();
            await GenerationWorkspaceJournal.PrepareAsync(lease, id, id, "apply", "cli", null,
                await Plan(root, ("a.txt", "new-a"), ("b.txt", "new-b")));
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "new-a");
            await File.WriteAllTextAsync(Path.Combine(root, "b.txt"), "human-edit");
            await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => GenerationWorkspaceJournal.CommitAsync(lease));
            Assert.AreEqual("human-edit", await File.ReadAllTextAsync(Path.Combine(root, "b.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(root, GenerationWorkspaceStore.ManifestRelativePath)));
            Assert.IsNotNull(await GenerationWorkspaceJournal.ReadPendingAsync(lease));
            File.Delete(Path.Combine(root, "b.txt"));
            await GenerationWorkspaceJournal.CommitAsync(lease);
            await GenerationWorkspaceJournal.CompleteAsync(lease);
            Assert.AreEqual("new-b", await File.ReadAllTextAsync(Path.Combine(root, "b.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task Claimed_original_can_be_recovered_after_process_stops_between_moves()
    {
        var root = Directory.CreateTempSubdirectory("fullnet-codegen-journal-claim-").FullName;
        try
        {
            await using var lease = await Acquire(root);
            var firstId = Guid.CreateVersion7();
            var first = await Plan(root, ("a.txt", "old"));
            await Apply(lease, firstId, first);
            var id = Guid.CreateVersion7();
            await GenerationWorkspaceJournal.PrepareAsync(lease, id, id, "apply", "cli", null, await Plan(root, ("a.txt", "new")));
            var stage = Path.Combine(root, ".fullnet", "codegeneration-operation-stage", id.ToString("N"));
            Directory.CreateDirectory(stage);
            File.Move(Path.Combine(root, "a.txt"), Path.Combine(stage, "0000.old"));
            await GenerationWorkspaceJournal.CommitAsync(lease);
            await GenerationWorkspaceJournal.CompleteAsync(lease);
            Assert.AreEqual("new", await File.ReadAllTextAsync(Path.Combine(root, "a.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(stage, "0000.old")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task Shared_workspace_rollbacks_are_global_and_revision_prevents_ABA()
    {
        var root = Directory.CreateTempSubdirectory("fullnet-codegen-journal-lifo-").FullName;
        try
        {
            await using var lease = await Acquire(root);
            var a = Guid.CreateVersion7();
            var b = Guid.CreateVersion7();
            var aPlan = await Plan(root, ("module-a.txt", "a"));
            await GenerationRollbackCheckpointStore.CreateAsync(root, a, aPlan);
            await Apply(lease, a, aPlan);
            var beforeB = await GenerationWorkspaceJournal.ReadHeadAsync(lease);
            var bPlan = await Plan(root, ("module-a.txt", "a"), ("module-b.txt", "b"));
            await GenerationRollbackCheckpointStore.CreateAsync(root, b, bPlan);
            await Apply(lease, b, bPlan);
            var aCheckpoint = await GenerationRollbackCheckpointStore.ReadAsync(root, a);
            await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(async () =>
                await GenerationWorkspaceJournal.PrepareAsync(lease, Guid.CreateVersion7(), Guid.CreateVersion7(), "rollback", "cli", null, aPlan, a));
            var rollbackId = Guid.CreateVersion7();
            var reverse = await GenerationRollbackWorkspace.PlanAsync(root, await GenerationRollbackCheckpointStore.ReadAsync(root, b));
            await GenerationWorkspaceJournal.PrepareAsync(lease, rollbackId, rollbackId, "rollback", "cli", null, reverse, b);
            await GenerationWorkspaceJournal.CommitAsync(lease);
            await GenerationWorkspaceJournal.CompleteAsync(lease);
            var afterB = await GenerationWorkspaceJournal.ReadHeadAsync(lease);
            Assert.AreEqual(a, afterB.TipOperationId);
            Assert.AreEqual(beforeB.Revision + 2, afterB.Revision);
            Assert.AreNotEqual(beforeB, afterB);
            var reverseA = await GenerationRollbackWorkspace.PlanAsync(root, aCheckpoint);
            var finalId = Guid.CreateVersion7();
            await GenerationWorkspaceJournal.PrepareAsync(lease, finalId, finalId, "rollback", "cli", null, reverseA, a);
            await GenerationWorkspaceJournal.CommitAsync(lease);
            await GenerationWorkspaceJournal.CompleteAsync(lease);
            Assert.IsNull((await GenerationWorkspaceJournal.ReadHeadAsync(lease)).TipOperationId);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task Disposed_lease_and_tampered_journal_cannot_authorize_writes()
    {
        var root = Directory.CreateTempSubdirectory("fullnet-codegen-journal-fence-").FullName;
        try
        {
            var lease = await Acquire(root);
            var id = Guid.CreateVersion7();
            await GenerationWorkspaceJournal.PrepareAsync(lease, id, id, "apply", "cli", null, await Plan(root, ("a.txt", "safe")));
            await lease.DisposeAsync();
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => GenerationWorkspaceJournal.CommitAsync(lease));
            await using var active = await Acquire(root);
            var path = Path.Combine(root, ".fullnet", "codegeneration-active-operation.json");
            await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("safe", "tampered", StringComparison.Ordinal));
            await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => GenerationWorkspaceJournal.CommitAsync(active));
            Assert.IsFalse(File.Exists(Path.Combine(root, "a.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task<GenerationWorkspaceLease> Acquire(string root) => await GenerationWorkspaceLease.TryAcquireAsync(root)
        ?? throw new InvalidOperationException("测试工作区被占用。");

    private static async Task<GenerationWritePlan> Plan(string root, params (string Path, string Content)[] files)
    {
        var artifacts = files.Select(file => new GeneratedArtifact(file.Path, GeneratedArtifactKind.Backend, file.Content)).ToArray();
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(root, artifacts);
        return GenerationWritePlanner.Plan(artifacts, snapshot.ExistingFiles, snapshot.PreviousManifest);
    }

    private static async Task Apply(GenerationWorkspaceLease lease, Guid id, GenerationWritePlan plan)
    {
        await GenerationWorkspaceJournal.PrepareAsync(lease, id, id, "apply", "cli", null, plan);
        await GenerationWorkspaceJournal.CommitAsync(lease);
        await GenerationWorkspaceJournal.CompleteAsync(lease);
    }
}
