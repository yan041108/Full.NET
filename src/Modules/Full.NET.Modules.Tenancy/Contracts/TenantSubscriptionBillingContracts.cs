namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>发布套餐报价；权益只允许选择已登记的功能目录，不接受任意权限码。</summary>
/// <param name="PaymentTenantId">收款租户。</param>
/// <param name="MerchantConfigId">收款商户配置。</param>
/// <param name="AmountMinor">最小货币单位金额。</param>
/// <param name="Currency">规范大写币种。</param>
/// <param name="SeatLimit">席位限额。</param>
/// <param name="StorageLimit">存储字节限额。</param>
/// <param name="DurationDays">本次价格对应的有效天数。</param>
/// <param name="EntitlementIds">已登记的功能权益。</param>
/// <param name="IsActive">是否接受新购买。</param>
/// <param name="Version">已见报价版本，首次创建为零。</param>
public sealed record UpsertTenantPackageOfferRequest(Guid PaymentTenantId, Guid MerchantConfigId, long AmountMinor,
    string Currency, long SeatLimit, long StorageLimit, int DurationDays, IReadOnlyList<Guid> EntitlementIds, bool IsActive, int Version);

/// <summary>套餐报价及当前允许的权益；购买时服务端另存不可变快照。</summary>
/// <param name="PackageId">所属套餐。</param>
/// <param name="PaymentTenantId">收款租户。</param>
/// <param name="MerchantConfigId">收款商户。</param>
/// <param name="AmountMinor">最小单位金额。</param>
/// <param name="Currency">规范币种。</param>
/// <param name="SeatLimit">席位上限。</param>
/// <param name="StorageLimit">存储字节上限。</param>
/// <param name="DurationDays">价格对应的有效天数。</param>
/// <param name="EntitlementIds">功能目录标识。</param>
/// <param name="IsActive">是否可购买。</param>
/// <param name="Version">报价版本。</param>
public sealed record TenantPackageOfferResponse(Guid PackageId, Guid PaymentTenantId, Guid MerchantConfigId, long AmountMinor,
    string Currency, long SeatLimit, long StorageLimit, int DurationDays, IReadOnlyList<Guid> EntitlementIds, bool IsActive, int Version);

/// <summary>按服务端报价创建付费订阅；不包含可被覆盖的金额和商户。</summary>
/// <param name="PackageId">目标套餐。</param>
/// <param name="OfferVersion">用户确认的报价版本。</param>
/// <param name="EffectiveFromUtc">明确生效时间。</param>
/// <param name="IdempotencyKey">调用方固定请求身份。</param>
public sealed record CreatePaidTenantSubscriptionRequest(Guid PackageId, int OfferVersion,
    DateTimeOffset EffectiveFromUtc, Guid IdempotencyKey)
{
    /// <summary>提前续期所依据的原订阅，必须与来源版本同时提供。</summary>
    public Guid? RenewalOfSubscriptionId { get; init; }
    /// <summary>确认提案时见到的原订阅版本；创建时在租户根锁内复核。</summary>
    public int? RenewalSourceVersion { get; init; }
}

/// <summary>同套餐续期提案；固定下一周期起点，创建时仍须重新校验来源与报价。</summary>
/// <param name="TenantId">权益受益企业。</param>
/// <param name="SourceSubscriptionId">已履约的当前周期。</param>
/// <param name="SourceVersion">原订阅版本。</param>
/// <param name="EffectiveFromUtc">固定为原周期结束时间。</param>
/// <param name="Offer">当前发布的同套餐报价。</param>
/// <param name="ReadAtUtc">服务器读取时间。</param>
public sealed record TenantSubscriptionRenewalPreviewResponse(Guid TenantId, Guid SourceSubscriptionId,
    int SourceVersion, DateTimeOffset EffectiveFromUtc, TenantPackageOfferResponse Offer, DateTimeOffset ReadAtUtc);
