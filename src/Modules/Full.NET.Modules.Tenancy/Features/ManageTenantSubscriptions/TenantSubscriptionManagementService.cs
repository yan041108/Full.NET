using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageHostTenants;
using Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions;

internal sealed class TenantSubscriptionManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator,
    TenantHostPackageBinder packageBinder)
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.Ordinal)
    {
        TenantSubscriptionStatuses.Trial,
        TenantSubscriptionStatuses.Active,
        TenantSubscriptionStatuses.PastDue,
    };

    public Task<Result<TenantSubscriptionResponse>> CreateAsync(
        Guid tenantId,
        CreateTenantSubscriptionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(tenantId, request, token),
            cancellationToken);

    public Task<Result<TenantSubscriptionResponse>> CancelAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancelTenantSubscriptionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CancelCoreAsync(tenantId, subscriptionId, request, token),
            cancellationToken);

    internal static Result<string> ValidateStatus(string? status)
    {
        var normalized = status?.Trim() ?? string.Empty;
        if (!AllowedStatuses.Contains(normalized))
        {
            return Result<string>.Failure(new Error(
                TenancyErrorCodes.SubscriptionStatusInvalid,
                "Subscription status is invalid.",
                ErrorType.Validation));
        }

        return Result<string>.Success(normalized);
    }

    private async Task<Result<TenantSubscriptionResponse>> CreateCoreAsync(
        Guid tenantId,
        CreateTenantSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var statusValidation = ValidateStatus(request.Status);
        if (!statusValidation.IsSuccess)
        {
            return Result<TenantSubscriptionResponse>.Failure(statusValidation.Error!);
        }

        if (request.CurrentPeriodEndUtc <= request.CurrentPeriodStartUtc)
        {
            return Result<TenantSubscriptionResponse>.Failure(new Error(
                TenancyErrorCodes.SubscriptionPeriodInvalid,
                "Subscription period is invalid.",
                ErrorType.Validation));
        }

        var existingActive = (await queryExecutor.QueryAsync<TenantSubscriptionRecord>(
                    TenantSubscriptionSql.ListByTenant,
                    Tenancy.Persistence.TenancySqlParameters.Create(("TenantId", tenantId)),
                    cancellationToken)
                .ConfigureAwait(false))
            .Any(row => AllowedStatuses.Contains(row.Status));
        if (existingActive)
        {
            return Result<TenantSubscriptionResponse>.Failure(new Error(
                TenancyErrorCodes.SubscriptionStatusInvalid,
                "An active subscription already exists for this tenant.",
                ErrorType.Conflict));
        }

        var now = clock.UtcNow;
        var id = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
                TenantSubscriptionSql.Insert,
                Tenancy.Persistence.TenancySqlParameters.Create(
                    ("Id", id),
                    ("TenantId", tenantId),
                    ("PackageId", request.PackageId),
                    ("Status", statusValidation.Value),
                    ("TrialEndsAtUtc", request.TrialEndsAtUtc),
                    ("CurrentPeriodStartUtc", request.CurrentPeriodStartUtc),
                    ("CurrentPeriodEndUtc", request.CurrentPeriodEndUtc),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);

        if (request.PackageId is Guid packageId)
        {
            var bindResult = await packageBinder.BindActivePackageAsync(
                    tenantId,
                    packageId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!bindResult.IsSuccess)
            {
                return Result<TenantSubscriptionResponse>.Failure(bindResult.Error!);
            }
        }

        return Result<TenantSubscriptionResponse>.Success(
            new TenantSubscriptionResponse(
                id,
                tenantId,
                request.PackageId,
                statusValidation.Value!,
                request.TrialEndsAtUtc,
                request.CurrentPeriodStartUtc,
                request.CurrentPeriodEndUtc,
                null,
                1));
    }

    private async Task<Result<TenantSubscriptionResponse>> CancelCoreAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancelTenantSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var existing = (await queryExecutor.QueryAsync<TenantSubscriptionRecord>(
                    TenantSubscriptionSql.ListByTenant,
                    Tenancy.Persistence.TenancySqlParameters.Create(("TenantId", tenantId)),
                    cancellationToken)
                .ConfigureAwait(false))
            .FirstOrDefault(row => row.Id == subscriptionId);
        if (existing is null)
        {
            return Result<TenantSubscriptionResponse>.Failure(new Error(
                TenancyErrorCodes.SubscriptionNotFound,
                "Subscription was not found.",
                ErrorType.NotFound));
        }

        if (existing.Version != request.Version)
        {
            return Result<TenantSubscriptionResponse>.Failure(new Error(
                TenancyErrorCodes.SubscriptionVersionConflict,
                "Subscription version conflict.",
                ErrorType.Conflict));
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                TenantSubscriptionSql.Cancel,
                Tenancy.Persistence.TenancySqlParameters.Create(
                    ("Id", subscriptionId),
                    ("TenantId", tenantId),
                    ("Status", TenantSubscriptionStatuses.Cancelled),
                    ("CancelledAtUtc", now),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return Result<TenantSubscriptionResponse>.Failure(new Error(
                TenancyErrorCodes.SubscriptionVersionConflict,
                "Subscription version conflict.",
                ErrorType.Conflict));
        }

        return Result<TenantSubscriptionResponse>.Success(
            new TenantSubscriptionResponse(
                existing.Id,
                existing.TenantId,
                existing.PackageId,
                TenantSubscriptionStatuses.Cancelled,
                existing.TrialEndsAtUtc,
                existing.CurrentPeriodStartUtc,
                existing.CurrentPeriodEndUtc,
                now,
                existing.Version + 1));
    }
}
