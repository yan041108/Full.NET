using Full.NET.Migrations.DbUp;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>冻结历史 093 的延迟编译边界；真正的数据保留和恢复由双库恢复用例验证。</summary>
[TestClass]
public sealed class SqlServerPublishedMigrationCompatibilityTests
{
    [TestMethod]
    public void Published_093_defers_both_legacy_password_backfills_until_after_add_column()
    {
        var source = ReadScript("SqlServer");
        var processed = new SqlServerPublishedMigrationCompatibilityPreprocessor().Process(source);
        var expected = source.ReplaceLineEndings("\n")
            .Replace("UPDATE dbo.fn_document_share\n            SET PasswordHash = Password\n            WHERE PasswordHash IS NULL;",
                "EXEC sys.sp_executesql N'UPDATE dbo.fn_document_share SET PasswordHash = Password WHERE PasswordHash IS NULL;';", StringComparison.Ordinal)
            .Replace("UPDATE dbo.fn_document_share\n            SET PasswordHash = COALESCE(PasswordHash, Password)\n            WHERE PasswordHash IS NULL;",
                "EXEC sys.sp_executesql N'UPDATE dbo.fn_document_share SET PasswordHash = COALESCE(PasswordHash, Password) WHERE PasswordHash IS NULL;';", StringComparison.Ordinal);

        Assert.IsFalse(source.ReplaceLineEndings("\n").Equals(expected, StringComparison.Ordinal));
        Assert.IsTrue(expected.Equals(processed.ReplaceLineEndings("\n"), StringComparison.Ordinal),
            "兼容变换必须仅修改两条回填语句，其余历史正文保持不变。");
        StringAssert.Contains(processed, "DROP COLUMN IF EXISTS Password;");
        StringAssert.Contains(processed, "ADD PasswordHash nvarchar(1024) NULL;");
    }

    [TestMethod]
    public void Changed_historical_digest_is_never_transformed()
    {
        var source = ReadScript("SqlServer") + "\n-- 摘要改变不能沿用历史兼容边界\n";
        Assert.AreEqual(source, new SqlServerPublishedMigrationCompatibilityPreprocessor().Process(source));
    }

    [TestMethod]
    public void MySql_script_remains_byte_for_byte_unchanged()
    {
        var source = ReadScript("MySql");
        Assert.AreEqual(source, new SqlServerPublishedMigrationCompatibilityPreprocessor().Process(source));
    }

    [TestMethod]
    public void Already_processed_script_is_unchanged_on_second_pass()
    {
        var processor = new SqlServerPublishedMigrationCompatibilityPreprocessor();
        var processed = processor.Process(ReadScript("SqlServer"));
        Assert.AreEqual(processed, processor.Process(processed));
    }

    private static string ReadScript(string provider)
    {
        var assembly = typeof(DbUpMigrationRunner).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(name =>
            name.EndsWith($".Migrations.{provider}.093_DocumentAdminNetParity.sql", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
