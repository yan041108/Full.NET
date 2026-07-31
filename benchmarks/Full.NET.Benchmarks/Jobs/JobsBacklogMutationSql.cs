using Full.NET.Modules.Jobs.Persistence;

namespace Full.NET.Benchmarks.Jobs;

public enum JobsBacklogMutationKind
{
    TriggerInsert = 0,
    Claim = 1,
    TerminalSuccess = 2,
}

public sealed record JobsBacklogMutationStatementSet(
    string TriggerInsertSql,
    string ClaimSelectSql,
    string? ClaimUpdateSql,
    string TerminalSuccessSql);

public static class JobsBacklogMutationSql
{
    public static JobsBacklogMutationStatementSet ForProvider(
        string provider) =>
        provider switch
        {
            "sqlserver" => new JobsBacklogMutationStatementSet(
                JobSql.InsertExecution.Text,
                JobSql.AcquireExecutionsSqlServer.Text,
                ClaimUpdateSql: null,
                JobSql.MarkExecutionSucceeded.Text),
            "mysql" => new JobsBacklogMutationStatementSet(
                JobSql.InsertExecution.Text,
                JobSql.SelectClaimableExecutionIdsMySql.Text,
                JobSql.ClaimExecutionsByIdsMySql.Text,
                JobSql.MarkExecutionSucceeded.Text),
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "不支持的 Jobs backlog 写路径 A/B Provider。"),
        };
}
