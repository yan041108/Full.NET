using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>
/// 在一次数据库往返中读取 Host 代码生成运行总数与稳定分页摘要。
/// </summary>
internal sealed class CodeGenerationRunQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<CodeGenerationRunResponse>>> ListAsync(
        int page,
        int pageSize,
        string? status,
        CancellationToken cancellationToken = default)
    {
        if (status is not null
            && status is not CodeGenerationRunStatuses.Running
                and not CodeGenerationRunStatuses.Succeeded
                and not CodeGenerationRunStatuses.Failed)
        {
            return Result<PagedResult<CodeGenerationRunResponse>>.Failure(
                new Error(
                    CodeGenerationRunErrorCodes.InvalidQuery,
                    "The code generation run query is invalid.",
                    ErrorType.Validation));
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)page - 1) * pageSize;
        var pageResult = await multiResultQueryExecutor.QueryMultipleAsync(
                ResolvePageStatement(),
                new { Offset = offset, PageSize = pageSize, Status = status },
                async (reader, _) =>
                {
                    var total = await reader.ReadSingleOrDefaultAsync<long>()
                        .ConfigureAwait(false);
                    var rows = await reader
                        .ReadAsync<CodeGenerationRunRecord>()
                        .ConfigureAwait(false);
                    return (Total: total, Rows: rows);
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Result<PagedResult<CodeGenerationRunResponse>>.Success(
            new PagedResult<CodeGenerationRunResponse>(
                pageResult.Rows.Select(Map).ToArray(),
                page,
                pageSize,
                pageResult.Total));
    }

    public async Task<Result<CodeGenerationRunResponse>> GetByIdAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindById,
                new { Id = runId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? Result<CodeGenerationRunResponse>.Failure(new Error(
                CodeGenerationRunErrorCodes.NotFound,
                "The code generation run was not found.",
                ErrorType.NotFound))
            : Result<CodeGenerationRunResponse>.Success(Map(record));
    }

    private SqlStatement ResolvePageStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => CodeGenerationRunSql.PageSqlServer,
            DatabaseProvider.MySql => CodeGenerationRunSql.PageMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };

    private static CodeGenerationRunResponse Map(
        CodeGenerationRunRecord record) =>
        new(
            record.Id,
            record.TemplateId,
            record.TemplateVersion,
            record.OperationKind,
            record.Status,
            record.ModuleKey,
            record.EntityKey,
            record.SchemaSha256,
            record.ArtifactCount,
            record.ManifestSha256,
            record.ErrorCode,
            record.RequestedByUserId,
            record.StartedAtUtc,
            record.FinishedAtUtc);
}
