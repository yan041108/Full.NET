using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Reporting.Features.ManageDataSources;

/// <summary>报表数据源分页列表与详情只读查询。</summary>
internal sealed class ReportingDataSourceQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>按标识读取报表数据源详情。</summary>
    /// <param name="dataSourceId">数据源标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>详情或稳定业务错误。</returns>
    public async Task<Result<ReportingDataSourceResponse>> GetByIdAsync(
        Guid dataSourceId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<ReportingDataSourceRecord>(
                ReportingDataSourceSql.FindById,
                ReportingSqlParameters.Create(("DataSourceId", dataSourceId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return NotFoundDetail();
        }

        return Result<ReportingDataSourceResponse>.Success(ReportingDataSourceMapper.MapDetail(row));
    }

    /// <summary>分页查询报表数据源列表（脱敏）。</summary>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="tenantId">可选租户筛选。</param>
    /// <param name="nameContains">可选名称模糊筛选。</param>
    /// <param name="isEnabled">可选启用状态筛选。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public async Task<Result<PagedResult<ReportingDataSourceListItem>>> ListAsync(
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
        var filter = ReportingSqlParameters.Create(
            ("TenantId", tenantId),
            ("NameContains", NormalizeFilter(nameContains)),
            ("IsEnabled", isEnabled),
            ("Offset", offset),
            ("PageSize", pageSize));
        var (countStatement, listStatement) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (
                ReportingDataSourceSql.CountSqlServer,
                ReportingDataSourceSql.ListSqlServer),
            DatabaseProvider.MySql => (
                ReportingDataSourceSql.CountMySql,
                ReportingDataSourceSql.ListMySql),
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                new SqlStatement("reporting.count_data_sources", countStatement, SqlDataScope.HostOnly),
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<ReportingDataSourceRecord>(
                new SqlStatement("reporting.list_data_sources", listStatement, SqlDataScope.HostOnly),
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<ReportingDataSourceListItem>>.Success(
            new PagedResult<ReportingDataSourceListItem>(
                rows.Select(ReportingDataSourceMapper.MapListItem).ToArray(),
                page,
                pageSize,
                total));
    }

    private static string? NormalizeFilter(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<ReportingDataSourceResponse> NotFoundDetail() =>
        Result<ReportingDataSourceResponse>.Failure(new Error(
            ReportingErrorCodes.DataSourceNotFound,
            "The reporting data source was not found.",
            ErrorType.NotFound));
}
