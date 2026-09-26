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
        var processed = new PresetPublishedModuleCompatibilityPreprocessor(
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
        Assert.AreEqual(source, new PresetPublishedModuleCompatibilityPreprocessor(null).Process(source));
        var segment = $".Migrations.{provider}.";
        var selected = typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames()
            .Where(name => name.Contains(segment, StringComparison.Ordinal))
            .Select(name => name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..])
            .ToHashSet(StringComparer.Ordinal);
        Assert.AreEqual(source,
            new PresetPublishedModuleCompatibilityPreprocessor(selected).Process(source));
    }

    [TestMethod]
    [DataRow("MySql")]
    [DataRow("SqlServer")]
    public void Different_script_digest_is_never_transformed(string provider)
    {
        var changed = Read203(provider) + "\n-- 改变历史全文后不得复用兼容变换\n";
        Assert.AreEqual(changed, new PresetPublishedModuleCompatibilityPreprocessor(
            new HashSet<string>(StringComparer.Ordinal)).Process(changed));
    }

    [TestMethod]
    [DataRow("MySql", "206_ExternalSideEffectUnknownState.sql", "fn_payment_order", "190_PaymentMerchantConfig.sql", "fn_ocr_id_card_task")]
    [DataRow("SqlServer", "206_ExternalSideEffectUnknownState.sql", "fn_payment_order", "190_PaymentMerchantConfig.sql", "fn_ocr_id_card_task")]
    [DataRow("MySql", "207_TaskExecutionLease.sql", "fn_import_export_task", "171_ImportExportTask.sql", "fn_reporting_export_task")]
    [DataRow("SqlServer", "207_TaskExecutionLease.sql", "fn_import_export_task", "171_ImportExportTask.sql", "fn_reporting_export_task")]
    public void Shared_module_scripts_preserve_selected_ddl_and_gate_excluded_tables(
        string provider, string script, string selectedTable, string origin, string excludedTable)
    {
        var source = ReadScript(provider, script);
        foreach (var includeSelected in new[] { false, true })
        {
            var selected = new HashSet<string>(StringComparer.Ordinal);
            if (includeSelected) selected.Add(origin);
            var processed = new PresetPublishedModuleCompatibilityPreprocessor(selected).Process(source);
            var ddlPrefix = provider == "MySql" ? "ALTER TABLE " : "ALTER TABLE dbo.";
            Assert.AreEqual(includeSelected, processed.Contains(ddlPrefix + selectedTable, StringComparison.Ordinal));
            Assert.IsFalse(processed.Contains(ddlPrefix + excludedTable, StringComparison.Ordinal));
            if (provider == "MySql")
            {
                StringAssert.Contains(processed, "CREATE TEMPORARY TABLE fn_migrations_preset_scope_guard");
                StringAssert.Contains(processed, "CHECK (InvalidCount = 0)");
                StringAssert.Contains(processed, $"TABLE_NAME = '{excludedTable}'");
                StringAssert.Contains(processed, "DROP TEMPORARY TABLE fn_migrations_preset_scope_guard;");
            }
            else
                StringAssert.Contains(processed, $"Preset migration inventory excludes existing table: {excludedTable}");
        }
    }

    [TestMethod]
    [DataRow("MySql", "206_ExternalSideEffectUnknownState.sql")]
    [DataRow("SqlServer", "206_ExternalSideEffectUnknownState.sql")]
    [DataRow("MySql", "207_TaskExecutionLease.sql")]
    [DataRow("SqlServer", "207_TaskExecutionLease.sql")]
    public void Shared_module_scripts_leave_unscoped_and_complete_inventory_unchanged(string provider, string script)
    {
        var source = ReadScript(provider, script);
        Assert.AreEqual(source, new PresetPublishedModuleCompatibilityPreprocessor(null).Process(source));
        var segment = $".Migrations.{provider}.";
        var selected = typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames()
            .Where(name => name.Contains(segment, StringComparison.Ordinal))
            .Select(name => name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..])
            .ToHashSet(StringComparer.Ordinal);
        Assert.AreEqual(source, new PresetPublishedModuleCompatibilityPreprocessor(selected).Process(source));
    }

    [TestMethod]
    [DataRow("MySql", "206_ExternalSideEffectUnknownState.sql")]
    [DataRow("SqlServer", "206_ExternalSideEffectUnknownState.sql")]
    [DataRow("MySql", "207_TaskExecutionLease.sql")]
    [DataRow("SqlServer", "207_TaskExecutionLease.sql")]
    public void Shared_module_scripts_with_changed_digest_are_not_transformed(string provider, string script)
    {
        var changed = ReadScript(provider, script) + "\n-- 摘要边界\n";
        Assert.AreEqual(changed, new PresetPublishedModuleCompatibilityPreprocessor(new HashSet<string>(StringComparer.Ordinal)).Process(changed));
    }

    private static string Read203(string provider) => ReadScript(provider, "203_PublishedModuleUuidStorage.sql");

    private static string ReadScript(string provider, string script)
    {
        var assembly = typeof(DbUpMigrationRunner).Assembly;
        var name = assembly.GetManifestResourceNames().Single(value =>
            value.Contains($".Migrations.{provider}.", StringComparison.Ordinal)
            && value.EndsWith(script, StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }
}
