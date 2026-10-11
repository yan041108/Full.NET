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

    public async Task<bool> TrySaveHostAsync(
        int minimumRetainedVersionsPerItem,
        int maximumRetainedHistoryVersions,
        int pollSeconds,
        int batchSize,
        long version,
        CancellationToken cancellationToken)
    {
        // 旧请求只能初始化。固定主键使两个首次写入由数据库唯一约束裁决，禁止覆盖胜出者。
        if (version == 0 && await GetHostAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            return false;
        }

        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => version == 0
                ? DocumentVersionRetentionSettingSql.InsertSqlServer
                : DocumentVersionRetentionSettingSql.UpdateSqlServer,
            DatabaseProvider.MySql => version == 0
                ? DocumentVersionRetentionSettingSql.InsertMySql
                : DocumentVersionRetentionSettingSql.UpdateMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };

        try
        {
            return await commandExecutor.ExecuteAsync(
                statement,
                DocumentSqlParameters.Create(
                    ("Id", DocumentVersionRetentionSettingSql.HostRowId),
                    ("MinimumRetainedVersionsPerItem", minimumRetainedVersionsPerItem),
                    ("MaximumRetainedHistoryVersions", maximumRetainedHistoryVersions),
                    ("PollSeconds", pollSeconds),
                    ("BatchSize", batchSize),
                    ("Version", version),
                    ("UpdatedAtUtc", clock.UtcNow)),
                cancellationToken).ConfigureAwait(false) == 1;
        }
        catch (DataCommandException exception) when (version == 0 && exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return false;
        }
    }
}
