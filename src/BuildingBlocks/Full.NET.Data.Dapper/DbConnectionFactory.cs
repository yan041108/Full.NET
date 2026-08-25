using System.Data.Common;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.Data.Dapper;

/// <summary>
/// 数据库连接工厂，按 <see cref="DatabaseOptions.Provider"/> 创建对应 Provider 的 DbConnection 实例。
/// </summary>
/// <remarks>
/// <para>生命周期：Singleton，每次调用 <see cref="Create"/> 返回全新连接实例，由调用方（DbSession）负责释放。</para>
/// <para>MySQL 策略：连接字符串通过 <see cref="MySqlConnectionStringPolicy"/> 二次处理，
/// 强制统一 GuidStorageMode 与 UserVariables 开关，避免应用层配置漂移。</para>
/// </remarks>
internal sealed class DbConnectionFactory(IOptions<DatabaseOptions> options)
    : IDbConnectionFactory
{
    private readonly DatabaseOptions _options = options.Value;

    /// <summary>
    /// 创建新的数据库连接实例（尚未打开）。
    /// </summary>
    /// <returns>根据 Provider 类型创建的 DbConnection。</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 <see cref="DatabaseOptions.Provider"/> 为不支持的值时抛出。</exception>
    public DbConnection Create() => _options.Provider switch
    {
        DatabaseProvider.SqlServer => new SqlConnection(_options.ConnectionString),
        DatabaseProvider.MySql => CreateMySqlConnection(),
        _ => throw new ArgumentOutOfRangeException(
            nameof(_options.Provider),
            _options.Provider,
            "Unsupported database provider."),
    };

    private DbConnection CreateMySqlConnection()
    {
        var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                _options.ConnectionString,
                _options.MySqlGuidStorageMode,
                allowUserVariables: false));
#if FULLNET_AOT_COMPILE
        return new MySqlAotUtcDateTimeOffsetConnection(connection);
#else
        return connection;
#endif
    }
}
