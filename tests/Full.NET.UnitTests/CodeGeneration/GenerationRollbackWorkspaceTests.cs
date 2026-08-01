using Full.NET.Data.CodeGeneration.Generation;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class GenerationRollbackWorkspaceTests
{
    private static readonly Guid ApplyRunId = Guid.Parse(
        "0198f7b3-34af-704c-8d2e-fc6aec9bf301");

    [TestMethod]
    public async Task Plan_reverses_update_create_and_delete_to_previous_owned_state()
    {
        using var workspace = new TemporaryDirectory();
        const string updatedPath = "backend/Product.g.cs";
        const string deletedPath = "backend/Legacy.g.cs";
        const string createdPath = "backend/NewProduct.g.cs";
        const string previousUpdated = "previous-product\n";
        const string previousDeleted = "previous-legacy\n";
        const string appliedUpdated = "applied-product\n";
        const string appliedCreated = "applied-new\n";

        workspace.Write(updatedPath, previousUpdated);
        workspace.Write(deletedPath, previousDeleted);
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            GenerationManifest.Create(
            [
                new GenerationManifestEntry(
                    updatedPath,
                    GenerationContentHash.Compute(previousUpdated)),
                new GenerationManifestEntry(
                    deletedPath,
                    GenerationContentHash.Compute(previousDeleted)),
            ]).ToJson());

        var artifacts = new[]
        {
            new GeneratedArtifact(
                updatedPath,
                GeneratedArtifactKind.Backend,
                appliedUpdated),
            new GeneratedArtifact(
                createdPath,
                GeneratedArtifactKind.Backend,
                appliedCreated),
        };
        var forwardSnapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.Path,
            artifacts);
        var forwardPlan = GenerationWritePlanner.Plan(
            artifacts,
            forwardSnapshot.ExistingFiles,
            forwardSnapshot.PreviousManifest);
        Assert.IsTrue(forwardPlan.CanApply);
        await GenerationRollbackCheckpointStore.CreateAsync(
            workspace.Path,
            ApplyRunId,
            forwardPlan);
        await GenerationWorkspaceStore.ApplyAsync(workspace.Path, forwardPlan);
        var checkpoint = await GenerationRollbackCheckpointStore.ReadAsync(
            workspace.Path,
            ApplyRunId);

        var reversePlan = await GenerationRollbackWorkspace.PlanAsync(
            workspace.Path,
            checkpoint);

        Assert.IsTrue(reversePlan.CanApply);
        Assert.AreEqual(
            checkpoint.PreviousManifest!.ToJson(),
            reversePlan.NextManifest!.ToJson());
        Assert.AreEqual(
            checkpoint.AppliedManifest.ToJson(),
            reversePlan.PreviousManifest!.ToJson());
        CollectionAssert.AreEqual(
            new[]
            {
                GenerationWriteActionKind.Create,
                GenerationWriteActionKind.Delete,
                GenerationWriteActionKind.Update,
            },
            reversePlan.Actions.Select(action => action.Kind).ToArray());
        Assert.AreEqual(deletedPath, reversePlan.Actions[0].RelativePath);
        Assert.AreEqual(previousDeleted, reversePlan.Actions[0].Content);
        Assert.AreEqual(createdPath, reversePlan.Actions[1].RelativePath);
        Assert.AreEqual(updatedPath, reversePlan.Actions[2].RelativePath);
        Assert.AreEqual(previousUpdated, reversePlan.Actions[2].Content);
    }

    [TestMethod]
    public async Task Plan_rejects_replaced_manifest_before_any_write()
    {
        using var workspace = new TemporaryDirectory();
        var checkpoint = await PublishAppliedCheckpointAsync(workspace);
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            GenerationManifest.Create(
            [
                new GenerationManifestEntry(
                    "backend/Other.g.cs",
                    GenerationContentHash.Compute("other\n")),
            ]).ToJson());
        var fingerprint = SnapshotFingerprint(workspace);

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationRollbackWorkspace.PlanAsync(
                workspace.Path,
                checkpoint));

        Assert.AreEqual(fingerprint, SnapshotFingerprint(workspace));
    }

    [TestMethod]
    public async Task Plan_rejects_modified_applied_file_before_any_write()
    {
        using var workspace = new TemporaryDirectory();
        var checkpoint = await PublishAppliedCheckpointAsync(workspace);
        workspace.Write("backend/Product.g.cs", "user-drift\n");
        var fingerprint = SnapshotFingerprint(workspace);

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationRollbackWorkspace.PlanAsync(
                workspace.Path,
                checkpoint));

        Assert.AreEqual(fingerprint, SnapshotFingerprint(workspace));
    }

    [TestMethod]
    public async Task Plan_rejects_deleted_applied_file_before_any_write()
    {
        using var workspace = new TemporaryDirectory();
        var checkpoint = await PublishAppliedCheckpointAsync(workspace);
        File.Delete(workspace.GetFullPath("backend/Product.g.cs"));
        var fingerprint = SnapshotFingerprint(workspace);

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationRollbackWorkspace.PlanAsync(
                workspace.Path,
                checkpoint));

        Assert.AreEqual(fingerprint, SnapshotFingerprint(workspace));
    }

    [TestMethod]
    public async Task Plan_rejects_path_casing_alias_before_any_write()
    {
        using var workspace = new TemporaryDirectory();
        var checkpoint = await PublishAppliedCheckpointAsync(workspace);
        var fullPath = workspace.GetFullPath("backend/Product.g.cs");
        var tempPath = workspace.GetFullPath("backend/product-temp.g.cs");
        var renamedPath = workspace.GetFullPath("backend/PRODUCT.g.cs");
        File.Move(fullPath, tempPath);
        File.Move(tempPath, renamedPath);
        var fingerprint = SnapshotFingerprint(workspace);

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationRollbackWorkspace.PlanAsync(
                workspace.Path,
                checkpoint));

        Assert.AreEqual(fingerprint, SnapshotFingerprint(workspace));
    }

    [TestMethod]
    public async Task Plan_rejects_reparse_point_before_any_write()
    {
        using var workspace = new TemporaryDirectory();
        using var external = new TemporaryDirectory();
        var checkpoint = await PublishAppliedCheckpointAsync(workspace);
        var productPath = workspace.GetFullPath("backend/Product.g.cs");
        var externalPath = external.GetFullPath("Product.g.cs");
        File.Copy(productPath, externalPath);
        File.Delete(productPath);

        try
        {
            File.CreateSymbolicLink(productPath, externalPath);
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException
                or PlatformNotSupportedException
                or IOException)
        {
            Assert.Inconclusive(
                "当前文件系统不能创建文件符号链接：" + exception.Message);
            return;
        }

        var fingerprint = SnapshotFingerprint(workspace);

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationRollbackWorkspace.PlanAsync(
                workspace.Path,
                checkpoint));

        Assert.AreEqual(fingerprint, SnapshotFingerprint(workspace));
    }


    [TestMethod]
    public async Task Restore_restores_mixed_create_update_delete_byte_for_byte()
    {
        using var workspace = new TemporaryDirectory();
        const string updatedPath = "backend/Product.g.cs";
        const string deletedPath = "backend/Legacy.g.cs";
        const string createdPath = "backend/NewProduct.g.cs";
        const string previousUpdated = "previous-product\n";
        const string previousDeleted = "previous-legacy\n";
        workspace.Write(updatedPath, previousUpdated);
        workspace.Write(deletedPath, previousDeleted);
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            GenerationManifest.Create(
            [
                new GenerationManifestEntry(
                    updatedPath,
                    GenerationContentHash.Compute(previousUpdated)),
                new GenerationManifestEntry(
                    deletedPath,
                    GenerationContentHash.Compute(previousDeleted)),
            ]).ToJson());
        var artifacts = new[]
        {
            new GeneratedArtifact(
                updatedPath,
                GeneratedArtifactKind.Backend,
                "applied-product\n"),
            new GeneratedArtifact(
                createdPath,
                GeneratedArtifactKind.Backend,
                "applied-new\n"),
        };
        var forwardSnapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.Path,
            artifacts);
        var forwardPlan = GenerationWritePlanner.Plan(
            artifacts,
            forwardSnapshot.ExistingFiles,
            forwardSnapshot.PreviousManifest);
        await GenerationRollbackCheckpointStore.CreateAsync(
            workspace.Path,
            ApplyRunId,
            forwardPlan);
        await GenerationWorkspaceStore.ApplyAsync(workspace.Path, forwardPlan);
        var checkpoint = await GenerationRollbackCheckpointStore.ReadAsync(
            workspace.Path,
            ApplyRunId);

        var restored = await GenerationRollbackWorkspace.RestoreAsync(
            workspace.Path,
            checkpoint);

        Assert.IsTrue(restored.CanApply);
        Assert.AreEqual(
            checkpoint.PreviousManifest!.ToJson(),
            File.ReadAllText(workspace.GetFullPath(
                GenerationWorkspaceStore.ManifestRelativePath)));
        Assert.AreEqual(previousUpdated, File.ReadAllText(workspace.GetFullPath(updatedPath)));
        Assert.AreEqual(previousDeleted, File.ReadAllText(workspace.GetFullPath(deletedPath)));
        Assert.IsFalse(File.Exists(workspace.GetFullPath(createdPath)));
        Assert.IsTrue(Directory.Exists(workspace.GetFullPath(
            $"{GenerationRollbackCheckpointStore.RootRelativePath}/{ApplyRunId:N}")));
    }

    [TestMethod]
    public async Task Restore_first_apply_leaves_canonical_empty_manifest()
    {
        using var workspace = new TemporaryDirectory();
        const string createdPath = "backend/First.g.cs";
        const string createdContent = "first-content\n";
        var artifacts = new[]
        {
            new GeneratedArtifact(
                createdPath,
                GeneratedArtifactKind.Backend,
                createdContent),
        };
        var forwardSnapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.Path,
            artifacts);
        var forwardPlan = GenerationWritePlanner.Plan(
            artifacts,
            forwardSnapshot.ExistingFiles,
            forwardSnapshot.PreviousManifest);
        Assert.IsNull(forwardPlan.PreviousManifest);
        await GenerationRollbackCheckpointStore.CreateAsync(
            workspace.Path,
            ApplyRunId,
            forwardPlan);
        await GenerationWorkspaceStore.ApplyAsync(workspace.Path, forwardPlan);
        var checkpoint = await GenerationRollbackCheckpointStore.ReadAsync(
            workspace.Path,
            ApplyRunId);

        var restored = await GenerationRollbackWorkspace.RestoreAsync(
            workspace.Path,
            checkpoint);

        Assert.IsTrue(restored.CanApply);
        Assert.AreEqual(
            GenerationManifest.Create([]).ToJson(),
            File.ReadAllText(workspace.GetFullPath(
                GenerationWorkspaceStore.ManifestRelativePath)));
        Assert.IsFalse(File.Exists(workspace.GetFullPath(createdPath)));
    }

    [TestMethod]
    public async Task Restore_stale_checkpoint_causes_zero_mutation()
    {
        using var workspace = new TemporaryDirectory();
        var checkpoint = await PublishAppliedCheckpointAsync(workspace);
        workspace.Write("backend/Product.g.cs", "user-drift\n");
        var fingerprint = SnapshotFingerprint(workspace);

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationRollbackWorkspace.RestoreAsync(
                workspace.Path,
                checkpoint));

        Assert.AreEqual(fingerprint, SnapshotFingerprint(workspace));
    }

    [TestMethod]
    public async Task Restore_cancelled_before_planning_causes_zero_mutation()
    {
        using var workspace = new TemporaryDirectory();
        var checkpoint = await PublishAppliedCheckpointAsync(workspace);
        var fingerprint = SnapshotFingerprint(workspace);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => GenerationRollbackWorkspace.RestoreAsync(
                workspace.Path,
                checkpoint,
                cancellation.Token));

        Assert.AreEqual(fingerprint, SnapshotFingerprint(workspace));
    }
    private static async Task<GenerationRollbackCheckpoint>
        PublishAppliedCheckpointAsync(TemporaryDirectory workspace)
    {
        const string relativePath = "backend/Product.g.cs";
        const string previousContent = "old-content\n";
        const string appliedContent = "new-content\n";
        workspace.Write(relativePath, previousContent);
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            GenerationManifest.Create(
            [
                new GenerationManifestEntry(
                    relativePath,
                    GenerationContentHash.Compute(previousContent)),
            ]).ToJson());
        var artifacts = new[]
        {
            new GeneratedArtifact(
                relativePath,
                GeneratedArtifactKind.Backend,
                appliedContent),
        };
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.Path,
            artifacts);
        var plan = GenerationWritePlanner.Plan(
            artifacts,
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);
        await GenerationRollbackCheckpointStore.CreateAsync(
            workspace.Path,
            ApplyRunId,
            plan);
        await GenerationWorkspaceStore.ApplyAsync(workspace.Path, plan);
        return await GenerationRollbackCheckpointStore.ReadAsync(
            workspace.Path,
            ApplyRunId);
    }

    private static string SnapshotFingerprint(TemporaryDirectory workspace)
    {
        var entries = Directory
            .EnumerateFileSystemEntries(
                workspace.Path,
                "*",
                SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var relative = Path.GetRelativePath(workspace.Path, path)
                    .Replace('\\', '/');
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    return "D:" + relative;
                }

                var content = File.ReadAllBytes(path);
                return "F:" + relative + ":" + Convert.ToHexString(content);
            });
        return string.Join('\n', entries);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory(
                "fullnet-codegeneration-rollback-workspace-").FullName;
        }

        public string Path { get; }

        public void Write(string relativePath, string content)
        {
            var fullPath = GetFullPath(relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        public string GetFullPath(string relativePath)
        {
            return System.IO.Path.Combine(
                Path,
                relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}