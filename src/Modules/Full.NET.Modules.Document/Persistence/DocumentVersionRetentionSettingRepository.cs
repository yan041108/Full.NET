using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Persistence;

internal sealed class DocumentVersionRetentionSettingRepository(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions)
{
    public Task<DocumentVersionRetentionSettingRecord?> GetHostAsync(CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<DocumentVersionRetentionSettingRecord>(
            DocumentVersionRetentionSettingSql.SelectHost,
            DocumentSqlParameters.Create(),
            cancellationToken);

    public Task UpsertHostAsync(
        int minimumRetainedVersionsPerItem,
        int maximumRetainedHistoryVersions,
        int pollSeconds,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var upsert = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DocumentVersionRetentionSettingSql.UpsertSqlServer,
            DatabaseProvider.MySql => DocumentVersionRetentionSettingSql.UpsertMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };

        return commandExecutor.ExecuteAsync(
            upsert,
            DocumentSqlParameters.Create(
                ("Id", DocumentVersionRetentionSettingSql.HostRowId),
                ("MinimumRetainedVersionsPerItem", minimumRetainedVersionsPerItem),
                ("MaximumRetainedHistoryVersions", maximumRetainedHistoryVersions),
                ("PollSeconds", pollSeconds),
                ("BatchSize", batchSize),
                ("UpdatedAtUtc", clock.UtcNow)),
            cancellationToken);
    }
}
