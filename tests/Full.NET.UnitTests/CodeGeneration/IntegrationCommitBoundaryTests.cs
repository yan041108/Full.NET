using System.Security.Cryptography;
using System.Text;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Integration;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class IntegrationCommitBoundaryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Host_failure_never_returns_empty_diagnostics(bool whitespace)
    {
        var failure = ModuleIntegrationHostApplyResult.Failure(whitespace ? [" "] : Array.Empty<string>());
        Assert.IsFalse(failure.Succeeded);
        StringAssert.Contains(string.Join("\n", failure.Diagnostics), "接入失败");
    }

    [TestMethod]
    [DataRow(false, "file")]
    [DataRow(false, "dangling")]
    [DataRow(false, "alias")]
    [DataRow(false, "directory")]
    [DataRow(false, "invalid-utf8")]
    [DataRow(false, "plain")]
    [DataRow(true, "file")]
    [DataRow(true, "dangling")]
    [DataRow(true, "alias")]
    [DataRow(true, "directory")]
    [DataRow(true, "invalid-utf8")]
    [DataRow(true, "plain")]
    public async Task Authorization_pending_material_blocks_host_and_direct_commit(bool host, string kind)
    {
        using var fixture = new CommitFixture();
        var parent = Path.GetDirectoryName(fixture.Entry)!;
        var pending = Path.Combine(parent, ".fullnet-authorization-orphan.tmp");
        File.WriteAllText(pending, "pending review");
        if (kind == "invalid-utf8") File.WriteAllBytes(pending, [0xff, 0xfe, 0xff]);
        else if (kind != "plain") fixture.Replace(pending, kind);
        var outside = fixture.CaptureOutside();
        var entries = Directory.GetFileSystemEntries(parent);
        if (host)
        {
            var target = ModuleIntegrationTarget.Create("Catalog",
                "src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj",
                "src/Modules/Acme.Modules.Catalog/OtherModule.cs",
                "src/Composition/Acme.Composition/Acme.Composition.csproj",
                "src/Composition/Acme.Composition/ModuleCatalog.cs", "ui/admin/routes.ts", null,
                authorizationContributorPath: "src/Modules/Acme.Modules.Catalog/CatalogModule.cs");
            var result = await ModuleIntegrationHostOrchestrator.ApplyAsync(fixture.Repository,
                FullNetCrudSchemaTests.CreateProductSchema(), target, CancellationToken.None);
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(string.Join("\n", result.Diagnostics), "待审查");
        }
        else
        {
            var conflict = await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => fixture.CommitAuthorization());
            StringAssert.Contains(conflict.Message, "待审查");
        }
        CollectionAssert.AreEquivalent(outside, fixture.CaptureOutside());
        CollectionAssert.AreEquivalent(entries, Directory.GetFileSystemEntries(parent));
        Assert.AreEqual(CommitFixture.EntryContent, File.ReadAllText(fixture.Entry));
    }

    [TestMethod]
    public async Task Authorization_drifted_material_blocks_actual_retry()
    {
        using var fixture = new CommitFixture();
        string? staged = null;
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => fixture.CommitAuthorization(() =>
        {
            staged = fixture.TemporaryFiles().Single();
            File.WriteAllText(staged, "human staging change\n");
            return Task.CompletedTask;
        }));
        var conflict = await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => fixture.CommitAuthorization());
        StringAssert.Contains(conflict.Message, "待审查");
        Assert.AreEqual(CommitFixture.EntryContent, File.ReadAllText(fixture.Entry));
        Assert.IsNotNull(staged);
        Assert.AreEqual("human staging change\n", File.ReadAllText(staged));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Authorization_commit_preserves_concurrent_human_content(bool afterStaging)
    {
        using var fixture = new CommitFixture();
        const string human = "human concurrent entry\n";
        if (!afterStaging) File.WriteAllText(fixture.Entry, human);
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() =>
            fixture.CommitAuthorization(afterStaging ? () =>
            {
                File.WriteAllText(fixture.Entry, human);
                return Task.CompletedTask;
            } : null));
        Assert.AreEqual(human, File.ReadAllText(fixture.Entry));
        Assert.AreEqual(0, fixture.TemporaryFiles().Length);
    }

    [TestMethod]
    [DataRow("file")]
    [DataRow("dangling")]
    [DataRow("alias")]
    [DataRow("directory")]
    public async Task Authorization_commit_rejects_unsafe_lock(string kind)
    {
        using var fixture = new CommitFixture();
        var path = Path.Combine(fixture.Repository, ".fullnet/codegeneration-authorization.lock");
        File.WriteAllText(path, "lock");
        fixture.Replace(path, kind);
        var outside = fixture.CaptureOutside();
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => fixture.CommitAuthorization());
        CollectionAssert.AreEquivalent(outside, fixture.CaptureOutside());
        Assert.AreEqual(CommitFixture.EntryContent, File.ReadAllText(fixture.Entry));
        Assert.AreEqual(0, fixture.TemporaryFiles().Length);
    }

    [TestMethod]
    public async Task Authorization_commit_rejects_held_lock()
    {
        using var fixture = new CommitFixture();
        using var held = new FileStream(Path.Combine(fixture.Repository, ".fullnet/codegeneration-authorization.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => fixture.CommitAuthorization());
        Assert.AreEqual(CommitFixture.EntryContent, File.ReadAllText(fixture.Entry));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Authorization_commit_preserves_drifted_staging_material(bool failAfterDrift)
    {
        using var fixture = new CommitFixture();
        string? staged = null;
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => fixture.CommitAuthorization(() =>
        {
            staged = fixture.TemporaryFiles().Single();
            File.WriteAllText(staged, "human staging change\n");
            if (failAfterDrift) throw new IOException("staging fault after drift");
            return Task.CompletedTask;
        }));
        Assert.AreEqual(CommitFixture.EntryContent, File.ReadAllText(fixture.Entry));
        Assert.IsNotNull(staged);
        Assert.AreEqual("human staging change\n", File.ReadAllText(staged));
    }

    [TestMethod]
    public async Task Authorization_cleanup_preserves_locked_material_and_original_failure()
    {
        using var fixture = new CommitFixture();
        FileStream? writer = null;
        string? staged = null;
        try
        {
            var conflict = await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => fixture.CommitAuthorization(() =>
            {
                staged = fixture.TemporaryFiles().Single();
                writer = new FileStream(staged, FileMode.Open, FileAccess.Write, FileShare.None);
                throw new IOException("original commit fault");
            }));
            Assert.IsInstanceOfType<AggregateException>(conflict.InnerException);
            var failures = ((AggregateException)conflict.InnerException!).InnerExceptions;
            Assert.AreEqual(2, failures.Count);
            StringAssert.Contains(failures[0].Message, "original commit fault");
            Assert.AreEqual(CommitFixture.EntryContent, File.ReadAllText(fixture.Entry));
        }
        finally { writer?.Dispose(); }
        Assert.IsNotNull(staged);
        Assert.IsTrue(File.Exists(staged));
    }

    [TestMethod]
    public async Task Authorization_commit_staging_failure_preserves_original()
    {
        using var fixture = new CommitFixture();
        await Assert.ThrowsExactlyAsync<IOException>(() => fixture.CommitAuthorization(() => throw new IOException("staging fault")));
        Assert.AreEqual(CommitFixture.EntryContent, File.ReadAllText(fixture.Entry));
        Assert.AreEqual(0, fixture.TemporaryFiles().Length);
    }

    [TestMethod]
    public async Task Authorization_commit_success_replaces_content_and_cleans_material()
    {
        using var fixture = new CommitFixture();
        await fixture.CommitAuthorization();
        Assert.AreEqual("updated authorization\n", File.ReadAllText(fixture.Entry));
        Assert.AreEqual(0, fixture.TemporaryFiles().Length);
    }

    [TestMethod]
    [DataRow("file")]
    [DataRow("dangling")]
    [DataRow("parent")]
    [DataRow("alias")]
    [DataRow("directory")]
    [DataRow("missing")]
    public async Task Host_rejects_unsafe_authorization_target_before_backend_reads(string kind)
    {
        using var fixture = new CommitFixture();
        const string relative = "authorization/CatalogAuthorizationContributor.cs";
        var contributor = Path.Combine(fixture.Repository, relative);
        var parent = Path.GetDirectoryName(contributor)!;
        Directory.CreateDirectory(parent);
        File.WriteAllText(contributor, "human contributor\n");
        if (kind == "parent")
        {
            var outsideParent = Path.Combine(fixture.Outside, "authorization");
            Directory.Move(parent, outsideParent);
            Directory.CreateSymbolicLink(parent, outsideParent);
        }
        else if (kind == "missing") File.Delete(contributor);
        else fixture.Replace(contributor, kind);
        try
        {
            var outside = fixture.CaptureOutside();
            var target = ModuleIntegrationTarget.Create("Catalog",
                "src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj",
                "src/Modules/Acme.Modules.Catalog/CatalogModule.cs",
                "src/Composition/Acme.Composition/Acme.Composition.csproj",
                "src/Composition/Acme.Composition/ModuleCatalog.cs",
                "ui/admin/routes.ts", null, authorizationContributorPath: relative);
            // 模块项目缺失，必须先返回授权目标冲突，而非进入后端前置检查。
            var result = await ModuleIntegrationHostOrchestrator.ApplyAsync(fixture.Repository,
                FullNetCrudSchemaTests.CreateProductSchema(), target, CancellationToken.None);
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(string.Join("\n", result.Diagnostics), kind switch
            {
                "missing" => "AuthorizationContributor 文件不存在",
                "directory" => "目录占用",
                "alias" => "大小写",
                _ => "链接",
            });
            CollectionAssert.AreEquivalent(outside, fixture.CaptureOutside());
            Assert.AreEqual(CommitFixture.EntryContent, File.ReadAllText(fixture.Entry));
            Assert.AreEqual(CommitFixture.ProjectContent, File.ReadAllText(fixture.Project));
            Assert.AreEqual(CommitFixture.CatalogContent, File.ReadAllText(fixture.Catalog));
            Assert.AreEqual(0, fixture.TemporaryFiles().Length);
        }
        finally
        {
            if (kind == "parent") Directory.Delete(parent);
        }
    }

    [TestMethod]
    [DataRow("registration")]
    [DataRow("project")]
    [DataRow("catalog")]
    public async Task Host_blocks_pending_composition_before_backend_prerequisite_reads(string kind)
    {
        using var fixture = new CommitFixture(separateCatalog: true);
        var pending = kind == "registration"
            ? Path.Combine(fixture.Repository, CompositionIntegrationRecovery.MarkerRelativePath)
            : Path.Combine(Path.GetDirectoryName(kind == "project" ? fixture.Project : fixture.Catalog)!,
                ".fullnet-composition-orphan.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(pending)!);
        File.WriteAllText(pending, "pending review");
        var before = Directory.GetFiles(fixture.Repository, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllText);
        var target = ModuleIntegrationTarget.Create("Catalog",
            "src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj",
            "src/Modules/Acme.Modules.Catalog/CatalogModule.cs",
            "src/Composition/Acme.Composition/Acme.Composition.csproj",
            "catalog/ModuleCatalog.cs", "ui/admin/routes.ts", null);
        // 模块项目故意缺失：恢复门禁必须优先失败，不能进入后端前置检查或构建。
        var result = await ModuleIntegrationHostOrchestrator.ApplyAsync(fixture.Repository,
            FullNetCrudSchemaTests.CreateProductSchema(), target, CancellationToken.None);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join("\n", result.Diagnostics), "待审查");
        CollectionAssert.AreEquivalent(before.Keys.ToArray(),
            Directory.GetFiles(fixture.Repository, "*", SearchOption.AllDirectories));
        foreach (var file in before) Assert.AreEqual(file.Value, File.ReadAllText(file.Key));
    }

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

    [TestMethod]
    [DataRow("failure")]
    [DataRow("directory")]
    public async Task Composition_catalog_failure_restores_owned_project(string kind)
    {
        using var fixture = new CommitFixture();
        await Assert.ThrowsAsync<IOException>(() => fixture.CommitComposition(() =>
        {
            Assert.AreEqual("updated project\n", File.ReadAllText(fixture.Project));
            if (kind == "failure") throw new IOException("注入首次项目提交后的故障");
            fixture.Replace(fixture.Catalog, "directory");
            return Task.CompletedTask;
        }));
        Assert.AreEqual(CommitFixture.ProjectContent, File.ReadAllText(fixture.Project));
        if (kind == "failure") Assert.AreEqual(CommitFixture.CatalogContent, File.ReadAllText(fixture.Catalog));
        else Assert.IsTrue(Directory.Exists(fixture.Catalog));
        Assert.AreEqual(0, fixture.TemporaryFiles().Length);
    }

    [TestMethod]
    [DataRow("human")]
    [DataRow("deleted")]
    [DataRow("invalid-utf8")]
    [DataRow("link")]
    [DataRow("recovery-drift")]
    [DataRow("recovery-link")]
    [DataRow("recovery-deleted")]
    [DataRow("recovery-dangling")]
    public async Task Composition_rollback_preserves_concurrent_project_and_recovery_material(string kind)
    {
        using var fixture = new CommitFixture();
        string? recovery = null;
        string[] outside = [];
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => fixture.CommitComposition(() =>
        {
            Assert.AreEqual("updated project\n", File.ReadAllText(fixture.Project));
            recovery = fixture.TemporaryFiles().Single(path => File.ReadAllText(path) == CommitFixture.ProjectContent);
            if (kind == "human") File.WriteAllText(fixture.Project, "人工新项目\n");
            else if (kind == "deleted") File.Delete(fixture.Project);
            else if (kind == "invalid-utf8") File.WriteAllBytes(fixture.Project, [0xFF]);
            else if (kind == "link") fixture.Replace(fixture.Project, "file");
            else if (kind == "recovery-drift") File.WriteAllText(recovery, "人工恢复资料\n");
            else if (kind == "recovery-deleted") File.Delete(recovery);
            else if (kind == "recovery-dangling") fixture.Replace(recovery, "dangling");
            else fixture.Replace(recovery, "file");
            fixture.Replace(fixture.Catalog, "directory");
            outside = fixture.CaptureOutside();
            return Task.CompletedTask;
        }));
        CollectionAssert.AreEquivalent(outside, fixture.CaptureOutside());
        Assert.IsNotNull(recovery);
        if (kind is "recovery-deleted" or "recovery-dangling")
        {
            if (kind == "recovery-deleted") Assert.IsFalse(File.Exists(recovery));
            else
            {
                var target = new FileInfo(recovery).LinkTarget;
                Assert.IsNotNull(target);
                Assert.IsFalse(File.Exists(target));
            }
        }
        else
        {
            Assert.IsTrue(File.Exists(recovery));
            Assert.AreEqual(kind == "recovery-drift" ? "人工恢复资料\n" : CommitFixture.ProjectContent, File.ReadAllText(recovery));
        }
        if (kind == "human") Assert.AreEqual("人工新项目\n", File.ReadAllText(fixture.Project));
        else if (kind == "deleted") Assert.IsFalse(File.Exists(fixture.Project));
        else if (kind == "invalid-utf8") CollectionAssert.AreEqual(new byte[] { 0xFF }, File.ReadAllBytes(fixture.Project));
        else
        {
            Assert.AreEqual("updated project\n", File.ReadAllText(fixture.Project));
            if (kind == "link") Assert.IsNotNull(new FileInfo(fixture.Project).LinkTarget);
        }
        Assert.AreEqual(kind == "recovery-deleted" ? 0 : 1, fixture.TemporaryFiles().Length);
        var marker = Path.Combine(fixture.Repository, ".fullnet/codegeneration-composition-recovery.pending");
        Assert.IsTrue(File.Exists(marker));
        var registration = File.ReadAllText(marker);
        StringAssert.Contains(registration, Path.GetRelativePath(fixture.Repository, recovery).Replace(Path.DirectorySeparatorChar, '/'));
        StringAssert.Contains(registration, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CommitFixture.ProjectContent))).ToLowerInvariant());
        Assert.IsTrue(Directory.Exists(fixture.Catalog));
    }

    [TestMethod]
    public async Task Composition_commit_preserves_catalog_edited_after_project_move()
    {
        using var fixture = new CommitFixture();
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => fixture.CommitComposition(() =>
        {
            File.WriteAllText(fixture.Catalog, "人工 Catalog\n");
            return Task.CompletedTask;
        }));
        Assert.AreEqual(CommitFixture.ProjectContent, File.ReadAllText(fixture.Project));
        Assert.AreEqual("人工 Catalog\n", File.ReadAllText(fixture.Catalog));
        Assert.AreEqual(0, fixture.TemporaryFiles().Length);
    }


    [TestMethod]
    [DataRow("file")]
    [DataRow("alias")]
    [DataRow("directory")]
    [DataRow("link")]
    [DataRow("dangling")]
    [DataRow("invalid-utf8")]
    public async Task Composition_commit_blocks_existing_recovery_registration(string kind)
    {
        using var fixture = new CommitFixture();
        var marker = Path.Combine(fixture.Repository, ".fullnet/codegeneration-composition-recovery.pending");
        File.WriteAllText(marker, "待人工审查");
        if (kind is "alias" or "directory" or "link" or "dangling") fixture.Replace(marker, kind == "link" ? "file" : kind);
        else if (kind == "invalid-utf8") File.WriteAllBytes(marker, [0xFF]);
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => fixture.CommitComposition());
        Assert.AreEqual(CommitFixture.ProjectContent, File.ReadAllText(fixture.Project));
        Assert.AreEqual(CommitFixture.CatalogContent, File.ReadAllText(fixture.Catalog));
        Assert.AreEqual(0, fixture.TemporaryFiles().Length);
    }

    [TestMethod]
    [DataRow("project", "file")]
    [DataRow("project", "alias")]
    [DataRow("project", "directory")]
    [DataRow("project", "link")]
    [DataRow("project", "dangling")]
    [DataRow("project", "invalid-utf8")]
    [DataRow("catalog", "file")]
    [DataRow("catalog", "alias")]
    [DataRow("catalog", "directory")]
    [DataRow("catalog", "link")]
    [DataRow("catalog", "dangling")]
    [DataRow("catalog", "invalid-utf8")]
    public async Task Composition_commit_blocks_orphan_recovery_material_without_registration(string target, string kind)
    {
        using var fixture = new CommitFixture(separateCatalog: target == "catalog");
        var orphan = Path.Combine(Path.GetDirectoryName(fixture.PathFor(target))!, ".fullnet-composition-orphan.tmp");
        File.WriteAllText(orphan, "待人工审查");
        if (kind is "alias" or "directory" or "link" or "dangling") fixture.Replace(orphan, kind == "link" ? "file" : kind);
        else if (kind == "invalid-utf8") File.WriteAllBytes(orphan, [0xFF]);
        var outside = fixture.CaptureOutside();
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => fixture.CommitComposition());
        CollectionAssert.AreEquivalent(outside, fixture.CaptureOutside());
        Assert.AreEqual(CommitFixture.ProjectContent, File.ReadAllText(fixture.Project));
        Assert.AreEqual(CommitFixture.CatalogContent, File.ReadAllText(fixture.Catalog));
    }

    [TestMethod]
    public async Task Composition_registration_failure_preserves_material_and_reports_all_failures()
    {
        using var fixture = new CommitFixture();
        var failure = await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => fixture.CommitComposition(() =>
        {
            File.WriteAllText(fixture.Project, "人工新项目\n");
            fixture.Replace(fixture.Catalog, "directory");
            Directory.CreateDirectory(Path.Combine(fixture.Repository, ".fullnet/codegeneration-composition-recovery.pending"));
            return Task.CompletedTask;
        }));
        StringAssert.Contains(failure.Message, "登记失败");
        Assert.IsInstanceOfType<AggregateException>(failure.InnerException);
        Assert.AreEqual(3, ((AggregateException)failure.InnerException!).InnerExceptions.Count);
        Assert.AreEqual("人工新项目\n", File.ReadAllText(fixture.Project));
        Assert.IsTrue(fixture.TemporaryFiles().Any(path => File.ReadAllText(path) == CommitFixture.ProjectContent));
    }


    [TestMethod]
    public async Task Composition_registration_failure_with_deleted_backup_preserves_candidate_and_blocks_retry()
    {
        using var fixture = new CommitFixture();
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => fixture.CommitComposition(() =>
        {
            var backup = fixture.TemporaryFiles().Single(path => File.ReadAllText(path) == CommitFixture.ProjectContent);
            File.Delete(backup);
            fixture.Replace(fixture.Catalog, "directory");
            return Task.CompletedTask;
        }, () => throw new IOException("注入登记文件创建前的故障")));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Repository, ".fullnet/codegeneration-composition-recovery.pending")));
        Assert.AreEqual("updated catalog\n", File.ReadAllText(fixture.TemporaryFiles().Single()));
        Directory.Delete(fixture.Catalog);
        File.WriteAllText(fixture.Catalog, CommitFixture.CatalogContent);
        var retry = await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => fixture.CommitComposition());
        StringAssert.Contains(retry.Message, "待审查");
        Assert.AreEqual("updated project\n", File.ReadAllText(fixture.Project));
        Assert.AreEqual(CommitFixture.CatalogContent, File.ReadAllText(fixture.Catalog));
    }


    private sealed class CommitFixture : IDisposable
    {
        public const string EntryContent = "original entry\n";
        public const string ProjectContent = "original project\n";
        public const string CatalogContent = "original catalog\n";
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"fullnet-integration-commit-{Guid.NewGuid():N}");
        private readonly List<string> _directoryLinks = [];

        public CommitFixture(bool separateCatalog = false)
        {
            Repository = Path.Combine(_root, "repository");
            Outside = Path.Combine(_root, "outside");
            Module = Path.Combine(Repository, "src/Modules/Acme.Modules.Catalog");
            Entry = Path.Combine(Module, "CatalogModule.cs");
            Project = Path.Combine(Repository, "src/Composition/Acme.Composition/Acme.Composition.csproj");
            Catalog = Path.Combine(separateCatalog ? Path.Combine(Repository, "catalog") : Path.GetDirectoryName(Project)!, "ModuleCatalog.cs");
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

        public Task CommitAuthorization(Func<Task>? afterStaging = null) =>
            ModuleIntegrationHostOrchestrator.CommitAuthorizationContributorAsync(Repository,
                Path.GetRelativePath(Repository, Entry).Replace(Path.DirectorySeparatorChar, '/'),
                EntryContent, "updated authorization\n", CancellationToken.None, afterStaging);

        public Task CommitComposition(Func<Task>? afterProjectCommit = null, Func<Task>? beforeRecoveryRegistration = null) => CompositionIntegrationApplyCommand.CommitAsync(
            Repository, Module, Entry, EntryContent, Project, ProjectContent,
            CompositionIntegrationEditResult.Success(ProjectContent, "updated project\n"),
            Catalog, CatalogContent, CompositionIntegrationEditResult.Success(CatalogContent, "updated catalog\n"),
            CancellationToken.None, afterProjectCommit, beforeRecoveryRegistration);

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
