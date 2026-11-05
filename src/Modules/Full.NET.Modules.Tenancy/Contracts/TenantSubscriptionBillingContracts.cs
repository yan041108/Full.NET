using System.Text.Json.Serialization;
using Full.NET.Hosting.Serialization;

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
public sealed record UpsertTenantPackageOfferRequest(Guid PaymentTenantId, Guid MerchantConfigId,
    [property: JsonConverter(typeof(StringInt64JsonConverter))] long AmountMinor,
    string Currency, [property: JsonConverter(typeof(StringInt64JsonConverter))] long SeatLimit,
    [property: JsonConverter(typeof(StringInt64JsonConverter))] long StorageLimit,
    int DurationDays, IReadOnlyList<Guid> EntitlementIds, bool IsActive, int Version);

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
public sealed record TenantPackageOfferResponse(Guid PackageId, Guid PaymentTenantId, Guid MerchantConfigId,
    [property: JsonConverter(typeof(StringInt64JsonConverter))] long AmountMinor,
    string Currency, [property: JsonConverter(typeof(StringInt64JsonConverter))] long SeatLimit,
    [property: JsonConverter(typeof(StringInt64JsonConverter))] long StorageLimit,
    int DurationDays, IReadOnlyList<Guid> EntitlementIds, bool IsActive, int Version);

/// <summary>创建付费账单的 HTTP 回执；金额使用精确字符串，进程内支付 Port 保留 long 业务契约。</summary>
/// <param name="SubscriptionId">本次独立账单身份。</param>
/// <param name="TenantId">权益受益企业。</param>
/// <param name="PaymentTenantId">收款租户。</param>
/// <param name="PaymentOperationId">原支付幂等身份。</param>
/// <param name="MerchantConfigId">冻结商户身份。</param>
/// <param name="PackageId">冻结套餐身份。</param>
/// <param name="OfferVersion">冻结报价版本。</param>
/// <param name="AmountMinor">精确最小货币单位金额。</param>
/// <param name="Currency">冻结币种。</param>
/// <param name="EffectiveFromUtc">新周期起点。</param>
/// <param name="EffectiveToUtc">新周期终点。</param>
/// <param name="Subject">冻结商品名称。</param>
public sealed record CreatePaidTenantSubscriptionResponse(Guid SubscriptionId, Guid TenantId, Guid PaymentTenantId,
    Guid PaymentOperationId, Guid MerchantConfigId, Guid PackageId, int OfferVersion,
    [property: JsonConverter(typeof(StringInt64JsonConverter))] long AmountMinor,
    string Currency, DateTimeOffset EffectiveFromUtc, DateTimeOffset EffectiveToUtc, string Subject)
{
    /// <summary>原续期来源；普通创建为空。</summary>
    public Guid? RenewalOfSubscriptionId { get; init; }
    /// <summary>创建时核对的来源版本。</summary>
    public int? RenewalSourceVersion { get; init; }

    internal static CreatePaidTenantSubscriptionResponse From(TenantSubscriptionPaymentTerms value) => new(
        value.SubscriptionId, value.TenantId, value.PaymentTenantId, value.PaymentOperationId, value.MerchantConfigId,
        value.PackageId, value.OfferVersion, value.AmountMinor, value.Currency, value.EffectiveFromUtc, value.EffectiveToUtc, value.Subject)
    { RenewalOfSubscriptionId = value.RenewalOfSubscriptionId, RenewalSourceVersion = value.RenewalSourceVersion };
}

/// <summary>按服务端报价创建付费订阅；不包含可被覆盖的金额和商户。</summary>
/// <param name="PackageId">目标套餐。</param>
/// <param name="OfferVersion">用户确认的报价版本。</param>
/// <param name="EffectiveFromUtc">明确生效时间。</param>
/// <param name="IdempotencyKey">调用方固定请求身份。</param>
public sealed record CreatePaidTenantSubscriptionRequest(Guid PackageId, int OfferVersion,
    DateTimeOffset EffectiveFromUtc, Guid IdempotencyKey)
{
    /// <summary>下一周期所依据的原订阅，必须与来源版本同时提供。</summary>
    public Guid? RenewalOfSubscriptionId { get; init; }
    /// <summary>确认提案时见到的原订阅版本；创建时在租户根锁内复核。</summary>
    public int? RenewalSourceVersion { get; init; }
}

/// <summary>下一周期提案；固定起点并区分原套餐与目标报价，创建时仍须重新校验。</summary>
/// <param name="TenantId">权益受益企业。</param>
/// <param name="SourceSubscriptionId">已履约的当前周期。</param>
/// <param name="SourcePackageId">原周期套餐；允许退出销售后显式选择其他活动套餐。</param>
/// <param name="SourceVersion">原订阅版本。</param>
/// <param name="EffectiveFromUtc">固定为原周期结束时间。</param>
/// <param name="Offer">当前发布的同套餐报价。</param>
/// <param name="ReadAtUtc">服务器读取时间。</param>
public sealed record TenantSubscriptionRenewalPreviewResponse(Guid TenantId, Guid SourceSubscriptionId, Guid SourcePackageId,
    int SourceVersion, DateTimeOffset EffectiveFromUtc, TenantPackageOfferResponse Offer, DateTimeOffset ReadAtUtc);

/// <summary>原冻结账单的只读快照；历史接收阶段不替代当前资金或权益判断。</summary>
/// <param name="SubscriptionId">账单所属订阅。</param>
/// <param name="TenantId">权益受益企业。</param>
/// <param name="PackageId">冻结套餐。</param>
/// <param name="Subject">冻结商品名称。</param>
/// <param name="OfferVersion">冻结报价版本。</param>
/// <param name="AmountMinor">最小货币单位金额的精确十进制字符串。</param>
/// <param name="Currency">冻结币种。</param>
/// <param name="SeatLimit">冻结席位上限的精确十进制字符串。</param>
/// <param name="StorageLimit">冻结存储字节上限的精确十进制字符串。</param>
/// <param name="EffectiveFromUtc">冻结周期起点。</param>
/// <param name="EffectiveToUtc">冻结周期终点。</param>
/// <param name="Phase">Tenancy 账单事实接收阶段。</param>
/// <param name="RenewalOfSubscriptionId">原续期来源，普通购买为空。</param>
/// <param name="RenewalSourceVersion">创建时核验的来源版本。</param>
/// <param name="PaymentOrderId">已接收付款事实的订单身份；不是下单指令。</param>
/// <param name="ReadAtUtc">服务器读取时间。</param>
public sealed record TenantSubscriptionInvoiceResponse(Guid SubscriptionId, Guid TenantId, Guid PackageId,
    string Subject, int OfferVersion, string AmountMinor, string Currency, string SeatLimit, string StorageLimit,
    DateTimeOffset EffectiveFromUtc, DateTimeOffset EffectiveToUtc, string Phase,
    Guid? RenewalOfSubscriptionId, int? RenewalSourceVersion, Guid? PaymentOrderId, DateTimeOffset ReadAtUtc);
