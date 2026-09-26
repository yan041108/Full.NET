using Full.NET.Data.CodeGeneration.Generation;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class GenerationRootArtifactTests
{
    private string _root = null!;
    private const string Name = "report.generated.txt";

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "fullnet-root-artifact-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public async Task Root_artifact_create_update_and_repeat_keep_manifest_ownership()
    {
        await ApplyAsync("first\n");
        await ApplyAsync("second\n");
        var manifest = await File.ReadAllBytesAsync(PathOf(GenerationWorkspaceStore.ManifestRelativePath));
        await ApplyAsync("second\n");
        Assert.AreEqual("second\n", await File.ReadAllTextAsync(PathOf(Name)));
        CollectionAssert.AreEqual(manifest, await File.ReadAllBytesAsync(PathOf(GenerationWorkspaceStore.ManifestRelativePath)));
        var owned = await GenerationWorkspaceStore.ReadManifestOrEmptyAsync(_root);
        Assert.AreEqual(Name, owned.Artifacts.Single().RelativePath);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Root_artifact_failure_recovers_create_and_update(bool update)
    {
        if (update) await ApplyAsync("old\n");
        var plan = await PlanAsync("new\n");
        await Assert.ThrowsExactlyAsync<IOException>(() => GenerationWorkspaceStore.ApplyForTestingAsync(
            _root, plan, () => Task.FromException(new IOException("清单前失败"))));
        if (update) Assert.AreEqual("old\n", await File.ReadAllTextAsync(PathOf(Name)));
        else Assert.IsFalse(File.Exists(PathOf(Name)));
        await GenerationWorkspaceStore.ApplyAsync(_root, plan);
        Assert.AreEqual("new\n", await File.ReadAllTextAsync(PathOf(Name)));
    }

    [TestMethod]
    public async Task Unowned_root_file_still_rejects_overwrite()
    {
        await File.WriteAllTextAsync(PathOf(Name), "人工内容\n");
        var plan = await PlanAsync("generated\n");
        Assert.IsFalse(plan.CanApply);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => GenerationWorkspaceStore.ApplyAsync(_root, plan));
        Assert.AreEqual("人工内容\n", await File.ReadAllTextAsync(PathOf(Name)));
        Assert.IsFalse(File.Exists(PathOf(GenerationWorkspaceStore.ManifestRelativePath)));
    }

    [TestMethod]
    [DataRow("../outside.txt")]
    [DataRow("/outside.txt")]
    [DataRow(".")]
    [DataRow("nested/../outside.txt")]
    public void Root_parent_support_does_not_accept_unsafe_artifact_paths(string relative)
    {
        Assert.ThrowsExactly<ArgumentException>(() => GenerationWorkspacePath.EnsureParentDirectory(_root, relative));
        Assert.ThrowsExactly<ArgumentException>(() => GenerationWorkspacePath.Resolve(_root, relative));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(_root).Length);
    }

    [TestMethod]
    public void Root_parent_check_still_rejects_reparse_root()
    {
        var actual = PathOf("actual");
        var link = PathOf("linked");
        Directory.CreateDirectory(actual);
        Directory.CreateSymbolicLink(link, actual);
        try
        {
            Assert.ThrowsExactly<GenerationWorkspaceConflictException>(() =>
                GenerationWorkspacePath.EnsureParentDirectory(link, Name));
            Assert.IsFalse(File.Exists(Path.Combine(actual, Name)));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    private async Task ApplyAsync(string content) => await GenerationWorkspaceStore.ApplyAsync(_root, await PlanAsync(content));
    private async Task<GenerationWritePlan> PlanAsync(string content)
    {
        GeneratedArtifact[] artifacts = [new(Name, GeneratedArtifactKind.Report, content)];
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(_root, artifacts);
        return GenerationWritePlanner.Plan(artifacts, snapshot.ExistingFiles, snapshot.PreviousManifest);
    }
    private string PathOf(string relative) => Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
}
