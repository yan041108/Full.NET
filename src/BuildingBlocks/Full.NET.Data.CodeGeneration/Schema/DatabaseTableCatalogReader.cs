using System.Data;
using System.Data.Common;

namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 从默认数据库 Schema 只读列举基础表，不推断业务名称或生成语义。
/// </summary>
public static class DatabaseTableCatalogReader
{

    /// <summary>
    /// 读取当前数据库的基础表名称，并使用 ordinal 规则确定性排序。
    /// </summary>
    /// <param name="connection">由调用方打开并管理生命周期的数据库连接。</param>
    /// <param name="provider">用于选择固定元数据查询的数据库方言。</param>
    /// <param name="cancellationToken">用于取消元数据查询的令牌。</param>
    /// <returns>不含视图且按 ordinal 排序的基础表名称。</returns>
    public static async Task<IReadOnlyList<string>> ListAsync(
        DbConnection connection,
        DatabaseMetadataProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException(
                "数据库基础表目录读取要求调用方先打开连接。");
        }

        var sql = provider switch
        {
            DatabaseMetadataProvider.SqlServer =>
                DatabaseCatalogQueries.ListTablesSqlServer,
            DatabaseMetadataProvider.MySql =>
                DatabaseCatalogQueries.ListTablesMySql,
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                null),
        };
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        var tableNameOrdinal = reader.GetOrdinal("TableName");
        var tableNames = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            tableNames.Add(reader.GetString(tableNameOrdinal));
        }

        return tableNames
            .OrderBy(tableName => tableName, StringComparer.Ordinal)
            .ToArray();
    }
}
