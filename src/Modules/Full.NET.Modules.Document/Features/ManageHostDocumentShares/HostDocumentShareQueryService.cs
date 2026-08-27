using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentShares;

/// <summary>
/// Host 文档分享只读查询服务。投影绝不包含 PasswordHash 明文或哈希，
/// 所有响应字段均与 <see cref="HostDocumentShareResponse"/> 显式映射，避免序列化泄露敏感列。
/// </summary>
internal sealed class HostDocumentShareQueryService(
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<HostDocumentShareResponse>>> PageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)page - 1) * pageSize;

        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DocumentShareSql.PageSqlServer,
            DatabaseProvider.MySql => DocumentShareSql.PageMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };

        var pageResult = await multiResultQueryExecutor.QueryMultipleAsync(
                statement,
                DocumentSqlParameters.Create(("Offset", offset), ("PageSize", pageSize)),
                async (reader, _) =>
                {
                    var total = await reader.ReadSingleOrDefaultAsync<long>().ConfigureAwait(false);
                    var rows = await reader.ReadAsync<DocumentShareRecord>().ConfigureAwait(false);
                    return (Total: total, Rows: rows);
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Result<PagedResult<HostDocumentShareResponse>>.Success(
            new PagedResult<HostDocumentShareResponse>(
                pageResult.Rows.Select(Map).ToArray(),
                page,
                pageSize,
                pageResult.Total));
    }

    public async Task<Result<HostDocumentShareResponse>> GetByIdAsync(
        Guid shareId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentShareRecord>(
                DocumentShareSql.FindById,
                DocumentSqlParameters.Create(("Id", shareId)),
                cancellationToken)
            .ConfigureAwait(false);

        return record is null
            ? NotFound()
            : Result<HostDocumentShareResponse>.Success(Map(record));
    }

    public async Task<Result<HostDocumentShareResponse>> GetByCodeAsync(
        string shareCode,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentShareRecord>(
                DocumentShareSql.FindByCode,
                DocumentSqlParameters.Create(("ShareCode", shareCode)),
                cancellationToken)
            .ConfigureAwait(false);

        return record is null
            ? CodeNotFound()
            : Result<HostDocumentShareResponse>.Success(Map(record));
    }

    internal static HostDocumentShareResponse Map(DocumentShareRecord record) =>
        new(
            record.Id,
            record.DocumentId,
            record.ShareCode,
            record.CreatedAtUtc,
            record.ExpireTime,
            // 口令字段只允许在服务端参与校验，任何查询响应都不得回显持久化凭据。
            Password: null,
            record.MaxAccessCount,
            record.AccessCount,
            record.IsEnabled,
            record.Version,
            HasPassword: !string.IsNullOrEmpty(record.PasswordHash));

    private static Result<HostDocumentShareResponse> NotFound() =>
        Result<HostDocumentShareResponse>.Failure(NotFoundError());

    private static Result<HostDocumentShareResponse> CodeNotFound() =>
        Result<HostDocumentShareResponse>.Failure(CodeNotFoundError());

    private static Error NotFoundError() =>
        new(DocumentErrorCodes.ShareNotFound, "Document share was not found.", ErrorType.NotFound);

    private static Error CodeNotFoundError() =>
        new(DocumentErrorCodes.ShareCodeNotFound, "Share code was not found.", ErrorType.NotFound);
}
