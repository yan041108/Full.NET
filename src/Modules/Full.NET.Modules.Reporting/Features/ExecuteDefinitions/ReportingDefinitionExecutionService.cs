using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Connectivity;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;
using Full.NET.Modules.Reporting.Features.ManageDefinitions;
using Full.NET.Modules.Reporting.Persistence;
using Full.NET.Modules.Reporting.Security;

namespace Full.NET.Modules.Reporting.Features.ExecuteDefinitions;

/// <summary>对已发布报表定义执行受界 Query Port 并返回分页结果。</summary>
/// <param name="publishedDefinitions">报表定义查询。</param>
/// <param name="secretProtector">数据源密码保护器。</param>
/// <param name="connectionFactory">外部数据源连接工厂。</param>
/// <param name="clock">时钟。</param>
/// <param name="permissions">身份模块统一的当前作用域权限解释器。</param>
internal sealed class ReportingDefinitionExecutionService(
    Full.NET.Modules.Reporting.Features.PublishedDefinitions.ReportingPublishedDefinitionResolver publishedDefinitions,
    ReportingDataSourceSecretProtector secretProtector,
    ReportingDataSourceConnectionFactory connectionFactory,
    IClock clock,
    IIdentityPermissionEvaluator permissions)
{
    /// <summary>执行已发布版本并返回分页结果；SQL 失败时立即失败。</summary>
    /// <param name="definitionId">报表定义标识。</param>
    /// <param name="request">执行请求，含参数与可选版本。</param>
    /// <param name="page">页码，小于 1 时按 1 处理。</param>
    /// <param name="pageSize">页大小，受执行策略上限约束。</param>
    /// <param name="principal">当前主体，用于结果列权限过滤。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果或业务失败。</returns>
    public async Task<Result<ReportingExecutionPageResponse>> ExecuteAsync(
        Guid definitionId,
        ExecuteReportingDefinitionRequest request,
        int page,
        int pageSize,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, ReportingExecutionPolicy.MaxPageSize);
        if (request?.Parameters is null || request.Parameters.Any(parameter => parameter is null || string.IsNullOrWhiteSpace(parameter.ParameterKey)))
        {
            return InvalidParameters("Execution parameter keys are required.");
        }

        var published = await publishedDefinitions.ResolveAsync(definitionId, request.VersionNumber, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!published.IsSuccess) return Result<ReportingExecutionPageResponse>.Failure(published.Error!);
        var definition = published.Value!.Definition;
        var version = published.Value.Version;
        var versionNumber = version.VersionNumber;
        var queryPort = ReportingQueryPortCatalog.TryGet(version.QueryPortKey);
        if (queryPort is null)
        {
            return Result<ReportingExecutionPageResponse>.Failure(new Error(
                ReportingErrorCodes.QueryPortNotFound,
                "The selected query port was not found.",
                ErrorType.Validation));
        }

        var bindOutcome = ReportingExecutionParameterBinder.Bind(
            queryPort,
            version.ParameterSchema,
            request.Parameters);
        if (!bindOutcome.Succeeded)
        {
            return InvalidParameters(bindOutcome.ErrorMessage!);
        }

        var dataSource = await publishedDefinitions.FindDataSourceAsync(published.Value, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (dataSource is null || !dataSource.IsEnabled)
        {
            return InvalidParameters("The reporting data source was not found or is disabled.");
        }

        if (string.IsNullOrWhiteSpace(dataSource.PasswordProtected))
        {
            return InvalidParameters("Password is not configured for the reporting data source.");
        }

        if (!queryPort.SupportedProviderKeys.Contains(dataSource.ProviderKey, StringComparer.Ordinal))
        {
            return InvalidParameters("The selected query port does not support the data source provider.");
        }

        var layoutColumns = ReportingLayoutConfigParser.ParseColumns(version.LayoutConfigJson);
        var defaultColumnKeys = ReportingQueryPortCatalog.ResolveResultColumnKeys(version.QueryPortKey);
        var (sql, sqlParameters) = ReportingExecutionSqlBuilder.Build(
            version.QueryPortKey,
            dataSource.ProviderKey,
            bindOutcome.Values,
            page,
            pageSize);

        var password = secretProtector.Unprotect(dataSource.PasswordProtected);
        var sessionOutcome = await connectionFactory
            .OpenAsync(dataSource, password, cancellationToken)
            .ConfigureAwait(false);
        if (sessionOutcome?.Session is not { } session)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ExecutionFailed("Failed to open reporting data source.");
        }

        // 交付会话即转移释放责任，失败载荷或打开后的取消也必须在持有范围内清理。
        await using (session)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sessionOutcome.Succeeded) return ExecutionFailed("Failed to open reporting data source.");
            var queryOutcome = await session
                .ExecuteQueryAsync(
                    sql,
                    sqlParameters,
                    ReportingExecutionPolicy.CommandTimeoutSeconds,
                    ReportingExecutionPolicy.MaxCellValueLength,
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!queryOutcome.Succeeded)
            {
                return ExecutionFailed("Reporting query execution failed.");
            }

            var resultColumnKeys = queryOutcome.ColumnKeys.Count > 0
                ? queryOutcome.ColumnKeys
                : defaultColumnKeys;
            var visibleColumns = ReportingLayoutConfigParser.ResolveVisibleColumns(
                layoutColumns,
                resultColumnKeys,
                permission => permissions.HasPermission(principal, permission));
            if (visibleColumns.Count == 0)
            {
                return Result<ReportingExecutionPageResponse>.Failure(new Error(
                    ReportingErrorCodes.ExecutionColumnsDenied,
                    "No result columns are visible for the current principal.",
                    ErrorType.Forbidden));
            }

            var visibleColumnKeys = visibleColumns
                .Select(column => column.ColumnKey)
                .ToHashSet(StringComparer.Ordinal);
            var rows = queryOutcome.Rows
                .Select(row => new ReportingExecutionRow(
                    row.Where(entry => visibleColumnKeys.Contains(entry.Key))
                        .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)))
                .ToArray();

            var topN = bindOutcome.Values.TryGetValue("topN", out var topNValue) && topNValue is int topNInt
                ? topNInt
                : (int?)null;
            var offset = ((long)page - 1) * pageSize;
            var hasMore = ReportingQueryPortCatalog.SupportsPagination(version.QueryPortKey)
                && topN.HasValue
                && offset + rows.Length < topN.Value
                && rows.Length == pageSize;

            // 外部查询位于事务外；返回前复核本次实际执行的版本，不能切换到新发布版本。
            var stillAvailable = await publishedDefinitions.ResolveAsync(definition.Id, versionNumber, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!stillAvailable.IsSuccess) return Result<ReportingExecutionPageResponse>.Failure(stillAvailable.Error!);

            return Result<ReportingExecutionPageResponse>.Success(new ReportingExecutionPageResponse(
                definition.Id,
                definition.DefinitionKey,
                definition.Name,
                version.VersionNumber,
                version.QueryPortKey,
                visibleColumns,
                rows,
                page,
                pageSize,
                hasMore,
                topN,
                ReportingExecutionPolicy.CommandTimeoutSeconds,
                clock.UtcNow));
        }
    }

    private static Result<ReportingExecutionPageResponse> InvalidParameters(string message) =>
        Result<ReportingExecutionPageResponse>.Failure(new Error(
            ReportingErrorCodes.ExecutionParametersInvalid,
            message,
            ErrorType.Validation));

    private static Result<ReportingExecutionPageResponse> ExecutionFailed(string message) =>
        Result<ReportingExecutionPageResponse>.Failure(new Error(
            ReportingErrorCodes.ExecutionFailed,
            message,
            ErrorType.Validation));
}
