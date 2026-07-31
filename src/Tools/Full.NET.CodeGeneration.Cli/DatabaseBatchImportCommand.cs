using System.Data.Common;
using Full.NET.Data.CodeGeneration.Schema;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 复用一个数据库连接导入显式映射的多张表，不承担工作区写盘职责。
/// </summary>
internal static class DatabaseBatchImportCommand
{
    public static async Task<IReadOnlyList<FullNetCrudSchema>> ImportAsync(
        DatabaseBatchCliOptions options,
        IReadOnlyList<DatabaseCrudImportOptions> mappings,
        string connectionString,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(mappings);
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

            var schemas = new List<FullNetCrudSchema>(mappings.Count);
            foreach (var mapping in mappings)
            {
                schemas.Add(await DatabaseCrudSchemaImporter.ImportAsync(
                    connection,
                    options.Provider,
                    mapping,
                    cancellationToken));
            }

            return schemas;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // 数据库驱动异常可能包含地址或凭据片段，CLI 对外只暴露稳定异常分类。
            throw new DatabaseBatchImportFailureException(exception);
        }
    }

    private sealed class DatabaseBatchImportFailureException(
        Exception innerException)
        : Exception(
            "数据库批量元数据导入失败。",
            innerException);
}
