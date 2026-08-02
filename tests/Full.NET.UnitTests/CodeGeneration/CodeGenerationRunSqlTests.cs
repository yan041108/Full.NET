using Full.NET.Data.Abstractions;
using Full.NET.Modules.CodeGeneration.Persistence;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CodeGenerationRunSqlTests
{
    private static IReadOnlyList<SqlStatement> AllStatements() =>
        typeof(CodeGenerationRunSql)
            .GetFields(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static)
            .Where(field => field.FieldType == typeof(SqlStatement))
            .Select(field => (SqlStatement)field.GetValue(null)!)
            .ToArray();

    [TestMethod]
    public void Statements_are_host_only_and_never_persist_generated_content()
    {
        var statements = AllStatements();

        Assert.HasCount(15, statements);
        Assert.IsTrue(statements.All(
            statement => statement.Scope == SqlDataScope.HostOnly));
        Assert.IsTrue(statements.All(
            statement => !statement.Text.Contains(
                "SELECT *",
                StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(statements.All(
            statement => !statement.Text.Contains(
                "SchemaJson",
                StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(statements.All(
            statement => !statement.Text.Contains(
                "Content",
                StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(statements.All(
            statement => !statement.Text.Contains(
                "ErrorMessage",
                StringComparison.OrdinalIgnoreCase)));
        StringAssert.Contains(
            CodeGenerationRunSql.Insert.Text,
            "SchemaSha256");
        StringAssert.Contains(
            CodeGenerationRunSql.Insert.Text,
            "ManifestSha256");
    }

    [TestMethod]
    public void Page_statements_combine_count_and_rows_in_stable_order()
    {
        foreach (var statement in new[]
                 {
                     CodeGenerationRunSql.PageSqlServer,
                     CodeGenerationRunSql.PageMySql,
                 })
        {
            StringAssert.Contains(statement.Text, "SELECT COUNT(1)");
            StringAssert.Contains(
                statement.Text,
                "(@Status IS NULL OR Status = @Status)");
            StringAssert.Contains(
                statement.Text,
                "ORDER BY StartedAtUtc DESC, Id");
            StringAssert.Contains(statement.Text, "@Offset");
            StringAssert.Contains(statement.Text, "@PageSize");
        }

        StringAssert.Contains(
            CodeGenerationRunSql.PageSqlServer.Text,
            "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");
        StringAssert.Contains(
            CodeGenerationRunSql.PageMySql.Text,
            "LIMIT @PageSize OFFSET @Offset");
    }

    [TestMethod]
    public void Apply_and_rollback_records_allow_only_guarded_terminal_state_transitions()
    {
        var fields = typeof(CodeGenerationRunSql)
            .GetFields(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static)
            .Where(field => field.FieldType == typeof(SqlStatement))
            .Select(field => field.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                nameof(CodeGenerationRunSql.Insert),
                nameof(CodeGenerationRunSql.FindById),
                nameof(CodeGenerationRunSql.FindSucceededRollbackBySourceApplyRunId),
                nameof(CodeGenerationRunSql.FindRunningRollbackBySourceApplyRunId),
                nameof(CodeGenerationRunSql.ListPendingRollbackApplies),
                nameof(CodeGenerationRunSql.CompleteApply),
                nameof(CodeGenerationRunSql.FailApply),
                nameof(CodeGenerationRunSql.CompleteRollback),
                nameof(CodeGenerationRunSql.FailRollback),
                nameof(CodeGenerationRunSql.PageSqlServer),
                nameof(CodeGenerationRunSql.PageMySql),
                nameof(CodeGenerationRunSql.ListEligibleCheckpointCleanupSqlServer),
                nameof(CodeGenerationRunSql.ListEligibleCheckpointCleanupMySql),
                nameof(CodeGenerationRunSql.ListCapacityOverflowCheckpointCleanupSqlServer),
                nameof(CodeGenerationRunSql.ListCapacityOverflowCheckpointCleanupMySql),
            },
            fields);
        foreach (var statement in new[]
                 {
                     CodeGenerationRunSql.CompleteApply,
                     CodeGenerationRunSql.FailApply,
                 })
        {
            StringAssert.Contains(statement.Text, "OperationKind = 'apply'");
            StringAssert.Contains(statement.Text, "Status = 'running'");
            StringAssert.Contains(statement.Text, "WHERE Id = @Id");
        }

        foreach (var statement in new[]
                 {
                     CodeGenerationRunSql.CompleteRollback,
                     CodeGenerationRunSql.FailRollback,
                 })
        {
            StringAssert.Contains(statement.Text, "OperationKind = 'rollback'");
            StringAssert.Contains(statement.Text, "Status = 'running'");
            StringAssert.Contains(statement.Text, "WHERE Id = @Id");
        }

        StringAssert.Contains(
            CodeGenerationRunSql.CompleteApply.Text,
            "Status = 'succeeded'");
        StringAssert.Contains(
            CodeGenerationRunSql.FailApply.Text,
            "Status = 'failed'");
        StringAssert.Contains(
            CodeGenerationRunSql.CompleteRollback.Text,
            "Status = 'succeeded'");
        StringAssert.Contains(
            CodeGenerationRunSql.FailRollback.Text,
            "Status = 'failed'");
    }
}
