using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>为原生 Worker 准备 Files 引用 Claim 对账场景，并读取两类确定终态。</summary>
internal static class NativeWorkerFilesReferenceClaimProbe
{
    private const string InsertFileSql =
        """
        INSERT INTO fn_files_file
            (Id, TenantId, OriginalFileName, ContentType, SizeBytes,
             ProviderKey, StorageKey, ContentHash, StorageState,
             CreatedAtUtc, CreatedByUserId, DeletedAtUtc)
        VALUES
            (@Id, NULL, @OriginalFileName, 'application/octet-stream', 7,
             'local', @StorageKey, NULL, 'ready',
             @CreatedAtUtc, @CreatedByUserId, NULL)
        """;

    private const string InsertDocumentItemSql =
        """
        INSERT INTO fn_document_item
            (Id, TenantId, CategoryId, CurrentVersionId, Title, Description,
             IsDeleted, DeletedAtUtc, DeletedByUserId,
             CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId, Version)
        VALUES
            (@Id, NULL, NULL, NULL, @Title, NULL,
             0, NULL, NULL,
             @CreatedAtUtc, @CreatedByUserId, NULL, NULL, 1)
        """;

    private const string InsertDocumentVersionSql =
        """
        INSERT INTO fn_document_version
            (Id, DocumentItemId, FileId, VersionNumber, ContentHash, SizeBytes,
             UploadedByUserId, CreatedAtUtc)
        VALUES
            (@Id, @DocumentItemId, @FileId, 1, NULL, 7,
             @UploadedByUserId, @CreatedAtUtc)
        """;

    private const string InsertClaimSql =
        """
        INSERT INTO fn_files_file_reference_claim
            (Id, IdempotencyKey, FileId, ConsumerModule, ConsumerReferenceId,
             State, ContentHash, SizeBytes, CreatedAtUtc, UpdatedAtUtc,
             ConfirmedAtUtc, ReleasedAtUtc)
        VALUES
            (@Id, @IdempotencyKey, @FileId, 'document', @ConsumerReferenceId,
             'pending', NULL, 7, @CreatedAtUtc, @UpdatedAtUtc,
             NULL, NULL)
        """;

    private const string SelectTerminalStatesSql =
        """
        SELECT
            CASE WHEN EXISTS (
                SELECT 1
                FROM fn_files_file_reference_claim
                WHERE Id = @ReferencedClaimId
                  AND State = 'active'
                  AND ConfirmedAtUtc IS NOT NULL
                  AND ReleasedAtUtc IS NULL
            ) THEN 1 ELSE 0 END AS IsReferencedActive,
            CASE WHEN EXISTS (
                SELECT 1
                FROM fn_files_file_reference_claim
                WHERE Id = @OrphanClaimId
                  AND State = 'released'
                  AND ConfirmedAtUtc IS NULL
                  AND ReleasedAtUtc IS NOT NULL
            ) THEN 1 ELSE 0 END AS IsOrphanReleased
        """;

    public static async Task<NativeWorkerFilesReferenceClaimScenario> PrepareAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var referencedFileId = Guid.CreateVersion7();
        var orphanFileId = Guid.CreateVersion7();
        var documentItemId = Guid.CreateVersion7();
        var documentVersionId = Guid.CreateVersion7();
        var referencedClaimId = Guid.CreateVersion7();
        var orphanClaimId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var createdAtUtc = DateTimeOffset.UtcNow.AddMinutes(-3);
        var updatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
        var databaseCreatedAtUtc = ToDatabaseTimestamp(provider, createdAtUtc);
        var databaseUpdatedAtUtc = ToDatabaseTimestamp(provider, updatedAtUtc);

