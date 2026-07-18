using System.Data.Common;
using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.Seeding.Dapper;

internal static class SeedDbConnectionFactory
{
    public static DbConnection Create(DatabaseOptions options) => options.Provider switch
    {
        DatabaseProvider.SqlServer => new SqlConnection(options.ConnectionString),
        DatabaseProvider.MySql => new MySqlConnection(options.ConnectionString),
        _ => throw new ArgumentOutOfRangeException(
            nameof(options.Provider),
            options.Provider,
            "Unsupported database provider."),
    };
}
