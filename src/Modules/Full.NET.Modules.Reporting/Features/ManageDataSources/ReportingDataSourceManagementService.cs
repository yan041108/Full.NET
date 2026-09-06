using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;
using Full.NET.Modules.Reporting.Persistence;
using Full.NET.Modules.Reporting.Security;

namespace Full.NET.Modules.Reporting.Features.ManageDataSources;

/// <summary>报表数据源创建、更新、禁用与删除。</summary>
internal sealed class ReportingDataSourceManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ReportingDataSourceQueryService queries,
    IIdentityActiveTenantDirectory activeTenants,
    ReportingDataSourceSecretProtector secretProtector,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>创建报表数据源。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建结果或稳定业务错误。</returns>
    public Task<Result<ReportingDataSourceResponse>> CreateAsync(
        CreateReportingDataSourceRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    /// <summary>更新报表数据源。</summary>
    /// <param name="dataSourceId">数据源标识。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的数据源或稳定业务错误。</returns>
    public Task<Result<ReportingDataSourceResponse>> UpdateAsync(
        Guid dataSourceId,
        UpdateReportingDataSourceRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(dataSourceId, request, token),
            cancellationToken);

    /// <summary>禁用报表数据源。</summary>
    /// <param name="dataSourceId">数据源标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>禁用后的数据源或稳定业务错误。</returns>
    public Task<Result<ReportingDataSourceResponse>> DisableAsync(
        Guid dataSourceId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DisableCoreAsync(dataSourceId, token),
            cancellationToken);

    /// <summary>删除报表数据源。</summary>
    /// <param name="dataSourceId">数据源标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除成功或稳定业务错误。</returns>
    public Task<Result<bool>> DeleteAsync(
        Guid dataSourceId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DeleteCoreAsync(dataSourceId, token),
            cancellationToken);

    private async Task<Result<ReportingDataSourceResponse>> CreateCoreAsync(
        CreateReportingDataSourceRequest request,
        CancellationToken cancellationToken)
    {
        var validationMessage = ReportingDataSourceFieldValidator.ValidateMetadata(
            request.Name,
            request.ProviderKey,
            request.ServerHost,
            request.Port,
            request.DatabaseName,
            request.Username);
        if (validationMessage is not null)
        {
            return ValidationFailure<ReportingDataSourceResponse>(validationMessage);
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return Result<ReportingDataSourceResponse>.Failure(new Error(
                ReportingErrorCodes.DataSourcePasswordRequired,
                "Password is required when creating a reporting data source.",
                ErrorType.Validation));
        }

        var tenantValidation = await ValidateTenantScopeAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!tenantValidation.IsSuccess)
        {
            return Result<ReportingDataSourceResponse>.Failure(tenantValidation.Error!);
        }

        var now = clock.UtcNow;
        var dataSourceId = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
                ReportingDataSourceSql.Insert,
                ReportingSqlParameters.Create(
                    ("Id", dataSourceId),
                    ("TenantId", request.TenantId),
                    ("Name", request.Name.Trim()),
                    ("ProviderKey", request.ProviderKey.Trim()),
                    ("ServerHost", request.ServerHost.Trim()),
                    ("Port", request.Port),
                    ("DatabaseName", request.DatabaseName.Trim()),
                    ("Username", request.Username.Trim()),
                    ("PasswordProtected", secretProtector.Protect(request.Password.Trim())),
                    ("TrustServerCertificate", request.TrustServerCertificate),
                    ("IsEnabled", request.IsEnabled),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", null),
                    ("Version", 1)),
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(dataSourceId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<ReportingDataSourceResponse>> UpdateCoreAsync(
        Guid dataSourceId,
        UpdateReportingDataSourceRequest request,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<ReportingDataSourceRecord>(
                ReportingDataSourceSql.FindById,
                ReportingSqlParameters.Create(("DataSourceId", dataSourceId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return NotFoundDetail();
        }

        var validationMessage = ReportingDataSourceFieldValidator.ValidateMetadata(
            request.Name,
            request.ProviderKey,
            request.ServerHost,
            request.Port,
            request.DatabaseName,
            request.Username);
        if (validationMessage is not null)
        {
            return ValidationFailure<ReportingDataSourceResponse>(validationMessage);
        }

        var protectedPassword = string.IsNullOrWhiteSpace(request.Password)
            ? current.PasswordProtected
            : secretProtector.Protect(request.Password.Trim());
        if (string.IsNullOrWhiteSpace(protectedPassword))
        {
            return Result<ReportingDataSourceResponse>.Failure(new Error(
                ReportingErrorCodes.DataSourcePasswordRequired,
                "Password must remain configured for a reporting data source.",
                ErrorType.Validation));
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                ReportingDataSourceSql.Update,
                ReportingSqlParameters.Create(
                    ("DataSourceId", dataSourceId),
                    ("Name", request.Name.Trim()),
                    ("ProviderKey", request.ProviderKey.Trim()),
                    ("ServerHost", request.ServerHost.Trim()),
                    ("Port", request.Port),
                    ("DatabaseName", request.DatabaseName.Trim()),
                    ("Username", request.Username.Trim()),
                    ("PasswordProtected", protectedPassword),
                    ("TrustServerCertificate", request.TrustServerCertificate),
                    ("IsEnabled", request.IsEnabled),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return ConcurrencyFailure<ReportingDataSourceResponse>();
        }

        return await queries.GetByIdAsync(dataSourceId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<ReportingDataSourceResponse>> DisableCoreAsync(
        Guid dataSourceId,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<ReportingDataSourceRecord>(
                ReportingDataSourceSql.FindById,
                ReportingSqlParameters.Create(("DataSourceId", dataSourceId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return NotFoundDetail();
        }

        var affected = await commandExecutor.ExecuteAsync(
                ReportingDataSourceSql.Disable,
                ReportingSqlParameters.Create(
                    ("DataSourceId", dataSourceId),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", current.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return ConcurrencyFailure<ReportingDataSourceResponse>();
        }

        return await queries.GetByIdAsync(dataSourceId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<bool>> DeleteCoreAsync(
        Guid dataSourceId,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<ReportingDataSourceRecord>(
                ReportingDataSourceSql.FindById,
                ReportingSqlParameters.Create(("DataSourceId", dataSourceId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return Result<bool>.Failure(new Error(
                ReportingErrorCodes.DataSourceNotFound,
                "The reporting data source was not found.",
                ErrorType.NotFound));
        }

        await commandExecutor.ExecuteAsync(
                ReportingDataSourceSql.Delete,
                ReportingSqlParameters.Create(("DataSourceId", dataSourceId)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<bool>.Success(true);
    }

    private async Task<Result<bool>> ValidateTenantScopeAsync(
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId is null)
        {
            return Result<bool>.Success(true);
        }

        var exists = await activeTenants.IsActiveTenantAsync(tenantId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (!exists)
        {
            return Result<bool>.Failure(new Error(
                ReportingErrorCodes.DataSourceInvalid,
                "The specified tenant does not exist or is not active.",
                ErrorType.Validation));
        }

        return Result<bool>.Success(true);
    }

    private static Result<T> ValidationFailure<T>(string message) =>
        Result<T>.Failure(new Error(
            ReportingErrorCodes.DataSourceInvalid,
            message,
            ErrorType.Validation));

    private static Result<T> ConcurrencyFailure<T>() =>
        Result<T>.Failure(new Error(
            ReportingErrorCodes.DataSourceConcurrencyConflict,
            "The reporting data source was modified by another request.",
            ErrorType.Conflict));

    private static Result<ReportingDataSourceResponse> NotFoundDetail() =>
        Result<ReportingDataSourceResponse>.Failure(new Error(
            ReportingErrorCodes.DataSourceNotFound,
            "The reporting data source was not found.",
            ErrorType.NotFound));
}
