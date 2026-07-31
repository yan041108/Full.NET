using Full.NET.Data.Abstractions;
using Full.NET.Modules.CodeGeneration.Persistence;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CodeGenerationTemplateSqlTests
{
    [TestMethod]
    public void Statements_are_host_only_parameterized_and_hide_soft_deleted_rows()
    {
        var statements = new[]
        {
            CodeGenerationTemplateSql.PageSqlServer,
            CodeGenerationTemplateSql.PageMySql,
            CodeGenerationTemplateSql.FindById,
            CodeGenerationTemplateSql.Insert,
            CodeGenerationTemplateSql.Update,
            CodeGenerationTemplateSql.SoftDelete,
        };

        Assert.IsTrue(statements.All(
            statement => statement.Scope == SqlDataScope.HostOnly));
        Assert.IsTrue(statements.All(
            statement => !statement.Text.Contains(
                "SELECT *",
                StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(
            new[]
            {
                CodeGenerationTemplateSql.PageSqlServer,
                CodeGenerationTemplateSql.PageMySql,
                CodeGenerationTemplateSql.FindById,
                CodeGenerationTemplateSql.Update,
                CodeGenerationTemplateSql.SoftDelete,
            }.All(statement => statement.Text.Contains(
                "IsDeleted = 0",
                StringComparison.Ordinal)));
        StringAssert.Contains(
            CodeGenerationTemplateSql.FindById.Text,
            "Id = @Id");
        StringAssert.Contains(
            CodeGenerationTemplateSql.Update.Text,
            "Version = @Version");
        StringAssert.Contains(
            CodeGenerationTemplateSql.SoftDelete.Text,
            "Version = @Version");
    }

    [TestMethod]
    public void Page_statements_combine_count_and_rows_in_stable_order()
    {
        foreach (var statement in new[]
                 {
                     CodeGenerationTemplateSql.PageSqlServer,
                     CodeGenerationTemplateSql.PageMySql,
                 })
        {
            StringAssert.Contains(statement.Text, "SELECT COUNT(1)");
            StringAssert.Contains(
                statement.Text,
                "ORDER BY UpdatedAtUtc DESC, CreatedAtUtc DESC, Id");
            StringAssert.Contains(statement.Text, "@Offset");
            StringAssert.Contains(statement.Text, "@PageSize");
        }

        StringAssert.Contains(
            CodeGenerationTemplateSql.PageSqlServer.Text,
            "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");
        StringAssert.Contains(
            CodeGenerationTemplateSql.PageMySql.Text,
            "LIMIT @PageSize OFFSET @Offset");
    }
}
