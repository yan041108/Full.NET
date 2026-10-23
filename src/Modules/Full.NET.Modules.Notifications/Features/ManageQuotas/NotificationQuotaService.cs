using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Features.ManageQuotas;

/// <summary>
/// 维护发送配额政策：按 Provider、租户、Producer、Scene 声明有界额度与窗口长度。
/// </summary>
/// <remarks>
/// 政策只声明上限，真实占用由发送链路的窗口计数器原子预留。缺失任一维度声明时发送失败关闭，
/// 不允许按无界并发处理。所有写操作以 Version 做 CAS，读操作按可信租户作用域过滤。
/// </remarks>
internal sealed class NotificationQuotaService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<NotificationQuotaPolicyResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                NotificationPlatformSql.CountQuotaPoliciesForScope,
                NotificationPlatformSqlParameters.Create(("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? NotificationPlatformSql.ListQuotaPoliciesMySql
            : NotificationPlatformSql.ListQuotaPoliciesSqlServer;
        var rows = await queryExecutor.QueryAsync<NotificationQuotaPolicyRecord>(
                statement,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("Offset", offset),
                    ("PageSize", pageSize)),
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<NotificationQuotaPolicyResponse>>.Success(
            new PagedResult<NotificationQuotaPolicyResponse>(items, page, pageSize, total));
    }

    public async Task<Result<NotificationQuotaPolicyResponse>> GetByIdAsync(
        Guid quotaPolicyId,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var record = await queryExecutor.QuerySingleOrDefaultAsync<NotificationQuotaPolicyRecord>(
                NotificationPlatformSql.FindQuotaPolicyById,
                NotificationPlatformSqlParameters.Create(
                    ("Id", quotaPolicyId),
                    ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? NotFound()
            : Result<NotificationQuotaPolicyResponse>.Success(Map(record));
    }

    /// <summary>按维度聚合当前作用域的预留台账，定位受配额阻塞与等待对账的受影响范围。</summary>
    public async Task<Result<IReadOnlyList<NotificationQuotaOutstandingResponse>>> GetOutstandingAsync(
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var rows = await queryExecutor.QueryAsync<NotificationQuotaOutstandingRecord>(
                databaseOptions.Value.Provider == DatabaseProvider.MySql
                    ? NotificationPlatformSql.CountQuotaOutstandingByScope
                    : NotificationPlatformSql.CountQuotaOutstandingByScopeSqlServer,
                NotificationPlatformSqlParameters.Create(("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<NotificationQuotaOutstandingResponse> items = rows
            .Select(record => new NotificationQuotaOutstandingResponse(
                record.DimensionKey,
                record.StateKey,
                record.ReservationCount))
            .ToArray();
        return Result<IReadOnlyList<NotificationQuotaOutstandingResponse>>.Success(items);
    }

    public Task<Result<NotificationQuotaPolicyResponse>> CreateAsync(
        Guid actorUserId,
        CreateNotificationQuotaPolicyRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => CreateCoreAsync(actorUserId, request, token),
            cancellationToken);

    public Task<Result<NotificationQuotaPolicyResponse>> UpdateAsync(
        Guid quotaPolicyId,
        UpdateNotificationQuotaPolicyRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => UpdateCoreAsync(quotaPolicyId, request, token),
            cancellationToken);

    public Task<Result<NotificationQuotaPolicyResponse>> SetEnabledAsync(
        Guid quotaPolicyId,
        SetNotificationQuotaPolicyEnabledRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => SetEnabledCoreAsync(quotaPolicyId, request, token),
            cancellationToken);

    private async Task<Result<NotificationQuotaPolicyResponse>> CreateCoreAsync(
        Guid actorUserId,
        CreateNotificationQuotaPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var declared = NormalizeDeclaration(
            request.DimensionKey,
            request.DimensionValue,
            request.WindowSeconds,
            request.MaxCount);
        if (!declared.IsSuccess)
        {
            return Result<NotificationQuotaPolicyResponse>.Failure(declared.Error!);
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var quotaPolicyId = idGenerator.NewId();
        var insert = scope.IsHost
            ? NotificationPlatformSql.InsertQuotaPolicyHost
            : NotificationPlatformSql.InsertQuotaPolicyTenant;
        var affected = await commandExecutor.ExecuteAsync(
                insert,
                NotificationPlatformSqlParameters.Create(
                    ("Id", quotaPolicyId),
                    ("TenantId", scope.TenantId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("DimensionKey", declared.Value!.DimensionKey),
                    ("DimensionValue", declared.Value.DimensionValue),
                    ("WindowSeconds", declared.Value.WindowSeconds),
                    ("MaxCount", declared.Value.MaxCount),
                    ("CreatedById", actorUserId),
                    ("Now", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 0
            ? Result<NotificationQuotaPolicyResponse>.Failure(new Error(
                NotificationsErrorCodes.QuotaPolicyKeyConflict,
                "A quota policy for this dimension and value already exists.",
                ErrorType.Conflict))
            : await GetByIdAsync(quotaPolicyId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<NotificationQuotaPolicyResponse>> UpdateCoreAsync(
        Guid quotaPolicyId,
        UpdateNotificationQuotaPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<NotificationQuotaPolicyRecord>(
                NotificationPlatformSql.FindQuotaPolicyById,
                NotificationPlatformSqlParameters.Create(
                    ("Id", quotaPolicyId),
                    ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        if (request.WindowSeconds <= 0 || request.MaxCount <= 0)
        {
            return ValidationFailed();
        }

        var affected = await commandExecutor.ExecuteAsync(
                NotificationPlatformSql.UpdateQuotaPolicy,
                NotificationPlatformSqlParameters.Create(
                    ("Id", quotaPolicyId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("WindowSeconds", request.WindowSeconds),
                    ("MaxCount", request.MaxCount),
                    ("Now", clock.UtcNow),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 0
            ? ConcurrencyConflict()
            : await GetByIdAsync(quotaPolicyId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<NotificationQuotaPolicyResponse>> SetEnabledCoreAsync(
        Guid quotaPolicyId,
        SetNotificationQuotaPolicyEnabledRequest request,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<NotificationQuotaPolicyRecord>(
                NotificationPlatformSql.FindQuotaPolicyById,
                NotificationPlatformSqlParameters.Create(
                    ("Id", quotaPolicyId),
                    ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var affected = await commandExecutor.ExecuteAsync(
                NotificationPlatformSql.SetQuotaPolicyEnabled,
                NotificationPlatformSqlParameters.Create(
                    ("Id", quotaPolicyId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("IsEnabled", request.IsEnabled),
                    ("Now", clock.UtcNow),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 0
            ? ConcurrencyConflict()
            : await GetByIdAsync(quotaPolicyId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>校验配额声明：维度必须在闭合集合内，取值非空，窗口与上限必须为正。</summary>
    private static Result<QuotaDeclaration> NormalizeDeclaration(
        string? dimensionKey,
        string? dimensionValue,
        int windowSeconds,
        int maxCount)
    {
        var dimension = dimensionKey?.Trim() ?? string.Empty;
        if (!NotificationQuotaDimensions.All.Contains(dimension))
        {
            return Result<QuotaDeclaration>.Failure(new Error(
                NotificationsErrorCodes.QuotaPolicyValidationFailed,
                "The quota dimension is invalid.",
                ErrorType.Validation));
        }

        var value = dimensionValue?.Trim() ?? string.Empty;
        if (value.Length == 0 || value.Length > 128)
        {
            return Result<QuotaDeclaration>.Failure(new Error(
                NotificationsErrorCodes.QuotaPolicyValidationFailed,
                "The quota dimension value is invalid.",
                ErrorType.Validation));
        }

        if (windowSeconds <= 0 || maxCount <= 0)
        {
            return Result<QuotaDeclaration>.Failure(new Error(
                NotificationsErrorCodes.QuotaPolicyValidationFailed,
                "The quota window and limit must be positive.",
                ErrorType.Validation));
        }

        return Result<QuotaDeclaration>.Success(new QuotaDeclaration(dimension, value, windowSeconds, maxCount));
    }

    private static NotificationQuotaPolicyResponse Map(NotificationQuotaPolicyRecord record) =>
        new(
            record.Id,
            record.DimensionKey,
            record.DimensionValue,
            record.WindowSeconds,
            record.MaxCount,
            record.IsEnabled,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private static Result<NotificationQuotaPolicyResponse> NotFound() =>
        Result<NotificationQuotaPolicyResponse>.Failure(new Error(
            NotificationsErrorCodes.QuotaPolicyNotFound,
            "The send quota policy was not found.",
            ErrorType.NotFound));

    private static Result<NotificationQuotaPolicyResponse> ConcurrencyConflict() =>
        Result<NotificationQuotaPolicyResponse>.Failure(new Error(
            NotificationsErrorCodes.QuotaPolicyConcurrencyConflict,
            "The send quota policy changed concurrently.",
            ErrorType.Conflict));

    private static Result<NotificationQuotaPolicyResponse> ValidationFailed() =>
        Result<NotificationQuotaPolicyResponse>.Failure(new Error(
            NotificationsErrorCodes.QuotaPolicyValidationFailed,
            "The send quota policy declaration is invalid.",
            ErrorType.Validation));

    /// <summary>已校验的配额声明中间值。</summary>
    private sealed record QuotaDeclaration(
        string DimensionKey,
        string DimensionValue,
        int WindowSeconds,
        int MaxCount);
}
