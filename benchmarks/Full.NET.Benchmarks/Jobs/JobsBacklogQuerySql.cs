using Full.NET.Modules.Jobs.Persistence;

namespace Full.NET.Benchmarks.Jobs;

public static class JobsBacklogQuerySql
{
    public static string ForProvider(string provider) =>
        provider switch
        {
            "sqlserver" => JobSql.ReadBacklogSqlServer.Text,
            "mysql" => JobSql.ReadBacklogMySql.Text,
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "不支持的 Jobs backlog 基准数据库 Provider。"),
        };
}
