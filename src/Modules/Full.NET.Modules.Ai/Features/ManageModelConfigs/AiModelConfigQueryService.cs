using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageModelConfigs;

/// <summary>AI 模型配置分页列表与详情只读查询。</summary>
internal sealed class AiModelConfigQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>按标识读取模型配置详情。</summary>
    /// <param name="modelConfigId">配置标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>详情或稳定业务错误。</returns>
    public async Task<Result<AiModelConfigResponse>> GetByIdAsync(
        Guid modelConfigId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<AiModelConfigRecord>(
                AiModelConfigSql.FindById,
                AiSqlParameters.Create(("ModelConfigId", modelConfigId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return NotFoundDetail();
        }

        return Result<AiModelConfigResponse>.Success(AiModelConfigMapper.MapDetail(row));
    }

    /// <summary>分页查询模型配置列表（脱敏）。</summary>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="tenantId">可选租户筛选。</param>
    /// <param name="nameContains">可选名称模糊筛选。</param>
    /// <param name="isEnabled">可选启用状态筛选。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public async Task<Result<PagedResult<AiModelConfigListItem>>> ListAsync(
        int page,
        int pageSize,
        Guid? tenantId,
        string? nameContains,
        bool? isEnabled,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = AiSqlParameters.Create(
            ("TenantId", tenantId),
            ("NameContains", NormalizeFilter(nameContains)),
            ("IsEnabled", isEnabled),
            ("Offset", offset),
            ("PageSize", pageSize));
        var (countStatement, listStatement) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (
                AiModelConfigSql.CountSqlServer,
                AiModelConfigSql.ListSqlServer),
            DatabaseProvider.MySql => (
                AiModelConfigSql.CountMySql,
                AiModelConfigSql.ListMySql),
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                new SqlStatement("ai.count_model_configs", countStatement, SqlDataScope.HostOnly),
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<AiModelConfigRecord>(
                new SqlStatement("ai.list_model_configs", listStatement, SqlDataScope.HostOnly),
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<AiModelConfigListItem>>.Success(
            new PagedResult<AiModelConfigListItem>(
                rows.Select(AiModelConfigMapper.MapListItem).ToArray(),
                page,
                pageSize,
                total));
    }

    private static string? NormalizeFilter(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<AiModelConfigResponse> NotFoundDetail() =>
        Result<AiModelConfigResponse>.Failure(new Error(
            AiErrorCodes.ModelConfigNotFound,
            "The AI model configuration was not found.",
            ErrorType.NotFound));
}
