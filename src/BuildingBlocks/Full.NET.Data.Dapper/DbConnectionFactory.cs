using System.Data.Common;
using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.Data.Dapper;

internal sealed class DbConnectionFactory(IOptions<DatabaseOptions> options)
{
    private readonly DatabaseOptions _options = options.Value;

    public DbConnection Create() => _options.Provider switch
    {
        DatabaseProvider.SqlServer => new SqlConnection(_options.ConnectionString),
        DatabaseProvider.MySql => new MySqlConnection(_options.ConnectionString),
        _ => throw new ArgumentOutOfRangeException(
            nameof(_options.Provider),
            _options.Provider,
            "Unsupported database provider."),
    };
}
