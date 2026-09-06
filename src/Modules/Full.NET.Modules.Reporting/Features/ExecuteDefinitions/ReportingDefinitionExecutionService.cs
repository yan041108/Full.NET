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
internal sealed class ReportingDefinitionExecutionService(
    IQueryExecutor queryExecutor,
    ReportingDefinitionQueryService definitionQueries,
    ReportingDataSourceSecretProtector secretProtector,
    IClock clock)
{
    /// <summary>执行已发布版本并返回分页结果；SQL 失败时立即失败。</summary>
    public async Task<Result<ReportingExecutionPageResponse>> ExecuteAsync(
        Guid definitionId,
        ExecuteReportingDefinitionRequest request,
        int page,
        int pageSize,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, ReportingExecutionPolicy.MaxPageSize);
        if (request.Parameters.Any(parameter => string.IsNullOrWhiteSpace(parameter.ParameterKey)))
        {
            return InvalidParameters("Execution parameter keys are required.");
        }

        var definitionResult = await definitionQueries.GetByIdAsync(definitionId, cancellationToken)
            .ConfigureAwait(false);
        if (!definitionResult.IsSuccess || definitionResult.Value is null)
        {
            return Result<ReportingExecutionPageResponse>.Failure(definitionResult.Error!);
        }

        var definition = definitionResult.Value;
        if (!definition.IsEnabled)
        {
            return InvalidParameters("The reporting definition is disabled.");
        }

        var versionNumber = request.VersionNumber ?? definition.LatestPublishedVersionNumber;
        if (versionNumber <= 0)
        {
            return Result<ReportingExecutionPageResponse>.Failure(new Error(
                ReportingErrorCodes.DefinitionNotPublished,
                "The reporting definition has no published version to execute.",
                ErrorType.Validation));
        }

        var versionResult = await definitionQueries
            .GetVersionAsync(definitionId, versionNumber, cancellationToken)
            .ConfigureAwait(false);
        if (!versionResult.IsSuccess || versionResult.Value is null)
        {
            return Result<ReportingExecutionPageResponse>.Failure(versionResult.Error!);
        }

        var version = versionResult.Value;
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

        var dataSource = await queryExecutor.QuerySingleOrDefaultAsync<ReportingDataSourceRecord>(
                ReportingDataSourceSql.FindById,
                ReportingSqlParameters.Create(("DataSourceId", version.DataSourceId)),
                cancellationToken)
            .ConfigureAwait(false);
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
        var connectionOutcome = await ReportingDataSourceConnectionFactory
            .OpenAsync(dataSource, password, cancellationToken)
            .ConfigureAwait(false);
        if (!connectionOutcome.Succeeded || connectionOutcome.Connection is null)
        {
            return ExecutionFailed(connectionOutcome.ErrorMessage ?? "Failed to open reporting data source.");
        }

        await using (connectionOutcome.Connection)
        {
            var queryOutcome = await ReportingExternalQueryExecutor
                .ExecuteAsync(connectionOutcome.Connection, sql, sqlParameters, cancellationToken)
                .ConfigureAwait(false);
            if (!queryOutcome.Succeeded)
            {
                return ExecutionFailed(queryOutcome.ErrorMessage ?? "Reporting query execution failed.");
            }

            var resultColumnKeys = queryOutcome.ColumnKeys.Count > 0
                ? queryOutcome.ColumnKeys
                : defaultColumnKeys;
            var visibleColumns = ReportingLayoutConfigParser.ResolveVisibleColumns(
                layoutColumns,
                resultColumnKeys,
                permission => HasPermission(principal, permission));
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
            var offset = (page - 1) * pageSize;
            var hasMore = ReportingQueryPortCatalog.SupportsPagination(version.QueryPortKey)
                && topN.HasValue
                && offset + rows.Length < topN.Value
                && rows.Length == pageSize;

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

    private static bool HasPermission(ClaimsPrincipal principal, string permissionCode) =>
        principal.FindAll(FullNetIdentityClaimTypes.Permission)
            .Any(claim => string.Equals(claim.Value, permissionCode, StringComparison.Ordinal));

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
