using DbUp.Engine;
using Full.NET.Migrations.DbUp;

namespace Full.NET.UnitTests.Data;

/// <summary>已发布 UUID 声明只在精确内容白名单内兼容，原始嵌入资源保持不变。</summary>
[TestClass]
public sealed class MySqlPublishedUuidCompatibilityTests
{
    /// <summary>新建库执行已发布模块表时使用 Binary16，避免外键类型不一致。</summary>
    [TestMethod]
    public void Published_document_script_uses_binary_uuid_at_execution()
    {
        var source = ReadScript("167_DocumentAccessLog.sql");
        Assert.Contains("char(36)", source);
        var processed = Preprocessor().Process(source);
        Assert.DoesNotContain("char(36)", processed);
        Assert.Contains("DocumentItemId BINARY(16)", processed);
        Assert.AreEqual(source, ReadScript("167_DocumentAccessLog.sql"));
    }

    /// <summary>同一脚本的 Windows/Linux 换行不影响内容白名单。</summary>
    [TestMethod]
    public void Line_endings_do_not_change_allowlist_match()
    {
        var source = ReadScript("173_ImportExportTaskExecution.sql");
        Assert.DoesNotContain("char(36)", Preprocessor().Process(source.ReplaceLineEndings("\r\n")));
    }

    /// <summary>新脚本或意外内容变动不得被旧迁移兼容逻辑静默改写。</summary>
    [TestMethod]
    public void Unregistered_content_is_unchanged()
    {
        var source = ReadScript("167_DocumentAccessLog.sql") + "\n-- changed migration";
        Assert.AreEqual(source, Preprocessor().Process(source));
    }

    /// <summary>全部已登记资源均移除 UUID 字符排序规则，保留其他文本列的定义。</summary>
    [TestMethod]
    public void All_published_uuid_scripts_have_valid_binary_declarations()
    {
        var assembly = typeof(DbUpMigrationRunner).Assembly;
        var count = 0;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.Contains(".MySql.", StringComparison.Ordinal)))
        {
            var file = name[(name.IndexOf(".MySql.", StringComparison.Ordinal) + 7)..];
            if (!int.TryParse(file[..3], out var number) || number < 165 || number > 199) continue;
            var source = ReadScript(file);
            if (!source.Contains("char(36)", StringComparison.Ordinal)) continue;
            var processed = Preprocessor().Process(source);
            Assert.DoesNotContain("char(36)", processed);
            Assert.DoesNotContain("BINARY(16) COLLATE", processed);
            Assert.DoesNotContain("BINARY(16) CHARACTER", processed);
            count++;
        }
        Assert.AreEqual(15, count);
    }

    /// <summary>通过公开 DbUp 合同调用内部预处理器，不扩大生产类型可见性。</summary>
    private static IScriptPreprocessor Preprocessor() => (IScriptPreprocessor)Activator.CreateInstance(
        typeof(DbUpMigrationRunner).Assembly.GetType("Full.NET.Migrations.DbUp.MySqlPublishedMigrationCompatibilityPreprocessor", true)!, true)!;

    /// <summary>读取交付程序集内的原始迁移资源。</summary>
    /// <param name="suffix">脚本文件名。</param>
    private static string ReadScript(string suffix)
    {
        var assembly = typeof(DbUpMigrationRunner).Assembly;
        var name = assembly.GetManifestResourceNames().Single(name => name.Contains(".MySql.", StringComparison.Ordinal)
            && name.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
