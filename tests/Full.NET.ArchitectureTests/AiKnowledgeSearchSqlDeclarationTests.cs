using System.Reflection;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Full.NET.ArchitectureTests;

/// <summary>新搜索路径沿用静态 SQL 构造与可信租户绑定，不为运行时筛选创建语句或放宽作用域。</summary>
[TestClass]
public sealed class AiKnowledgeSearchSqlDeclarationTests
{
    [TestMethod]
    public void Search_statements_remain_static_read_only_and_explicitly_tenant_bound()
    {
        var type = typeof(AiModule).Assembly.GetType("Full.NET.Modules.Ai.Persistence.AiKnowledgeRetrievalSql", true)!;
        Assert.HasCount(0, SqlStatementConstructionScanner.FindViolations([type]));
        var statements = type.GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(SqlStatement)).Select(field => (SqlStatement)field.GetValue(null)!)
            .Where(statement => statement.Name.StartsWith("ai.knowledge_search.", StringComparison.Ordinal)).ToArray();
        Assert.IsNotEmpty(statements);
        foreach (var statement in statements)
        {
            StringAssert.StartsWith(statement.Text, "SELECT ");
            if (statement.Name.EndsWith("_tenant", StringComparison.Ordinal))
            {
                Assert.AreEqual(SqlDataScope.TenantRequired, statement.Scope);
                Assert.AreEqual(SqlTenantBinding.CurrentTenantId, statement.TenantBinding);
                StringAssert.Contains(statement.Text, "knowledge.TenantId=@TenantId");
            }
            else { Assert.AreEqual(SqlDataScope.HostOnly, statement.Scope); StringAssert.Contains(statement.Text, "knowledge.TenantId IS NULL"); }
        }
    }
}
