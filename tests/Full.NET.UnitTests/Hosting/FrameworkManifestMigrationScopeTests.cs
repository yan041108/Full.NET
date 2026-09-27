using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Hosting;

/// <summary>只读验证迁移清单的失败关闭，不启动宿主、连接数据库或执行SQL。</summary>
[TestClass]
public sealed class FrameworkManifestMigrationScopeTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("{}")]
    [DataRow("{\"migrationInventory\":{\"selectionStatus\":\"unscoped\"}}")]
    public void Non_application_workspace_retains_legacy_unscoped_mode(string? manifest)
    {
        using var fixture = new ManifestFixture(manifest, standalone: false);
        Assert.IsNull(fixture.Load());
    }

    [TestMethod]
    [DataRow(false, "preset-recovery-through237")]
    [DataRow(true, "preset-minimal")]
    public void Explicit_scope_returns_only_declared_names_and_preserves_files(bool standalone, string status)
    {
        using var fixture = new ManifestFixture(
            JsonSerializer.Serialize(new { migrationInventory = new { selectionStatus = status,
                scripts = new[] { new { name = "001_First.sql" }, new { name = "002_Second.sql" } } } }),
            standalone);
        var before = fixture.ReadFiles();
        CollectionAssert.AreEquivalent(new[] { "001_First.sql", "002_Second.sql" }, fixture.Load()!.ToArray());
        AssertFilesUnchanged(before, fixture.ReadFiles());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("{}")]
    [DataRow("{\"migrationInventory\":{}}")]
    [DataRow("{\"migrationInventory\":{\"selectionStatus\":null}}")]
    [DataRow("{\"migrationInventory\":{\"selectionStatus\":\"\"}}")]
    [DataRow("{\"migrationInventory\":{\"selectionStatus\":\" \"}}")]
    [DataRow("{\"migrationInventory\":{\"selectionStatus\":\"unscoped\"}}")]
    [DataRow("{\"migrationInventory\":{\"selectionStatus\":\"preset-\",\"scripts\":[{\"name\":\"001_First.sql\"}]}}")]
    [DataRow("{\"migrationInventory\":{\"selectionStatus\":\"preset-minimal\"}}")]
    [DataRow("{\"migrationInventory\":{\"selectionStatus\":\"preset-minimal\",\"scripts\":[]}}")]
    [DataRow("{\"migrationInventory\":{\"selectionStatus\":\"preset-minimal\",\"scripts\":[{}, {\"name\":\"001_First.sql\"}]}}")]
    [DataRow("{\"migrationInventory\":{\"selectionStatus\":\"preset-minimal\",\"scripts\":[{\"name\":\" \"}, {\"name\":\"001_First.sql\"}]}}")]
    public void Standalone_application_cannot_fall_back_to_all_scripts_for_incomplete_scope(string? manifest)
    {
        using var fixture = new ManifestFixture(manifest);
        var before = fixture.ReadFiles();
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Load());
        AssertFilesUnchanged(before, fixture.ReadFiles());
    }

    [TestMethod]
    public void Manifest_directory_placeholder_cannot_be_treated_as_absent_legacy_manifest()
    {
        using var fixture = new ManifestFixture(null);
        Directory.CreateDirectory(Path.Combine(fixture.Root, "framework-manifest.json"));
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Load());
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Public_runner_rejects_missing_application_scope_before_parsing_connection(DatabaseProvider provider)
    {
        using var fixture = new ManifestFixture(null);
        using var logs = LoggerFactory.Create(_ => { });
        var runner = new DbUpMigrationRunner(Options.Create(new DatabaseOptions
            { Provider = provider, ConnectionString = "credential-probe" }), logs,
            Options.Create(new UuidBinaryContractOptions()), Options.Create(new PreV1NamingContractOptions()),
            Options.Create(fixture.Options));
        var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => runner.MigrateAsync());
        StringAssert.Contains(failure.Message, "framework-manifest");
        Assert.IsFalse(failure.Message.Contains("credential-probe", StringComparison.Ordinal));
    }

    private static void AssertFilesUnchanged(Dictionary<string, byte[]> before, Dictionary<string, byte[]> after)
    {
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (var path in before.Keys) CollectionAssert.AreEqual(before[path], after[path], path);
    }

    private sealed class ManifestFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "fullnet-migration-scope-" + Guid.NewGuid().ToString("N"));
        public FrameworkManifestMigrationOptions Options => new() { ContentRoot = Root };

        public ManifestFixture(string? manifest, bool standalone = true)
        {
            Directory.CreateDirectory(Root);
            if (standalone) File.WriteAllText(Path.Combine(Root, "fullnet-app.json"), """{"preset":"minimal"}""");
            if (manifest is not null) File.WriteAllText(Path.Combine(Root, "framework-manifest.json"), manifest);
        }

        public HashSet<string>? Load() => FrameworkManifestMigrationScope.TryLoadAllowedScriptNames(Options);
        public Dictionary<string, byte[]> ReadFiles() => Directory.EnumerateFiles(Root)
            .ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes, StringComparer.Ordinal);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
