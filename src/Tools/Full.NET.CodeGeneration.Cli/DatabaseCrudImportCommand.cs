using System.Data.Common;
using Full.NET.Data.CodeGeneration.Schema;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 在 CLI 边界创建具体数据库连接，并把只读元数据导入委托给 provider-neutral 内核。
/// </summary>
internal static class DatabaseCrudImportCommand
{
    public static async Task<FullNetCrudSchema> ImportAsync(
        DatabaseImportCliOptions options,
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
            return await DatabaseCrudSchemaImporter.ImportAsync(
                connection,
                options.Provider,
                options.ToImportOptions(),
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // 驱动异常可能包含地址或凭据片段，只保留异常链供进程内诊断，
            // 对外 stderr 由 CLI 输出稳定分类，不输出原始消息。
            throw new DatabaseImportFailureException(exception);
        }
    }

    private sealed class DatabaseImportFailureException(
        Exception innerException)
        : Exception(
            "数据库元数据导入失败。",
            innerException);
}
