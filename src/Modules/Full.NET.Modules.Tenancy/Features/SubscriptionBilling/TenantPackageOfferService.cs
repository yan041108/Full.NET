using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageHostTenantPackages;
using Full.NET.Modules.Tenancy.Persistence;
using Full.NET.Modules.Tenancy.Auditing;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Tenancy.Features.SubscriptionBilling;

/// <summary>Host 管理报价，购买方只能引用服务端版本，不能临时替换资金与权益条款。</summary>
internal sealed class TenantPackageOfferService(IQueryExecutor queries, ICommandExecutor commands,
    ICommandTransaction transaction, IClock clock, IIdGenerator ids, ITransactionalDomainAuditWriter<TenancyDomainAuditWrite> audit,
    IOptions<DatabaseOptions> database)
{
    internal Task<Result<TenantPackageOfferResponse>> GetAsync(Guid packageId, CancellationToken token) => transaction.ExecuteResultAsync(async local =>
    {
        // 报价行和权益集合必须来自同一版本，避免用户确认混合条款。
        var offer = await queries.QuerySingleOrDefaultAsync<TenantPackageOfferRecord>(database.Value.Provider == DatabaseProvider.MySql
                ? TenantPackageOfferSql.ReadLockMySql : TenantPackageOfferSql.ReadLockSqlServer,
            TenancySqlParameters.Create(("Id", packageId)), local).ConfigureAwait(false);
        if (offer is null) return Failure("tenancy.package_offer.not_found", ErrorType.NotFound);
        var entitlements = await queries.QueryAsync<SubscriptionEntitlementRecord>(TenantPackageOfferSql.ListEntitlements,
            TenancySqlParameters.Create(("Id", packageId)), local).ConfigureAwait(false);
        return Result<TenantPackageOfferResponse>.Success(Map(offer, entitlements.Select(e => e.Id).ToArray()));
    }, token);

    internal Task<Result<TenantPackageOfferResponse>> SaveAsync(Guid packageId, UpsertTenantPackageOfferRequest request, CancellationToken token,
        Guid? actorUserId = null)
    {
        var currency = request.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (packageId == Guid.Empty || request.PaymentTenantId == Guid.Empty || request.MerchantConfigId == Guid.Empty
            || request.Version < 0 || request.AmountMinor <= 0 || request.SeatLimit < 0 || request.StorageLimit < 0
            || request.DurationDays is < 1 or > 3660
            || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z')
            || request.EntitlementIds is null || request.EntitlementIds.Count > 100
            || request.EntitlementIds.Contains(Guid.Empty) || request.EntitlementIds.Distinct().Count() != request.EntitlementIds.Count)
            return Task.FromResult(Failure("tenancy.package_offer.invalid", ErrorType.Validation));
        return transaction.ExecuteResultAsync(async local =>
        {
            var package = await queries.QuerySingleOrDefaultAsync<TenantPackageIdentityRecord>(TenantPackageSql.FindPackageById,
                TenancySqlParameters.Create(("PackageId", packageId)), local).ConfigureAwait(false);
            if (package is null || !package.IsActive) return Failure(TenancyErrorCodes.PackageInactive, ErrorType.BusinessRule);
            var entitlements = request.EntitlementIds.Count == 0 ? []
                : await queries.QueryAsync<SubscriptionEntitlementRecord>(TenantPackageOfferSql.FindEntitlements,
                    TenancySqlParameters.Create(("Ids", request.EntitlementIds.ToArray())), local).ConfigureAwait(false);
            if (entitlements.Count != request.EntitlementIds.Count || entitlements.Any(e => !e.IsActive || e.EntitlementType != TenantEntitlementTypes.Feature))
                return Failure(TenancyErrorCodes.EntitlementCodeInvalid, ErrorType.Validation);
            var parameters = TenancySqlParameters.Create(("Id", packageId), ("PaymentTenantId", request.PaymentTenantId),
                ("MerchantConfigId", request.MerchantConfigId), ("AmountMinor", request.AmountMinor), ("Currency", currency),
                ("SeatLimit", request.SeatLimit), ("StorageLimit", request.StorageLimit), ("DurationDays", request.DurationDays), ("IsActive", request.IsActive),
                ("Version", request.Version), ("Now", clock.UtcNow));
            if (await commands.ExecuteAsync(request.Version == 0 ? TenantPackageOfferSql.Insert : TenantPackageOfferSql.Update,
                parameters, local).ConfigureAwait(false) != 1) return Failure("tenancy.package_offer.version_conflict", ErrorType.Conflict);
            await commands.ExecuteAsync(TenantPackageOfferSql.DeleteEntitlements, TenancySqlParameters.Create(("Id", packageId)), local).ConfigureAwait(false);
            foreach (var entitlement in entitlements)
                if (await commands.ExecuteAsync(TenantPackageOfferSql.InsertEntitlement, TenancySqlParameters.Create(("Id", ids.NewId()),
                    ("OfferId", packageId), ("EntitlementId", entitlement.Id), ("Now", clock.UtcNow)), local).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("Offer entitlement snapshot was not persisted.");
            await audit.WriteAsync(new(TenancyDomainAuditActionKeys.PackageOfferUpdated, packageId, null,
                TenancyDomainAuditOutcomes.Success, actorUserId, null, null), local).ConfigureAwait(false);
            return Result<TenantPackageOfferResponse>.Success(new(packageId, request.PaymentTenantId, request.MerchantConfigId,
                request.AmountMinor, currency, request.SeatLimit, request.StorageLimit, request.DurationDays, request.EntitlementIds.ToArray(), request.IsActive, request.Version + 1));
        }, token);
    }

    private static TenantPackageOfferResponse Map(TenantPackageOfferRecord o, IReadOnlyList<Guid> entitlements) =>
        new(o.Id, o.PaymentTenantId, o.MerchantConfigId, o.AmountMinor, o.Currency, o.SeatLimit, o.StorageLimit, o.DurationDays, entitlements, o.IsActive, o.Version);
    private static Result<TenantPackageOfferResponse> Failure(string code, ErrorType type) =>
        Result<TenantPackageOfferResponse>.Failure(new Error(code, "The package offer is unavailable or changed.", type));
}
