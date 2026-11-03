#if FULLNET_AOT_COMPILE
using Full.NET.Data.Dapper;
using Full.NET.Modules.Payments.Features.SubscriptionBilling;

namespace Full.NET.Modules.Payments.Persistence;

/// <summary>资金与订阅恢复可达路径采用显式列序，避免 Worker 或 API 原生路径依赖反射物化。</summary>
internal sealed class PaymentsDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<SubscriptionFulfillmentRecord>(r => new(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetGuid(3), r.GetGuid(4),
            r.GetInt64(5), r.GetString(6), AotDataReaderExtensions.ReadDateTimeOffset(r, 7), AotDataReaderExtensions.ReadDateTimeOffset(r, 8),
            AotDataReaderExtensions.ReadNullableGuid(r, 9), AotDataReaderExtensions.ReadNullableString(r, 10),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(r, 11), AotDataReaderExtensions.ReadNullableGuid(r, 12),
            r.GetInt64(13), r.GetString(14), AotDataReaderExtensions.ReadNullableString(r, 15), AotDataReaderExtensions.ReadDateTimeOffset(r, 16),
            AotDataReaderExtensions.ReadNullableGuid(r, 17), AotDataReaderExtensions.ReadNullableDateTimeOffset(r, 18),
            AotDataReaderExtensions.ReadInt32(r, 19), AotDataReaderExtensions.ReadDateTimeOffset(r, 20),
            AotDataReaderExtensions.ReadDateTimeOffset(r, 21), AotDataReaderExtensions.ReadInt32(r, 22)));
        registrar.Register<SubscriptionPaymentAuthorityRecord>(r => new(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2),
            AotDataReaderExtensions.ReadNullableGuid(r, 3), r.GetInt64(4), r.GetString(5), r.GetString(6),
            AotDataReaderExtensions.ReadNullableString(r, 7), AotDataReaderExtensions.ReadNullableDateTimeOffset(r, 8)));
        registrar.Register<SubscriptionRefundAuthorityRecord>(r => new(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetGuid(3),
            r.GetInt64(4), r.GetString(5), r.GetString(6), AotDataReaderExtensions.ReadNullableString(r, 7)));
        registrar.Register<PaymentOrderRecord>(r => new()
        {
            Id=r.GetGuid(0), TenantId=r.GetGuid(1), MerchantConfigId=r.GetGuid(2), ChannelKey=r.GetString(3), OutTradeNo=r.GetString(4),
            TradeStateKey=r.GetString(5), AmountMinor=r.GetInt64(6), Currency=r.GetString(7), Subject=r.GetString(8),
            Description=AotDataReaderExtensions.ReadNullableString(r,9), CodeUrl=AotDataReaderExtensions.ReadNullableString(r,10),
            ProviderTransactionId=AotDataReaderExtensions.ReadNullableString(r,11), FailMessage=AotDataReaderExtensions.ReadNullableString(r,12),
            CreatedAtUtc=AotDataReaderExtensions.ReadDateTimeOffset(r,13), UpdatedAtUtc=AotDataReaderExtensions.ReadNullableDateTimeOffset(r,14),
            PaidAtUtc=AotDataReaderExtensions.ReadNullableDateTimeOffset(r,15), Version=AotDataReaderExtensions.ReadInt32(r,16),
            IdempotencyKey=AotDataReaderExtensions.ReadNullableGuid(r,17), RequestHash=AotDataReaderExtensions.ReadNullableString(r,18)
        });
        registrar.Register<PaymentMerchantConfigRecord>(r => new()
        {
            Id=r.GetGuid(0), TenantId=AotDataReaderExtensions.ReadNullableGuid(r,1), Name=r.GetString(2), ChannelKey=r.GetString(3),
            AppId=r.GetString(4), MerchantId=r.GetString(5), CertificateSerialNo=r.GetString(6), NotifyUrl=r.GetString(7),
            ReturnUrl=AotDataReaderExtensions.ReadNullableString(r,8), ApiV3KeyProtected=AotDataReaderExtensions.ReadNullableString(r,9),
            PrivateKeyProtected=AotDataReaderExtensions.ReadNullableString(r,10), IsDefault=AotDataReaderExtensions.ReadBoolean(r,11),
            IsEnabled=AotDataReaderExtensions.ReadBoolean(r,12), CreatedAtUtc=AotDataReaderExtensions.ReadDateTimeOffset(r,13),
            UpdatedAtUtc=AotDataReaderExtensions.ReadNullableDateTimeOffset(r,14), Version=AotDataReaderExtensions.ReadInt32(r,15)
        });
        registrar.Register<PaymentRefundRecord>(r => new()
        {
            Id=r.GetGuid(0), TenantId=r.GetGuid(1), OrderId=r.GetGuid(2), MerchantConfigId=r.GetGuid(3), OutTradeNo=r.GetString(4),
            OutRefundNo=r.GetString(5), RefundStateKey=r.GetString(6), AmountMinor=r.GetInt64(7), Currency=r.GetString(8), Reason=r.GetString(9),
            ProviderRefundId=AotDataReaderExtensions.ReadNullableString(r,10), FailMessage=AotDataReaderExtensions.ReadNullableString(r,11),
            CreatedAtUtc=AotDataReaderExtensions.ReadDateTimeOffset(r,12), UpdatedAtUtc=AotDataReaderExtensions.ReadNullableDateTimeOffset(r,13),
            CompletedAtUtc=AotDataReaderExtensions.ReadNullableDateTimeOffset(r,14), Version=AotDataReaderExtensions.ReadInt32(r,15)
        });
        registrar.Register<PaymentNotifyReceiptRecord>(r => new()
        {
            Id=r.GetGuid(0), MerchantConfigId=r.GetGuid(1), ProviderNotifyId=r.GetString(2), EventTypeKey=r.GetString(3),
            OutTradeNo=AotDataReaderExtensions.ReadNullableString(r,4), ProcessStatusKey=r.GetString(5), PayloadSummary=r.GetString(6),
            CreatedAtUtc=AotDataReaderExtensions.ReadDateTimeOffset(r,7)
        });
    }
}
#endif
