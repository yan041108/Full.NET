using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>为原生 Worker 准备本地文件对账探针，并按文件标识读取双终态。</summary>
internal static class NativeWorkerFilesProbe
{
    private const string InsertSql =
        """
        INSERT INTO fn_files_file
            (Id, TenantId, OriginalFileName, ContentType, SizeBytes,
             ProviderKey, StorageKey, ContentHash, StorageState,
             CreatedAtUtc, CreatedByUserId, DeletedAtUtc)
        VALUES
            (@Id, NULL, @OriginalFileName, @ContentType, @SizeBytes,
             @ProviderKey, @StorageKey, @ContentHash, @StorageState,
             @CreatedAtUtc, @CreatedByUserId, NULL)
        """;

    private const string SelectStatesSql =
        """
        SELECT
            CASE WHEN EXISTS (
                SELECT 1 FROM fn_files_file
                WHERE Id = @ExistingId
                  AND TenantId IS NULL
                  AND StorageState = 'ready'
                  AND DeletedAtUtc IS NULL
            ) THEN 1 ELSE 0 END AS IsExistingReady,
            CASE WHEN EXISTS (
                SELECT 1 FROM fn_files_file
                WHERE Id = @MissingId
                  AND TenantId IS NULL
            ) THEN 1 ELSE 0 END AS IsMissingPresent
        """;

    public static async Task<NativeWorkerFilesScenario> PrepareAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var existingId = Guid.CreateVersion7();
        var missingId = Guid.CreateVersion7();
        var createdByUserId = Guid.CreateVersion7();
        var storageRoot = Path.Combine(
            GetOwnedStorageParent(),
            Guid.NewGuid().ToString("N"));
        var existingStorageKey = $"native-aot/{existingId:N}.bin";
        var missingStorageKey = $"native-aot/{missingId:N}.bin";
        var existingBlobPath = Path.Combine(
            storageRoot,
            existingStorageKey.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(
            Path.GetDirectoryName(existingBlobPath)
                ?? throw new InvalidOperationException("Files probe storage key has no directory."));
        await File.WriteAllBytesAsync(
                existingBlobPath,
                [0x46, 0x75, 0x6C, 0x6C, 0x4E, 0x45, 0x54],
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var createdAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
            var databaseCreatedAtUtc = provider == DatabaseProvider.MySql
                ? (object)createdAtUtc.UtcDateTime
                : createdAtUtc;
            await using var connection = CreateConnection(provider, connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await InsertAsync(
                    connection,
                    transaction,
                    existingId,
                    existingStorageKey,
                    existingBlobPath,
                    databaseCreatedAtUtc,
                    createdByUserId,
                    cancellationToken)
                .ConfigureAwait(false);
            await InsertAsync(
                    connection,
                    transaction,
                    missingId,
                    missingStorageKey,
                    existingBlobPath,
                    databaseCreatedAtUtc,
                    createdByUserId,
                    cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new NativeWorkerFilesScenario(
                existingId,
                missingId,
                storageRoot,
                existingBlobPath);
        }
        catch
        {
            DeleteOwnedStorageRoot(storageRoot);
            throw;
        }
    }

    public static async Task<NativeWorkerFilesTerminalStates> WaitForTerminalStatesAsync(
        DatabaseProvider provider,
        string connectionString,
        NativeWorkerFilesScenario scenario,
        TimeSpan timeout,
        string logFilePath,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        NativeWorkerFilesTerminalStates? lastStates = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var connection = CreateConnection(provider, connectionString);
            lastStates = await connection.QuerySingleAsync<NativeWorkerFilesTerminalStates>(
                    new CommandDefinition(
                        SelectStatesSql,
                        new
                        {
                            scenario.ExistingId,
                            scenario.MissingId,
                        },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);
            if (lastStates.IsExistingReady == 1
                && lastStates.IsMissingPresent == 0)
            {
                return lastStates;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Native Worker 未在 {timeout} 内写入 Files 对账终态；"
            + $"IsExistingReady={lastStates?.IsExistingReady}, "
            + $"IsMissingPresent={lastStates?.IsMissingPresent}。日志：{logFilePath}");
    }

    public static void DeleteOwnedStorageRoot(string storageRoot)
    {
        var ownedParent = GetOwnedStorageParent();
        var resolvedRoot = Path.GetFullPath(storageRoot);
        var resolvedParent = Path.GetFullPath(ownedParent)
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!resolvedRoot.StartsWith(resolvedParent, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                resolvedRoot.TrimEnd(Path.DirectorySeparatorChar),
                resolvedParent.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Files probe refused to delete an unowned directory.");
        }

        if (Directory.Exists(resolvedRoot))
        {
            Directory.Delete(resolvedRoot, recursive: true);
        }
    }

    private static Task<int> InsertAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid id,
        string storageKey,
        string existingBlobPath,
        object createdAtUtc,
        Guid createdByUserId,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(
            new CommandDefinition(
                InsertSql,
                new
                {
                    Id = id,
                    OriginalFileName = $"{id:N}.bin",
                    ContentType = "application/octet-stream",
                    SizeBytes = new FileInfo(existingBlobPath).Length,
                    ProviderKey = "local",
                    StorageKey = storageKey,
                    ContentHash = (string?)null,
                    StorageState = "pending",
                    CreatedAtUtc = createdAtUtc,
                    CreatedByUserId = createdByUserId,
                },
                transaction,
                cancellationToken: cancellationToken));

    private static string GetOwnedStorageParent() => Path.Combine(
        Path.GetTempPath(),
        "fullnet-worker-native-aot-files-e2e");

    private static DbConnection CreateConnection(
        DatabaseProvider provider,
        string connectionString) => provider switch
        {
            DatabaseProvider.SqlServer => new SqlConnection(connectionString),
            DatabaseProvider.MySql => new MySqlConnection(
                MySqlConnectionStringPolicy.Create(
                    connectionString,
                    MySqlGuidStorageMode.Binary16,
                    allowUserVariables: false)),
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{provider}'."),
        };
}

internal sealed record NativeWorkerFilesScenario(
    Guid ExistingId,
    Guid MissingId,
    string StorageRoot,
    string ExistingBlobPath);

internal sealed class NativeWorkerFilesTerminalStates
{
    public int IsExistingReady { get; init; }

    public int IsMissingPresent { get; init; }
}
