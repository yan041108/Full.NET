using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Features.NormalizeCrudSchema;
using Full.NET.Modules.CodeGeneration.Persistence;
using Full.NET.Modules.CodeGeneration.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostTemplates;

/// <summary>
/// 在一次数据库往返中读取 Host 模板总数与稳定分页结果。
/// </summary>
internal sealed class CodeGenerationTemplateQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    CodeGenerationSchemaNormalizer schemaNormalizer,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<CodeGenerationTemplateResponse>>>
        ListAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)page - 1) * pageSize;
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer =>
                CodeGenerationTemplateSql.PageSqlServer,
            DatabaseProvider.MySql => CodeGenerationTemplateSql.PageMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var pageResult = await multiResultQueryExecutor.QueryMultipleAsync(
                statement,
                new { Offset = offset, PageSize = pageSize },
                async (reader, _) =>
                {
                    var total = await reader.ReadSingleOrDefaultAsync<long>()
                        .ConfigureAwait(false);
                    var rows = await reader
                        .ReadAsync<CodeGenerationTemplateRecord>()
                        .ConfigureAwait(false);
                    return (Total: total, Rows: rows);
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Result<PagedResult<CodeGenerationTemplateResponse>>.Success(
            new PagedResult<CodeGenerationTemplateResponse>(
                pageResult.Rows.Select(Map).ToArray(),
                page,
                pageSize,
                pageResult.Total));
    }

    public async Task<Result<CodeGenerationTemplateResponse>> GetByIdAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<CodeGenerationTemplateRecord>(
                CodeGenerationTemplateSql.FindById,
                new { Id = templateId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? NotFound()
            : Result<CodeGenerationTemplateResponse>.Success(Map(record));
    }

    private CodeGenerationTemplateResponse Map(
        CodeGenerationTemplateRecord record)
    {
        var schema = JsonSerializer.Deserialize(
                record.SchemaJson,
                CodeGenerationJsonSerializerContext.Default
                    .CodeGenerationPreviewRequest)
            ?? throw new JsonException(
                "Persisted CodeGeneration template schema is null.");
        var normalized = schemaNormalizer.Normalize(schema);
        if (!normalized.IsSuccess
            || !string.Equals(
                normalized.Value!.SchemaSha256,
                record.SchemaSha256,
                StringComparison.Ordinal))
        {
            throw new JsonException(
                "Persisted CodeGeneration template schema failed integrity validation.");
        }

        return new CodeGenerationTemplateResponse(
            record.Id,
            record.Name,
            record.Description,
            normalized.Value.CanonicalRequest,
            record.SchemaSha256,
            record.CreatedAtUtc,
            record.CreatedByUserId,
            record.UpdatedAtUtc,
            record.UpdatedByUserId,
            record.Version);
    }

    private static Result<CodeGenerationTemplateResponse> NotFound() =>
        Result<CodeGenerationTemplateResponse>.Failure(new Error(
            CodeGenerationTemplateErrorCodes.NotFound,
            "The code generation template was not found.",
            ErrorType.NotFound));
}
