using System.Data.Common;
using Full.NET.Data.CodeGeneration.Schema;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 在 CLI 边界创建具体数据库连接，并委托内核读取只读基础表目录。
/// </summary>
internal static class DatabaseTableCatalogCommand
{
    public static async Task<IReadOnlyList<string>> ListAsync(
        DatabaseCatalogCliOptions options,
        string connectionString,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        try
        {
            await using DbConnection connection = options.Provider switch
            {
                DatabaseMetadataProvider.SqlServer =>
                    new SqlConnection(connectionString),
                DatabaseMetadataProvider.MySql =>
                    new MySqlConnection(connectionString),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.Provider,
                    null),
            };
            await connection.OpenAsync(cancellationToken);
            return await DatabaseTableCatalogReader.ListAsync(
                connection,
                options.Provider,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // 驱动异常可能携带数据库地址或凭据片段，CLI 对外只暴露稳定分类。
            throw new DatabaseTableCatalogFailureException(exception);
        }
    }

    private sealed class DatabaseTableCatalogFailureException(
        Exception innerException)
        : Exception(
            "数据库基础表目录读取失败。",
            innerException);
}
