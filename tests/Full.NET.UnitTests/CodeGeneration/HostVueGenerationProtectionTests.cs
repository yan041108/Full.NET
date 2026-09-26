using System.Reflection;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Integration;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class HostVueGenerationProtectionTests
{
    private string _root = null!;
    private static readonly ModuleClientRouteTarget Route = ModuleClientRouteTarget.Create(
        "/catalog/products", "catalog-products", "ui/admin/src/views/ProductsView.vue");

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "fullnet-vue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task Unowned_human_file_rejects_entire_vue_batch(int index)
    {
        var path = Paths()[index];
        Directory.CreateDirectory(Path.GetDirectoryName(FullPath(path))!);
        await File.WriteAllTextAsync(FullPath(path), "人工实现，不允许覆盖\n");
        var before = Snapshot();
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => WriteAsync());
        AssertSnapshot(before);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task Modified_owned_file_rejects_entire_vue_batch(int index)
    {
        await WriteAsync();
        await File.AppendAllTextAsync(FullPath(Paths()[index]), "// 人工改动\n");
        var before = Snapshot();
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => WriteAsync());
        AssertSnapshot(before);
    }

    [TestMethod]
    public async Task First_write_records_ownership_and_repeat_preserves_bytes()
    {
        await WriteAsync();
        Assert.IsTrue(File.Exists(FullPath(GenerationWorkspaceStore.ManifestRelativePath)));
        Assert.AreEqual(3, GenerationManifest.Parse(await File.ReadAllTextAsync(
            FullPath(GenerationWorkspaceStore.ManifestRelativePath))).Artifacts.Count);
        var before = Snapshot();
        await WriteAsync();
        AssertSnapshot(before);
    }

    [TestMethod]
    public async Task Other_owned_artifacts_are_preserved_when_vue_is_added()
    {
        var artifact = new GeneratedArtifact("other/owned.txt", GeneratedArtifactKind.Report, "已有产物\n");
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(_root, [artifact], CancellationToken.None);
        await GenerationWorkspaceStore.ApplyAsync(_root,
            GenerationWritePlanner.Plan([artifact], snapshot.ExistingFiles), CancellationToken.None);
        await WriteAsync();
        Assert.AreEqual(artifact.Content, await File.ReadAllTextAsync(FullPath(artifact.RelativePath)));
        Assert.AreEqual(4, GenerationManifest.Parse(await File.ReadAllTextAsync(
            FullPath(GenerationWorkspaceStore.ManifestRelativePath))).Artifacts.Count);
    }

    [TestMethod]
    public async Task Schema_change_updates_unchanged_owned_files()
    {
        await WriteAsync();
        var before = Snapshot();
        var schema = FullNetCrudSchemaTests.CreateProductSchema();
        var changed = FullNetCrudSchemaTests.CreateProductSchema(columns: [.. schema.Columns,
            new FullNetColumn("Extra", "Extra", "extra", FullNetScalarType.String, IsNullable: true, MaxLength: 100)]);
        await WriteAsync(schema: changed);
        Assert.IsTrue(Paths().Any(path => !before[Path.GetRelativePath(_root, FullPath(path))]
            .SequenceEqual(File.ReadAllBytes(FullPath(path)))));
        var after = Snapshot();
        await WriteAsync(schema: changed);
        AssertSnapshot(after);
    }

    [TestMethod]
    public async Task Another_view_keeps_previous_view_and_ownership()
    {
        await WriteAsync();
        var original = await File.ReadAllBytesAsync(FullPath(Route.VueComponentPath));
        await WriteAsync(route: ModuleClientRouteTarget.Create("/catalog/other", "catalog-other",
            "ui/admin/src/views/OtherView.vue"));
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(FullPath(Route.VueComponentPath)));
        Assert.AreEqual(4, GenerationManifest.Parse(await File.ReadAllTextAsync(
            FullPath(GenerationWorkspaceStore.ManifestRelativePath))).Artifacts.Count);
    }

    [TestMethod]
    public async Task Unrelated_owned_drift_does_not_get_adopted_or_deleted()
    {
        await Other_owned_artifacts_are_preserved_when_vue_is_added();
        await File.AppendAllTextAsync(FullPath("other/owned.txt"), "人工修改\n");
        var before = Snapshot();
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => WriteAsync());
        AssertSnapshot(before);
    }

    [TestMethod]
    public async Task Cancellation_before_write_leaves_no_files()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => WriteAsync(source.Token));
        Assert.AreEqual(0, Snapshot().Count);
    }

    private string[] Paths()
    {
        var artifacts = CrudArtifactGenerator.Generate(FullNetCrudSchemaTests.CreateProductSchema());
        return [Route.VueComponentPath, .. artifacts.Where(artifact => artifact.Kind == GeneratedArtifactKind.VueClient
            && artifact.RelativePath.EndsWith(".generated.ts", StringComparison.Ordinal))
            .Select(artifact => "ui/admin/src/views/" + Path.GetFileName(artifact.RelativePath))];
    }

    private Task WriteAsync(CancellationToken token = default, FullNetCrudSchema? schema = null,
        ModuleClientRouteTarget? route = null)
    {
        // 通过现有私有入口复现 Host 的实际写盘行为，不为测试增加公共契约。
        var method = typeof(ModuleIntegrationHostOrchestrator).GetMethod("WriteVueViewAsync",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Task)method.Invoke(null, [_root, schema ?? FullNetCrudSchemaTests.CreateProductSchema(), route ?? Route, token])!;
    }

    private string FullPath(string relative) => Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
    private Dictionary<string, byte[]> Snapshot() => !Directory.Exists(_root) ? [] :
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(_root, path), File.ReadAllBytes);
    private void AssertSnapshot(Dictionary<string, byte[]> before)
    {
        var after = Snapshot();
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (var pair in before) CollectionAssert.AreEqual(pair.Value, after[pair.Key], pair.Key);
    }
}
