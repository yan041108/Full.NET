using Full.NET.Data.Abstractions;

namespace Full.NET.UnitTests.Data;

/// <summary>就绪探针与 Migrator 使用同一套完整或预设迁移要求。</summary>
[TestClass]
public sealed class DatabaseSchemaRequirementsTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public void Latest_release_migration_is_required_and_preset_is_respected(DatabaseProvider provider)
    {
        var root = Path.Combine(Path.GetTempPath(), "fullnet-schema-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = new FrameworkManifestMigrationOptions { ContentRoot = root };
            var all = DatabaseSchemaRequirements.Read(provider, options);
            Assert.IsTrue(all.Any(name => name.EndsWith("322_PaymentOrderIdempotency.sql", StringComparison.Ordinal)));
            File.WriteAllText(Path.Combine(root, "framework-manifest.json"),
                """{"migrationInventory":{"selectionStatus":"preset-test","scripts":[{"name":"322_PaymentOrderIdempotency.sql"}]}}""");
            var subset = DatabaseSchemaRequirements.Read(provider, options);
            Assert.AreEqual(1, subset.Count);
            Assert.IsTrue(all.Contains(subset.Single()));
            File.WriteAllText(Path.Combine(root, "framework-manifest.json"),
                """{"migrationInventory":{"selectionStatus":"preset-test","scripts":[{"name":"999_Missing.sql"}]}}""");
            Assert.ThrowsExactly<InvalidOperationException>(() => DatabaseSchemaRequirements.Read(provider, options));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
