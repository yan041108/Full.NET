using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Domain;
using Full.NET.Modules.Payments.Persistence;

namespace Full.NET.Modules.Payments.Features.ManageMerchantConfigs;

/// <summary>支付商户配置响应映射。</summary>
internal static class PaymentMerchantConfigMapper
{
    /// <summary>映射列表项（脱敏）。</summary>
    /// <param name="row">持久化行。</param>
    /// <returns>列表响应。</returns>
    public static PaymentMerchantConfigListItem MapListItem(PaymentMerchantConfigRecord row) =>
        new(
            row.Id,
            row.TenantId,
            row.Name,
            row.ChannelKey,
            PaymentMerchantMasking.MaskIdentifier(row.AppId),
            PaymentMerchantMasking.MaskIdentifier(row.MerchantId),
            PaymentMerchantMasking.MaskIdentifier(row.CertificateSerialNo),
            PaymentMerchantMasking.MaskNotifyUrl(row.NotifyUrl),
            HasApiV3Key(row),
            HasPrivateKey(row),
            row.IsDefault,
            row.IsEnabled,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    /// <summary>映射详情（不回显受保护密钥）。</summary>
    /// <param name="row">持久化行。</param>
    /// <returns>详情响应。</returns>
    public static PaymentMerchantConfigResponse MapDetail(PaymentMerchantConfigRecord row) =>
        new(
            row.Id,
            row.TenantId,
            row.Name,
            row.ChannelKey,
            row.AppId,
            row.MerchantId,
            row.CertificateSerialNo,
            row.NotifyUrl,
            HasApiV3Key(row),
            HasPrivateKey(row),
            row.IsDefault,
            row.IsEnabled,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    private static bool HasApiV3Key(PaymentMerchantConfigRecord row) =>
        !string.IsNullOrWhiteSpace(row.ApiV3KeyProtected);

    private static bool HasPrivateKey(PaymentMerchantConfigRecord row) =>
        !string.IsNullOrWhiteSpace(row.PrivateKeyProtected);
}
