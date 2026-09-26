using System.Security.Cryptography;
using System.Text;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Integration;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class IntegrationCommitBoundaryTests
{
    [TestMethod]
    [DataRow("file")]
    [DataRow("parent")]
    [DataRow("alias")]
    [DataRow("directory")]
    public async Task Entry_commit_rejects_path_replacement_after_candidate_compilation(string kind)
    {
        using var fixture = new CommitFixture();
        fixture.Replace(fixture.Entry, kind);
        var outside = fixture.CaptureOutside();
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => fixture.CommitEntry());
        CollectionAssert.AreEquivalent(outside, fixture.CaptureOutside());
        Assert.AreEqual(0, fixture.TemporaryFiles().Length);
    }

    [TestMethod]
    [DataRow("entry", "file")]
    [DataRow("entry", "parent")]
    [DataRow("entry", "alias")]
    [DataRow("entry", "directory")]
    [DataRow("project", "file")]
    [DataRow("project", "parent")]
    [DataRow("project", "alias")]
    [DataRow("project", "directory")]
    [DataRow("catalog", "file")]
    [DataRow("catalog", "parent")]
    [DataRow("catalog", "alias")]
    [DataRow("catalog", "directory")]
    public async Task Composition_commit_rejects_path_replacement_after_candidate_compilation(string target, string kind)
    {
        using var fixture = new CommitFixture();
        fixture.Replace(fixture.PathFor(target), kind);
        var outside = fixture.CaptureOutside();
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => fixture.CommitComposition());
        CollectionAssert.AreEquivalent(outside, fixture.CaptureOutside());
        Assert.AreEqual(0, fixture.TemporaryFiles().Length);
    }

    [TestMethod]
    [DataRow("entry", "file")]
    [DataRow("entry", "dangling")]
    [DataRow("entry", "alias")]
    [DataRow("entry", "directory")]
    [DataRow("composition", "file")]
    [DataRow("composition", "dangling")]
    [DataRow("composition", "alias")]
    [DataRow("composition", "directory")]
    [DataRow("composition-module", "file")]
    [DataRow("composition-module", "dangling")]
    [DataRow("composition-module", "alias")]
    [DataRow("composition-module", "directory")]
    public async Task Commit_rejects_unsafe_lock_before_opening_it(string mode, string kind)
    {
        using var fixture = new CommitFixture();
        var lockPath = fixture.LockFor(mode);
        fixture.Replace(lockPath, kind);
        var outside = fixture.CaptureOutside();
        var exception = await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => mode == "entry" ? fixture.CommitEntry() : fixture.CommitComposition());
        StringAssert.Contains(exception.Message, kind == "directory" ? "目录占用" : kind == "alias" ? "大小写" : "链接");
        CollectionAssert.AreEquivalent(outside, fixture.CaptureOutside());
        Assert.AreEqual(CommitFixture.EntryContent, File.ReadAllText(fixture.Entry));
        Assert.AreEqual(CommitFixture.ProjectContent, File.ReadAllText(fixture.Project));
        Assert.AreEqual(CommitFixture.CatalogContent, File.ReadAllText(fixture.Catalog));
        Assert.AreEqual(0, fixture.TemporaryFiles().Length);
    }

    [TestMethod]
    [DataRow("entry")]
    [DataRow("composition")]
    [DataRow("composition-module")]
    public async Task Commit_rejects_held_lock_without_changing_targets(string mode)
    {
        using var fixture = new CommitFixture();
        using var held = new FileStream(fixture.LockFor(mode), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => mode == "entry" ? fixture.CommitEntry() : fixture.CommitComposition());
        Assert.AreEqual(CommitFixture.EntryContent, File.ReadAllText(fixture.Entry));
        Assert.AreEqual(CommitFixture.ProjectContent, File.ReadAllText(fixture.Project));
        Assert.AreEqual(CommitFixture.CatalogContent, File.ReadAllText(fixture.Catalog));
    }

    [TestMethod]
    [DataRow("entry")]
    [DataRow("composition-entry")]
    [DataRow("project")]
    [DataRow("catalog")]
    public async Task Commit_preserves_handwritten_content_changed_after_compilation(string target)
    {
        using var fixture = new CommitFixture();
        var changedPath = fixture.PathFor(target == "composition-entry" ? "entry" : target);
        File.WriteAllText(changedPath, "人工并发修改\n", new UTF8Encoding(false));
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => target == "entry" ? fixture.CommitEntry() : fixture.CommitComposition());
        Assert.AreEqual("人工并发修改\n", File.ReadAllText(changedPath));
        Assert.AreEqual(0, fixture.TemporaryFiles().Length);
    }

    [TestMethod]
    [DataRow("entry")]
    [DataRow("composition")]
    public async Task Commit_updates_regular_targets_and_removes_staging_files(string mode)
    {
        using var fixture = new CommitFixture();
        if (mode == "entry")
        {
            await fixture.CommitEntry();
            Assert.AreEqual("updated entry\n", File.ReadAllText(fixture.Entry));
        }
        else
        {
            await fixture.CommitComposition();
            Assert.AreEqual("updated project\n", File.ReadAllText(fixture.Project));
            Assert.AreEqual("updated catalog\n", File.ReadAllText(fixture.Catalog));
        }
        Assert.AreEqual(0, fixture.TemporaryFiles().Length);
        Assert.IsFalse(File.Exists(fixture.LockFor("entry")));
        Assert.AreEqual(mode == "entry", File.Exists(fixture.LockFor("composition")));
    }

    private sealed class CommitFixture : IDisposable
    {
        public const string EntryContent = "original entry\n";
        public const string ProjectContent = "original project\n";
        public const string CatalogContent = "original catalog\n";
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"fullnet-integration-commit-{Guid.NewGuid():N}");
        private readonly List<string> _directoryLinks = [];

        public CommitFixture()
        {
            Repository = Path.Combine(_root, "repository");
            Outside = Path.Combine(_root, "outside");
            Module = Path.Combine(Repository, "src/Modules/Acme.Modules.Catalog");
            Entry = Path.Combine(Module, "CatalogModule.cs");
            Project = Path.Combine(Repository, "src/Composition/Acme.Composition/Acme.Composition.csproj");
            Catalog = Path.Combine(Path.GetDirectoryName(Project)!, "ModuleCatalog.cs");
            Directory.CreateDirectory(Outside);
            Write(Entry, EntryContent);
            Write(Project, ProjectContent);
            Write(Catalog, CatalogContent);
            Write(Path.Combine(Module, ".fullnet/codegeneration.lock"), "module lock");
            Write(Path.Combine(Repository, ".fullnet/codegeneration-composition.lock"), "composition lock");
            const string registry = "owned registry\n";
            var registryPath = ModuleIntegrationBackendWorkspace.RegistryRelativePath;
            Write(Path.Combine(Module, registryPath), registry);
            Write(Path.Combine(Module, GenerationWorkspaceStore.ManifestRelativePath),
                GenerationManifest.Create([new GenerationManifestEntry(registryPath,
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(registry))).ToLowerInvariant())]).ToJson());
        }

        public string Repository { get; }
        public string Outside { get; }
        public string Module { get; }
        public string Entry { get; }
        public string Project { get; }
        public string Catalog { get; }

        public string PathFor(string target) => target switch
        {
            "entry" => Entry,
            "project" => Project,
            "catalog" => Catalog,
            _ => throw new ArgumentException(target),
        };

        public string LockFor(string mode) => mode == "composition"
            ? Path.Combine(Repository, ".fullnet/codegeneration-composition.lock")
            : Path.Combine(Module, ".fullnet/codegeneration.lock");

        // 只调用生产提交阶段，模拟候选编译已经成功；不启动 MSBuild 或增加公共测试入口。
        public Task CommitEntry() => ModuleEntryIntegrationApplyCommand.ApplyUnderWorkspaceLockAsync(
            Repository, Module, Entry, EntryContent, "updated entry\n", CancellationToken.None);

        public Task CommitComposition() => CompositionIntegrationApplyCommand.CommitAsync(
            Repository, Module, Entry, EntryContent, Project, ProjectContent,
            CompositionIntegrationEditResult.Success(ProjectContent, "updated project\n"),
            Catalog, CatalogContent, CompositionIntegrationEditResult.Success(CatalogContent, "updated catalog\n"),
            CancellationToken.None);

        public void Replace(string path, string kind)
        {
            if (kind == "parent")
            {
                var parent = Path.Combine(Repository, "src");
                var relocated = Path.Combine(Outside, "src");
                Directory.Move(parent, relocated);
                Directory.CreateSymbolicLink(parent, relocated);
                _directoryLinks.Add(parent);
                return;
            }

            var outsideFile = Path.Combine(Outside, Path.GetFileName(path));
            if (kind is "file" or "dangling")
            {
                File.Move(path, outsideFile);
                File.CreateSymbolicLink(path, kind == "file" ? outsideFile : outsideFile + ".missing");
            }
            else if (kind == "alias")
            {
                File.Move(path, Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(path).ToUpperInvariant()));
            }
            else
            {
                File.Delete(path);
                Directory.CreateDirectory(path);
            }
        }

        public string[] CaptureOutside() => Directory.GetFiles(Outside, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Outside, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();

        public string[] TemporaryFiles() => Directory.GetFiles(Repository, ".fullnet-*.tmp", SearchOption.AllDirectories);

        private static void Write(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        public void Dispose()
        {
            foreach (var link in _directoryLinks) Directory.Delete(link);
            Directory.Delete(_root, recursive: true);
        }
    }
}