        await using var connection = CreateConnection(provider, connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await InsertFileAsync(
                connection,
                transaction,
                referencedFileId,
                databaseCreatedAtUtc,
                userId,
                cancellationToken)
            .ConfigureAwait(false);
        await InsertFileAsync(
                connection,
                transaction,
                orphanFileId,
                databaseCreatedAtUtc,
                userId,
                cancellationToken)
            .ConfigureAwait(false);
        await connection.ExecuteAsync(
                new CommandDefinition(
                    InsertDocumentItemSql,
                    new
                    {
                        Id = documentItemId,
                        Title = "Native AOT reference claim probe",
                        CreatedAtUtc = databaseCreatedAtUtc,
                        CreatedByUserId = userId,
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        await connection.ExecuteAsync(
                new CommandDefinition(
                    InsertDocumentVersionSql,
                    new
                    {
                        Id = documentVersionId,
                        DocumentItemId = documentItemId,
                        FileId = referencedFileId,
                        UploadedByUserId = userId,
                        CreatedAtUtc = databaseCreatedAtUtc,
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        await InsertClaimAsync(
                connection,
                transaction,
                referencedClaimId,
                referencedFileId,
                documentVersionId,
                databaseCreatedAtUtc,
                databaseUpdatedAtUtc,
                cancellationToken)
            .ConfigureAwait(false);
        await InsertClaimAsync(
                connection,
                transaction,
                orphanClaimId,
                orphanFileId,
                Guid.CreateVersion7(),
                databaseCreatedAtUtc,
                databaseUpdatedAtUtc,
                cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new NativeWorkerFilesReferenceClaimScenario(
            referencedClaimId,
            orphanClaimId);
    }

    public static async Task<NativeWorkerFilesReferenceClaimTerminalStates>
        WaitForTerminalStatesAsync(
            DatabaseProvider provider,
            string connectionString,
            NativeWorkerFilesReferenceClaimScenario scenario,
            TimeSpan timeout,
            string logFilePath,
            CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        NativeWorkerFilesReferenceClaimTerminalStates? lastStates = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var connection = CreateConnection(provider, connectionString);
            lastStates = await connection
                .QuerySingleAsync<NativeWorkerFilesReferenceClaimTerminalStates>(
                    new CommandDefinition(
                        SelectTerminalStatesSql,
                        new
                        {
                            scenario.ReferencedClaimId,
                            scenario.OrphanClaimId,
                        },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);
            if (lastStates.IsReferencedActive == 1
                && lastStates.IsOrphanReleased == 1)
            {
                return lastStates;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Native Worker 未在 {timeout} 内写入 Files Reference Claim 对账终态；"
            + $"IsReferencedActive={lastStates?.IsReferencedActive}, "
            + $"IsOrphanReleased={lastStates?.IsOrphanReleased}。日志：{logFilePath}");
    }

    private static Task<int> InsertFileAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid id,
        object createdAtUtc,
        Guid createdByUserId,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(
            new CommandDefinition(
                InsertFileSql,
                new
                {
                    Id = id,
                    OriginalFileName = $"{id:N}.bin",
                    StorageKey = $"native-aot/{id:N}.bin",
                    CreatedAtUtc = createdAtUtc,
                    CreatedByUserId = createdByUserId,
                },
                transaction,
                cancellationToken: cancellationToken));

    private static Task<int> InsertClaimAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid id,
        Guid fileId,
        Guid consumerReferenceId,
        object createdAtUtc,
        object updatedAtUtc,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(
            new CommandDefinition(
                InsertClaimSql,
                new
                {
                    Id = id,
                    IdempotencyKey = $"native-aot-reference-claim:{id:D}",
                    FileId = fileId,
                    ConsumerReferenceId = consumerReferenceId,
                    CreatedAtUtc = createdAtUtc,
                    UpdatedAtUtc = updatedAtUtc,
                },
                transaction,
                cancellationToken: cancellationToken));

    private static object ToDatabaseTimestamp(
        DatabaseProvider provider,
        DateTimeOffset timestamp) => provider == DatabaseProvider.MySql
        ? timestamp.UtcDateTime
        : timestamp;

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

internal sealed record NativeWorkerFilesReferenceClaimScenario(
    Guid ReferencedClaimId,
    Guid OrphanClaimId);

internal sealed class NativeWorkerFilesReferenceClaimTerminalStates
{
    public int IsReferencedActive { get; init; }

    public int IsOrphanReleased { get; init; }
}
