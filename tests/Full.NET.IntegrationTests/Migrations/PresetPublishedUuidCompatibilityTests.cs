using System.Text.RegularExpressions;
using Full.NET.Migrations.DbUp;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证固定清单仅省略未选表，保留历史校验、恢复和摘要边界；这些用例不启动数据库。</summary>
[TestClass]
public sealed class PresetPublishedUuidCompatibilityTests
{
    [TestMethod]
    [DataRow("MySql")]
    [DataRow("SqlServer")]
    public void Scoped_processing_preserves_selected_table_validation_and_rejects_unselected_existing_tables(string provider)
    {
        var source = Read203(provider);
        var processed = new PresetPublishedUuidCompatibilityPreprocessor(
            new HashSet<string>(StringComparer.Ordinal) { "165_DocumentVersionDeletionAudit.sql" }).Process(source);
        if (provider == "MySql")
        {
            StringAssert.Contains(processed, "SELECT COUNT(*) FROM fn_document_version_deletion_audit");
            StringAssert.Contains(processed, "ALTER TABLE fn_document_version_deletion_audit MODIFY COLUMN Id");
            StringAssert.Contains(processed, "CREATE TEMPORARY TABLE fn_migrations_uuid_preflight");
            StringAssert.Contains(processed, "DROP TEMPORARY TABLE fn_migrations_uuid_preflight;");
            Assert.IsFalse(Regex.IsMatch(processed, @"(?:FROM|ALTER TABLE|UPDATE) fn_ai_model_config\b"));
            StringAssert.Contains(processed, "FROM information_schema.TABLES\nWHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_model_config'");
            StringAssert.Contains(processed, "CHECK (InvalidCount = 0)");
        }
        else
        {
            StringAssert.Contains(processed, "UUID storage invariant failed: fn_document_version_deletion_audit.Id");
            Assert.IsFalse(processed.Contains("UUID storage invariant failed: fn_ai_model_config.Id", StringComparison.Ordinal));
            StringAssert.Contains(processed, "Preset migration inventory excludes existing table: fn_ai_model_config");
            StringAssert.Contains(processed, "system_type_id = TYPE_ID(N'uniqueidentifier')");
        }
    }

    [TestMethod]
    [DataRow("MySql")]
    [DataRow("SqlServer")]
    public void Unscoped_and_complete_inventory_leave_historical_script_unchanged(string provider)
    {
        var source = Read203(provider);
        Assert.AreEqual(source, new PresetPublishedUuidCompatibilityPreprocessor(null).Process(source));
        var segment = $".Migrations.{provider}.";
        var selected = typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames()
            .Where(name => name.Contains(segment, StringComparison.Ordinal))
            .Select(name => name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..])
            .ToHashSet(StringComparer.Ordinal);
        Assert.AreEqual(source.ReplaceLineEndings("\n"),
            new PresetPublishedUuidCompatibilityPreprocessor(selected).Process(source));
    }

    [TestMethod]
    [DataRow("MySql")]
    [DataRow("SqlServer")]
    public void Different_script_digest_is_never_transformed(string provider)
    {
        var changed = Read203(provider) + "\n-- 改变历史全文后不得复用兼容变换\n";
        Assert.AreEqual(changed, new PresetPublishedUuidCompatibilityPreprocessor(
            new HashSet<string>(StringComparer.Ordinal)).Process(changed));
    }

    private static string Read203(string provider)
    {
        var assembly = typeof(DbUpMigrationRunner).Assembly;
        var name = assembly.GetManifestResourceNames().Single(value =>
            value.Contains($".Migrations.{provider}.", StringComparison.Ordinal)
            && value.EndsWith("203_PublishedModuleUuidStorage.sql", StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }
}
