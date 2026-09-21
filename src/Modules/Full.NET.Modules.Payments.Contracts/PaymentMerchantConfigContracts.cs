namespace Full.NET.Modules.Payments.Contracts;

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>支付商户配置列表项；敏感字段已脱敏。</summary>
/// <param name="Id">配置稳定标识。</param>
/// <param name="TenantId">所属租户标识；为空表示 Host 级配置。</param>
/// <param name="Name">显示名称。</param>
/// <param name="ChannelKey">支付渠道键。</param>
/// <param name="MaskedAppId">脱敏后的应用标识。</param>
/// <param name="MaskedMerchantId">脱敏后的商户号。</param>
/// <param name="MaskedCertificateSerialNo">脱敏后的证书序列号。</param>
/// <param name="MaskedNotifyUrl">脱敏后的回调地址。</param>
/// <param name="MaskedReturnUrl">脱敏后的同步跳转地址。</param>
/// <param name="HasApiV3Key">是否已配置 API v3 密钥。</param>
/// <param name="HasPrivateKey">是否已配置商户私钥。</param>
/// <param name="IsDefault">是否为当前作用域默认配置。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record PaymentMerchantConfigListItem(
    Guid Id,
    Guid? TenantId,
    string Name,
    string ChannelKey,
    string MaskedAppId,
    string MaskedMerchantId,
    string MaskedCertificateSerialNo,
    string MaskedNotifyUrl,
    string MaskedReturnUrl,
    bool HasApiV3Key,
    bool HasPrivateKey,
    bool IsDefault,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>支付商户配置详情；不回显受保护密钥与私钥。</summary>
/// <param name="Id">配置稳定标识。</param>
/// <param name="TenantId">所属租户标识；为空表示 Host 级配置。</param>
/// <param name="Name">显示名称。</param>
/// <param name="ChannelKey">支付渠道键。</param>
/// <param name="AppId">应用标识。</param>
/// <param name="MerchantId">商户号；支付宝可选占位时为 "-"。</param>
/// <param name="CertificateSerialNo">证书序列号；支付宝可选占位时为 "-"。</param>
/// <param name="NotifyUrl">支付结果通知地址。</param>
/// <param name="ReturnUrl">支付完成同步跳转地址；支付宝 Page Pay 必填。</param>
/// <param name="HasApiV3Key">是否已配置 API v3 密钥。</param>
/// <param name="HasPrivateKey">是否已配置商户私钥。</param>
/// <param name="IsDefault">是否为当前作用域默认配置。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record PaymentMerchantConfigResponse(
    Guid Id,
    Guid? TenantId,
    string Name,
    string ChannelKey,
    string AppId,
    string MerchantId,
    string CertificateSerialNo,
    string NotifyUrl,
    string ReturnUrl,
    bool HasApiV3Key,
    bool HasPrivateKey,
    bool IsDefault,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>创建支付商户配置请求。</summary>
/// <param name="TenantId">所属租户标识；为空表示 Host 级配置。</param>
/// <param name="Name">显示名称。</param>
/// <param name="ChannelKey">支付渠道键。</param>
/// <param name="AppId">应用标识。</param>
/// <param name="MerchantId">商户号；支付宝可留空并由服务端写入 "-"。</param>
/// <param name="CertificateSerialNo">证书序列号；支付宝可留空并由服务端写入 "-"。</param>
/// <param name="NotifyUrl">支付结果通知地址。</param>
/// <param name="ReturnUrl">支付完成同步跳转地址；支付宝 Page Pay 必填。</param>
/// <param name="ApiV3Key">API v3 密钥；微信创建时必填，支付宝不需要。</param>
/// <param name="PrivateKeyPem">商户 API 私钥 PEM；仅写入时接受，响应不回显。</param>
/// <param name="IsDefault">是否为当前作用域默认配置。</param>
/// <param name="IsEnabled">是否启用。</param>
public sealed record CreatePaymentMerchantConfigRequest(
    Guid? TenantId,
    string Name,
    string ChannelKey,
    string AppId,
    string MerchantId,
    string CertificateSerialNo,
    string NotifyUrl,
    string ReturnUrl,
    string? ApiV3Key,
    string? PrivateKeyPem,
    bool IsDefault,
    bool IsEnabled);

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>更新支付商户配置请求。</summary>
/// <param name="Name">显示名称。</param>
/// <param name="ChannelKey">支付渠道键。</param>
/// <param name="AppId">应用标识。</param>
/// <param name="MerchantId">商户号；支付宝可留空并由服务端写入 "-"。</param>
/// <param name="CertificateSerialNo">证书序列号；支付宝可留空并由服务端写入 "-"。</param>
/// <param name="NotifyUrl">支付结果通知地址。</param>
/// <param name="ReturnUrl">支付完成同步跳转地址；支付宝 Page Pay 必填。</param>
/// <param name="ApiV3Key">API v3 密钥；为空表示不修改，非空则覆盖。</param>
/// <param name="ClearApiV3Key">是否清除已保存的 API v3 密钥。</param>
/// <param name="PrivateKeyPem">商户 API 私钥 PEM；为空表示不修改，非空则覆盖。</param>
/// <param name="ClearPrivateKey">是否清除已保存的商户私钥。</param>
/// <param name="IsDefault">是否为当前作用域默认配置。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record UpdatePaymentMerchantConfigRequest(
    string Name,
    string ChannelKey,
    string AppId,
    string MerchantId,
    string CertificateSerialNo,
    string NotifyUrl,
    string ReturnUrl,
    string? ApiV3Key,
    bool ClearApiV3Key,
    string? PrivateKeyPem,
    bool ClearPrivateKey,
    bool IsDefault,
    bool IsEnabled,
    int Version);
