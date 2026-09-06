using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Payments.Persistence;

/// <summary>支付商户配置管理 SQL。</summary>
internal static class PaymentMerchantConfigSql
{
    private const string SelectColumns = """
        config.Id,
               config.TenantId,
               config.Name,
               config.ChannelKey,
               config.AppId,
               config.MerchantId,
               config.CertificateSerialNo,
               config.NotifyUrl,
               config.ApiV3KeyProtected,
               config.PrivateKeyProtected,
               config.IsDefault,
               config.IsEnabled,
               config.CreatedAtUtc,
               config.UpdatedAtUtc,
               config.Version
        """;

    public static readonly SqlStatement Insert = new(
        "payments.insert_merchant_config",
        """
        INSERT INTO fn_payment_merchant_config
            (Id, TenantId, Name, ChannelKey, AppId, MerchantId, CertificateSerialNo, NotifyUrl,
             ApiV3KeyProtected, PrivateKeyProtected, IsDefault, IsEnabled, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @Name, @ChannelKey, @AppId, @MerchantId, @CertificateSerialNo, @NotifyUrl,
             @ApiV3KeyProtected, @PrivateKeyProtected, @IsDefault, @IsEnabled, @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "payments.find_merchant_config_by_id",
        $"""
        SELECT {SelectColumns}
        FROM fn_payment_merchant_config AS config
        WHERE config.Id = @MerchantConfigId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindDefaultForTenant = new(
        "payments.find_default_merchant_config_for_tenant",
        $"""
        SELECT TOP (1) {SelectColumns}
        FROM fn_payment_merchant_config AS config
        WHERE config.TenantId = @TenantId
          AND config.ChannelKey = @ChannelKey
          AND config.IsDefault = 1
          AND config.IsEnabled = 1
        ORDER BY config.Name, config.Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindDefaultForTenantMySql = new(
        "payments.find_default_merchant_config_for_tenant",
        $"""
        SELECT {SelectColumns}
        FROM fn_payment_merchant_config AS config
        WHERE config.TenantId = @TenantId
          AND config.ChannelKey = @ChannelKey
          AND config.IsDefault = 1
          AND config.IsEnabled = 1
        ORDER BY config.Name, config.Id
        LIMIT 1
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Update = new(
        "payments.update_merchant_config",
        """
        UPDATE fn_payment_merchant_config
        SET Name = @Name,
            ChannelKey = @ChannelKey,
            AppId = @AppId,
            MerchantId = @MerchantId,
            CertificateSerialNo = @CertificateSerialNo,
            NotifyUrl = @NotifyUrl,
            ApiV3KeyProtected = @ApiV3KeyProtected,
            PrivateKeyProtected = @PrivateKeyProtected,
            IsDefault = @IsDefault,
            IsEnabled = @IsEnabled,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @MerchantConfigId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ClearDefaultForScope = new(
        "payments.clear_default_merchant_config_for_scope",
        """
        UPDATE fn_payment_merchant_config
        SET IsDefault = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE ((@TenantId IS NULL AND TenantId IS NULL) OR TenantId = @TenantId)
          AND ChannelKey = @ChannelKey
          AND IsDefault = 1
          AND Id <> @ExcludeMerchantConfigId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Disable = new(
        "payments.disable_merchant_config",
        """
        UPDATE fn_payment_merchant_config
        SET IsEnabled = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @MerchantConfigId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly string CountSqlServer = """
        SELECT COUNT(1)
        FROM fn_payment_merchant_config AS config
        WHERE (@TenantId IS NULL OR config.TenantId = @TenantId)
          AND (@ChannelKey IS NULL OR config.ChannelKey = @ChannelKey)
          AND (@NameContains IS NULL OR config.Name LIKE '%' + @NameContains + '%')
          AND (@IsEnabled IS NULL OR config.IsEnabled = @IsEnabled)
        """;

    public static readonly string ListSqlServer = $"""
        SELECT {SelectColumns}
        FROM fn_payment_merchant_config AS config
        WHERE (@TenantId IS NULL OR config.TenantId = @TenantId)
          AND (@ChannelKey IS NULL OR config.ChannelKey = @ChannelKey)
          AND (@NameContains IS NULL OR config.Name LIKE '%' + @NameContains + '%')
          AND (@IsEnabled IS NULL OR config.IsEnabled = @IsEnabled)
        ORDER BY config.Name, config.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """;

    public static readonly string CountMySql = """
        SELECT COUNT(1)
        FROM fn_payment_merchant_config AS config
        WHERE (@TenantId IS NULL OR config.TenantId = @TenantId)
          AND (@ChannelKey IS NULL OR config.ChannelKey = @ChannelKey)
          AND (@NameContains IS NULL OR config.Name LIKE CONCAT('%', @NameContains, '%'))
          AND (@IsEnabled IS NULL OR config.IsEnabled = @IsEnabled)
        """;

    public static readonly string ListMySql = $"""
        SELECT {SelectColumns}
        FROM fn_payment_merchant_config AS config
        WHERE (@TenantId IS NULL OR config.TenantId = @TenantId)
          AND (@ChannelKey IS NULL OR config.ChannelKey = @ChannelKey)
          AND (@NameContains IS NULL OR config.Name LIKE CONCAT('%', @NameContains, '%'))
          AND (@IsEnabled IS NULL OR config.IsEnabled = @IsEnabled)
        ORDER BY config.Name, config.Id
        LIMIT @PageSize OFFSET @Offset
        """;
}
