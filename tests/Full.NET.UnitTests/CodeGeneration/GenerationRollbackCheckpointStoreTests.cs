using Full.NET.Data.CodeGeneration.Generation;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class GenerationRollbackCheckpointStoreTests
{
    private static readonly Guid ApplyRunId = Guid.Parse(
        "0198f7b3-34af-704c-8d2e-fc6aec9bf201");

    [TestMethod]
    public async Task Create_persists_exact_previous_content_and_planned_manifest()
    {
        using var workspace = new TemporaryDirectory();
        const string relativePath = "backend/Product.g.cs";
        const string previousContent = "old-content\n";
        const string nextContent = "new-content\n";
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
                nextContent),
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
        var checkpoint = await GenerationRollbackCheckpointStore.ReadAsync(
            workspace.Path,
            ApplyRunId);

        Assert.AreEqual(ApplyRunId, checkpoint.ApplyRunId);
        Assert.AreEqual(
            plan.PreviousManifest!.ToJson(),
            checkpoint.PreviousManifest!.ToJson());
        Assert.AreEqual(
            plan.NextManifest!.ToJson(),
            checkpoint.AppliedManifest.ToJson());
        Assert.AreEqual(previousContent, checkpoint.PreviousContents[relativePath]);
    }

    [TestMethod]
    public async Task Create_rejects_duplicate_apply_run_without_overwriting_checkpoint()
    {
        using var workspace = new TemporaryDirectory();
        var plan = await CreateUpdatePlanAsync(workspace);
        await GenerationRollbackCheckpointStore.CreateAsync(
            workspace.Path,
            ApplyRunId,
            plan);

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationRollbackCheckpointStore.CreateAsync(
                workspace.Path,
                ApplyRunId,
                plan));

        var checkpoint = await GenerationRollbackCheckpointStore.ReadAsync(
            workspace.Path,
            ApplyRunId);
        Assert.AreEqual(
            plan.NextManifest!.ToJson(),
            checkpoint.AppliedManifest.ToJson());
    }

    [TestMethod]
    public async Task Create_rejects_previous_owned_content_changed_after_plan()
    {
        using var workspace = new TemporaryDirectory();
        var plan = await CreateUpdatePlanAsync(workspace);
        workspace.Write("backend/Product.g.cs", "user-edited-content\n");

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationRollbackCheckpointStore.CreateAsync(
                workspace.Path,
                ApplyRunId,
                plan));

        Assert.IsFalse(Directory.Exists(Path.Combine(
            workspace.Path,
            GenerationRollbackCheckpointStore.RootRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar),
            ApplyRunId.ToString("N"))));
    }

    [TestMethod]
    public async Task Create_rejects_previous_owned_content_missing_after_plan()
    {
        using var workspace = new TemporaryDirectory();
        var plan = await CreateUpdatePlanAsync(workspace);
        File.Delete(workspace.GetFullPath("backend/Product.g.cs"));

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationRollbackCheckpointStore.CreateAsync(
                workspace.Path,
                ApplyRunId,
                plan));

        Assert.IsFalse(Directory.Exists(workspace.GetCheckpointPath()));
    }

    [TestMethod]
    public async Task Read_rejects_checkpoint_content_changed_after_publication()
    {
        using var workspace = new TemporaryDirectory();
        var plan = await CreateUpdatePlanAsync(workspace);
        await GenerationRollbackCheckpointStore.CreateAsync(
            workspace.Path,
            ApplyRunId,
            plan);
        var previousEntry = plan.PreviousManifest!.Artifacts.Single();
        workspace.Write(
            $"{GenerationRollbackCheckpointStore.RootRelativePath}/"
            + $"{ApplyRunId:N}/contents/{previousEntry.Sha256}.txt",
            "tampered-content\n");

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => GenerationRollbackCheckpointStore.ReadAsync(
                workspace.Path,
                ApplyRunId));
    }

    [TestMethod]
    public async Task Read_rejects_checkpoint_content_reparse_point()
    {
        using var workspace = new TemporaryDirectory();
        using var external = new TemporaryDirectory();
        var plan = await CreateUpdatePlanAsync(workspace);
        await GenerationRollbackCheckpointStore.CreateAsync(
            workspace.Path,
            ApplyRunId,
            plan);
        var previousEntry = plan.PreviousManifest!.Artifacts.Single();
        var contentsRelativePath =
            $"{GenerationRollbackCheckpointStore.RootRelativePath}/"
            + $"{ApplyRunId:N}/contents";
        var contentsPath = workspace.GetFullPath(contentsRelativePath);
        external.Write(
            $"contents/{previousEntry.Sha256}.txt",
            "old-content\n");
        Directory.Delete(contentsPath, recursive: true);

        try
        {
            Directory.CreateSymbolicLink(
                contentsPath,
                external.GetFullPath("contents"));
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException
                or PlatformNotSupportedException
                or IOException)
        {
            Assert.Inconclusive(
                $"当前文件系统不能创建目录符号链接：{exception.Message}");
            return;
        }

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationRollbackCheckpointStore.ReadAsync(
                workspace.Path,
                ApplyRunId));
    }

    [TestMethod]
    public async Task Read_rejects_malformed_checkpoint_metadata()
    {
        using var workspace = new TemporaryDirectory();
        var plan = await CreateUpdatePlanAsync(workspace);
        await GenerationRollbackCheckpointStore.CreateAsync(
            workspace.Path,
            ApplyRunId,
            plan);
        workspace.Write(
            $"{GenerationRollbackCheckpointStore.RootRelativePath}/"
            + $"{ApplyRunId:N}/checkpoint.json",
            "{not-json\n");

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => GenerationRollbackCheckpointStore.ReadAsync(
                workspace.Path,
                ApplyRunId));
    }

    [TestMethod]
    public async Task Read_rejects_valid_but_changed_applied_manifest()
    {
        using var workspace = new TemporaryDirectory();
        var plan = await CreateUpdatePlanAsync(workspace);
        await GenerationRollbackCheckpointStore.CreateAsync(
            workspace.Path,
            ApplyRunId,
            plan);
        var metadataRelativePath =
            $"{GenerationRollbackCheckpointStore.RootRelativePath}/"
            + $"{ApplyRunId:N}/checkpoint.json";
        var document = JsonNode.Parse(
            await File.ReadAllTextAsync(
                workspace.GetFullPath(metadataRelativePath)))!;
        document["appliedManifest"] = GenerationManifest.Create([]).ToJson();
        workspace.Write(
            metadataRelativePath,
            document.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
            }) + "\n");

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => GenerationRollbackCheckpointStore.ReadAsync(
                workspace.Path,
                ApplyRunId));
    }

    [TestMethod]
    public async Task Read_rejects_checkpoint_path_casing_alias()
    {
        using var workspace = new TemporaryDirectory();
        var plan = await CreateUpdatePlanAsync(workspace);
        await GenerationRollbackCheckpointStore.CreateAsync(
            workspace.Path,
            ApplyRunId,
            plan);
        var metadataPath = workspace.GetFullPath(
            $"{GenerationRollbackCheckpointStore.RootRelativePath}/"
            + $"{ApplyRunId:N}/checkpoint.json");
        var intermediatePath = metadataPath + ".renaming";
        var aliasPath = workspace.GetFullPath(
            $"{GenerationRollbackCheckpointStore.RootRelativePath}/"
            + $"{ApplyRunId:N}/Checkpoint.json");
        File.Move(metadataPath, intermediatePath);
        File.Move(intermediatePath, aliasPath);

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationRollbackCheckpointStore.ReadAsync(
                workspace.Path,
                ApplyRunId));
    }

    [TestMethod]
    public async Task TryDelete_removes_checkpoint_directory_idempotently()
    {
        using var workspace = new TemporaryDirectory();
        var plan = await CreateUpdatePlanAsync(workspace);
        await GenerationRollbackCheckpointStore.CreateAsync(
            workspace.Path,
            ApplyRunId,
            plan);

        var deleted = await GenerationRollbackCheckpointStore.TryDeleteAsync(
            workspace.Path,
            ApplyRunId);
        Assert.IsTrue(deleted);
        Assert.IsFalse(Directory.Exists(workspace.GetCheckpointPath()));

        var deletedAgain = await GenerationRollbackCheckpointStore.TryDeleteAsync(
            workspace.Path,
            ApplyRunId);
        Assert.IsFalse(deletedAgain);
    }

    private static async Task<GenerationWritePlan> CreateUpdatePlanAsync(
        TemporaryDirectory workspace)
    {
        const string relativePath = "backend/Product.g.cs";
        const string previousContent = "old-content\n";
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
                "new-content\n"),
        };
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.Path,
            artifacts);
        return GenerationWritePlanner.Plan(
            artifacts,
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory(
                "fullnet-codegeneration-rollback-checkpoint-").FullName;
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

        public string GetCheckpointPath()
        {
            return GetFullPath(
                $"{GenerationRollbackCheckpointStore.RootRelativePath}/"
                + $"{ApplyRunId:N}");
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
