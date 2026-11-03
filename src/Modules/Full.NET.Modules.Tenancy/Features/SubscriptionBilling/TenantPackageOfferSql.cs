using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Tenancy.Features.SubscriptionBilling;

/// <summary>报价更新先锁定版本，再替换本模块映射；冻结报价使用同一行锁保护金额和权益的一致视图。</summary>
internal static class TenantPackageOfferSql
{
    internal const string Columns = "Id,PaymentTenantId,MerchantConfigId,AmountMinor,Currency,SeatLimit,StorageLimit,DurationDays,IsActive,Version";
    internal static readonly SqlStatement Find = new("tenancy.package_offer.find", $"SELECT {Columns} FROM fn_tenancy_package_offer WHERE Id=@Id", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReadLockSqlServer = new("tenancy.package_offer.read_lock.sql_server",
        $"SELECT {Columns} FROM fn_tenancy_package_offer WITH (UPDLOCK,HOLDLOCK) WHERE Id=@Id", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReadLockMySql = new("tenancy.package_offer.read_lock.my_sql",
        $"SELECT {Columns} FROM fn_tenancy_package_offer WHERE Id=@Id FOR UPDATE", SqlDataScope.HostOnly);
    internal static readonly SqlStatement LockSqlServer = new("tenancy.package_offer.lock.sql_server",
        $"SELECT {Columns} FROM fn_tenancy_package_offer WITH (UPDLOCK,HOLDLOCK) WHERE Id=@Id AND Version=@Version AND IsActive=1", SqlDataScope.HostOnly);
    internal static readonly SqlStatement LockMySql = new("tenancy.package_offer.lock.my_sql",
        $"SELECT {Columns} FROM fn_tenancy_package_offer WHERE Id=@Id AND Version=@Version AND IsActive=1 FOR UPDATE", SqlDataScope.HostOnly);
    internal static readonly SqlStatement Insert = new("tenancy.package_offer.insert", """
        INSERT INTO fn_tenancy_package_offer (Id,PaymentTenantId,MerchantConfigId,AmountMinor,Currency,SeatLimit,StorageLimit,DurationDays,IsActive,CreatedAtUtc,UpdatedAtUtc,Version)
        VALUES (@Id,@PaymentTenantId,@MerchantConfigId,@AmountMinor,@Currency,@SeatLimit,@StorageLimit,@DurationDays,@IsActive,@Now,@Now,1)
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement Update = new("tenancy.package_offer.update", """
        UPDATE fn_tenancy_package_offer SET PaymentTenantId=@PaymentTenantId,MerchantConfigId=@MerchantConfigId,
          AmountMinor=@AmountMinor,Currency=@Currency,SeatLimit=@SeatLimit,StorageLimit=@StorageLimit,DurationDays=@DurationDays,
          IsActive=@IsActive,UpdatedAtUtc=@Now,Version=Version+1 WHERE Id=@Id AND Version=@Version
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListEntitlements = new("tenancy.package_offer.list_entitlements", """
        SELECT catalog.Id,catalog.Code,catalog.EntitlementType,catalog.IsActive
        FROM fn_tenancy_offer_entitlement link INNER JOIN fn_tenancy_entitlement_catalog catalog ON catalog.Id=link.EntitlementId
        WHERE link.OfferId=@Id ORDER BY catalog.Id
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindEntitlements = new("tenancy.package_offer.find_entitlements",
        "SELECT Id,Code,EntitlementType,IsActive FROM fn_tenancy_entitlement_catalog WHERE Id IN @Ids", SqlDataScope.HostOnly);
    internal static readonly SqlStatement DeleteEntitlements = new("tenancy.package_offer.delete_entitlements", "DELETE FROM fn_tenancy_offer_entitlement WHERE OfferId=@Id", SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertEntitlement = new("tenancy.package_offer.insert_entitlement", """
        INSERT INTO fn_tenancy_offer_entitlement (Id,OfferId,EntitlementId,CreatedAtUtc) VALUES (@Id,@OfferId,@EntitlementId,@Now)
        """, SqlDataScope.HostOnly);
}

/// <summary>报价固定列序投影。</summary>
internal sealed record TenantPackageOfferRecord(Guid Id, Guid PaymentTenantId, Guid MerchantConfigId, long AmountMinor,
    string Currency, long SeatLimit, long StorageLimit, int DurationDays, bool IsActive, int Version);

/// <summary>冻结功能码时使用的目录权威信息。</summary>
internal sealed record SubscriptionEntitlementRecord(Guid Id, string Code, string EntitlementType, bool IsActive);
