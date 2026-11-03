using System.Reflection;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Full.NET.ArchitectureTests;

/// <summary>K02 的 SQL 名称、租户元数据及对象必须封闭在静态声明中；字符串格式映射不能创建运行时语句。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionSqlDeclarationTests
{
    [TestMethod]
    [DataRow("AiKnowledgeCorrectionCleanupSql")]
    [DataRow("AiKnowledgeCorrectionSql")]
    [DataRow("AiKnowledgeCorrectionPreparationSql")]
    [DataRow("AiKnowledgeCorrectionActivationSql")]
    [DataRow("AiKnowledgeCorrectionVectorSql")]
    [DataRow("AiKnowledgeCorrectionManifestSql")]
    [DataRow("AiKnowledgeCleanupWorkerSql")]
    [DataRow("AiKnowledgeIndexCorrectionSql")]
    public void Correction_sql_objects_are_constructed_only_in_static_declarations(string name)
    {
        var type = typeof(AiModule).Assembly.GetType("Full.NET.Modules.Ai.Persistence." + name, throwOnError: true)!;
        var statements = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == typeof(SqlStatement)).Select(field => (Field: field.Name, Value: (SqlStatement)field.GetValue(null)!))
            .OrderBy(item => item.Field, StringComparer.Ordinal).ToArray();
        Assert.IsNotEmpty(statements);
        // 可选导出只记录无参数值的静态契约，供机械迁移前后核对；不改变标准执行的架构断言。
        var export = Environment.GetEnvironmentVariable("FULLNET_AI_SQL_DECLARATION_EXPORT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(export))
        {
            Directory.CreateDirectory(export);
            File.WriteAllText(Path.Combine(export, name + ".json"), JsonSerializer.Serialize(statements.Select(item => new
            { item.Field, item.Value.Name, item.Value.Text, Scope = item.Value.Scope.ToString(), TenantBinding = item.Value.TenantBinding.ToString() })));
        }
        Assert.HasCount(0, SqlStatementConstructionScanner.FindViolations([type]));
    }
}
