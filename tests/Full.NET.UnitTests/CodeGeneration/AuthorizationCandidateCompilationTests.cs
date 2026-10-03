using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Integration;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class AuthorizationCandidateCompilationTests
{
    [TestMethod]
    public async Task Default_compiler_rejects_missing_module_project_before_authorization_commit()
    {
        using var fixture = new Fixture();
        var result = await fixture.ApplyDefault();
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join("\n", result.Diagnostics), "模块项目不存在");
        Assert.AreEqual(Fixture.Source, File.ReadAllText(fixture.Path));
        Assert.AreEqual(0, fixture.TemporaryFiles().Length);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Failed_candidate_compilation_preserves_original_contributor(bool emptyDiagnostics)
    {
        using var fixture = new Fixture();
        var called = false;
        var result = await fixture.Apply((path, content, token) =>
        {
            called = true;
            Assert.AreEqual(fixture.Path, path);
            StringAssert.Contains(content, "<fullnet-generated catalog.product permissions>");
            Assert.AreEqual(Fixture.Source, File.ReadAllText(path));
            return Task.FromResult(ModuleIntegrationCompilationResult.Failure(emptyDiagnostics ? [] : ["CS0103 candidate failed"]));
        });
        Assert.IsTrue(called);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join("\n", result.Diagnostics), emptyDiagnostics ? "接入失败" : "CS0103");
        Assert.AreEqual(Fixture.Source, File.ReadAllText(fixture.Path));
        Assert.AreEqual(0, fixture.TemporaryFiles().Length);
    }

    [TestMethod]
    public async Task Cancelled_candidate_compilation_does_not_commit()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Apply((path, content, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(ModuleIntegrationCompilationResult.Success());
        }, cancellation.Token));
        Assert.AreEqual(Fixture.Source, File.ReadAllText(fixture.Path));
        Assert.AreEqual(0, fixture.TemporaryFiles().Length);
    }

    [TestMethod]
    public async Task Successful_candidate_compilation_commits_and_repeat_is_unchanged()
    {
        using var fixture = new Fixture();
        var calls = 0;
        Task<ModuleIntegrationCompilationResult> Compile(string path, string content, CancellationToken token)
        {
            calls++;
            Assert.AreEqual(Fixture.Source, File.ReadAllText(path));
            StringAssert.Contains(content, "catalog.manual");
            StringAssert.Contains(content, "\"手写\", AuthorizationScope.Tenant),");
            return Task.FromResult(ModuleIntegrationCompilationResult.Success());
        }
        var first = await fixture.Apply(Compile);
        Assert.IsTrue(first.Succeeded, string.Join("\n", first.Diagnostics));
        Assert.AreEqual(1, calls);
        var committed = File.ReadAllText(fixture.Path);
        StringAssert.Contains(committed, "<fullnet-generated catalog.product permissions>");
        var repeat = await fixture.Apply(Compile);
        Assert.IsTrue(repeat.Succeeded, string.Join("\n", repeat.Diagnostics));
        Assert.AreEqual(1, calls);
        Assert.AreEqual(committed, File.ReadAllText(fixture.Path));
        Assert.AreEqual(0, fixture.TemporaryFiles().Length);
    }

    [TestMethod]
    public async Task Contributor_changed_during_compilation_is_not_overwritten()
    {
        using var fixture = new Fixture();
        var human = Fixture.Source + "\n// 人工修改\n";
        var result = await fixture.Apply((path, content, token) =>
        {
            File.WriteAllText(path, human);
            return Task.FromResult(ModuleIntegrationCompilationResult.Success());
        });
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(human, File.ReadAllText(fixture.Path));
        Assert.AreEqual(0, fixture.TemporaryFiles().Length);
    }

    private sealed class Fixture : IDisposable
    {
        public const string Source = """
            public sealed class CatalogAuthorizationContributor
            {
                public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
                [new PermissionDefinition("catalog.manual", "手写", AuthorizationScope.Tenant)];
                public IReadOnlyCollection<NavigationDefinition> Navigation { get; } = [];
                public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } = [];
            }
            """;
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fullnet-authorization-candidate-{Guid.NewGuid():N}");
        private readonly ModuleIntegrationTarget _target;
        public Fixture()
        {
            const string relative = "src/Acme.Modules.Catalog/CatalogAuthorizationContributor.cs";
            Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(_root, relative));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, Source);
            _target = ModuleIntegrationTarget.Create("Catalog", "src/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj",
                "src/Acme.Modules.Catalog/CatalogModule.cs", "src/Composition/Acme.Composition.csproj",
                "src/Composition/ModuleCatalog.cs", "ui/admin/routes.ts", null, authorizationContributorPath: relative);
        }
        public string Path { get; }
        public Task<ModuleIntegrationHostApplyResult> ApplyDefault() => ModuleIntegrationHostOrchestrator.ApplyAuthorizationContributorAsync(
            _root, FullNetCrudSchemaTests.CreateProductSchema(), _target, CancellationToken.None);
        public Task<ModuleIntegrationHostApplyResult> Apply(
            Func<string, string, CancellationToken, Task<ModuleIntegrationCompilationResult>> compile,
            CancellationToken token = default) => ModuleIntegrationHostOrchestrator.ApplyAuthorizationContributorAsync(
                _root, FullNetCrudSchemaTests.CreateProductSchema(), _target, token, compile);
        public string[] TemporaryFiles() => Directory.GetFiles(_root, ".fullnet-authorization-*.tmp", SearchOption.AllDirectories);
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
