using System.Data.Common;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.Seeding.Dapper;

/// <summary>为播种与租约获取创建双库连接；MySQL 强制 Guid 存储策略与变量策略。</summary>
internal static class SeedDbConnectionFactory
{
    public static DbConnection Create(DatabaseOptions options) => options.Provider switch
    {
        DatabaseProvider.SqlServer => new SqlConnection(options.ConnectionString),
        DatabaseProvider.MySql => new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                options.ConnectionString,
                options.MySqlGuidStorageMode,
                allowUserVariables: false)),
        _ => throw new ArgumentOutOfRangeException(
            nameof(options.Provider),
            options.Provider,
            "Unsupported database provider."),
    };
}
