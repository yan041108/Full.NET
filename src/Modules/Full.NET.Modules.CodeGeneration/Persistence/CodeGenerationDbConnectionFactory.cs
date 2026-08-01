using System.Data.Common;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.Modules.CodeGeneration.Persistence;

internal static class CodeGenerationDbConnectionFactory
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