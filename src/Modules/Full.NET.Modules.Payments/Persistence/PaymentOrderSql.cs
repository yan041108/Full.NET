using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Payments.Persistence;

/// <summary>支付订单管理 SQL。</summary>
internal static class PaymentOrderSql
{
    private const string SelectColumns = """
        orders.Id,
               orders.TenantId,
               orders.MerchantConfigId,
               orders.ChannelKey,
               orders.OutTradeNo,
               orders.TradeStateKey,
               orders.AmountMinor,
               orders.Currency,
               orders.Subject,
               orders.Description,
               orders.CodeUrl,
               orders.ProviderTransactionId,
               orders.FailMessage,
               orders.CreatedAtUtc,
               orders.UpdatedAtUtc,
               orders.PaidAtUtc,
               orders.Version
        """;

    public static readonly SqlStatement Insert = new(
        "payments.insert_order",
        """
        INSERT INTO fn_payment_order
            (Id, TenantId, MerchantConfigId, ChannelKey, OutTradeNo, TradeStateKey, AmountMinor,
             Currency, Subject, Description, CodeUrl, ProviderTransactionId, FailMessage,
             CreatedAtUtc, UpdatedAtUtc, PaidAtUtc, Version)
        VALUES
            (@Id, @TenantId, @MerchantConfigId, @ChannelKey, @OutTradeNo, @TradeStateKey, @AmountMinor,
             @Currency, @Subject, @Description, @CodeUrl, @ProviderTransactionId, @FailMessage,
             @CreatedAtUtc, @UpdatedAtUtc, @PaidAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "payments.find_order_by_id",
        $"""
        SELECT {SelectColumns}
        FROM fn_payment_order AS orders
        WHERE orders.Id = @OrderId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateProviderResult = new(
        "payments.update_order_provider_result",
        """
        UPDATE fn_payment_order
        SET TradeStateKey = @TradeStateKey,
            CodeUrl = @CodeUrl,
            ProviderTransactionId = @ProviderTransactionId,
            FailMessage = @FailMessage,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @OrderId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly string CountSqlServer = """
        SELECT COUNT(1)
        FROM fn_payment_order AS orders
        WHERE (@TenantId IS NULL OR orders.TenantId = @TenantId)
          AND (@TradeStateKey IS NULL OR orders.TradeStateKey = @TradeStateKey)
          AND (@OutTradeNoContains IS NULL OR orders.OutTradeNo LIKE '%' + @OutTradeNoContains + '%')
        """;

    public static readonly string ListSqlServer = $"""
        SELECT {SelectColumns}
        FROM fn_payment_order AS orders
        WHERE (@TenantId IS NULL OR orders.TenantId = @TenantId)
          AND (@TradeStateKey IS NULL OR orders.TradeStateKey = @TradeStateKey)
          AND (@OutTradeNoContains IS NULL OR orders.OutTradeNo LIKE '%' + @OutTradeNoContains + '%')
        ORDER BY orders.CreatedAtUtc DESC, orders.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """;

    public static readonly string CountMySql = """
        SELECT COUNT(1)
        FROM fn_payment_order AS orders
        WHERE (@TenantId IS NULL OR orders.TenantId = @TenantId)
          AND (@TradeStateKey IS NULL OR orders.TradeStateKey = @TradeStateKey)
          AND (@OutTradeNoContains IS NULL OR orders.OutTradeNo LIKE CONCAT('%', @OutTradeNoContains, '%'))
        """;

    public static readonly string ListMySql = $"""
        SELECT {SelectColumns}
        FROM fn_payment_order AS orders
        WHERE (@TenantId IS NULL OR orders.TenantId = @TenantId)
          AND (@TradeStateKey IS NULL OR orders.TradeStateKey = @TradeStateKey)
          AND (@OutTradeNoContains IS NULL OR orders.OutTradeNo LIKE CONCAT('%', @OutTradeNoContains, '%'))
        ORDER BY orders.CreatedAtUtc DESC, orders.Id
        LIMIT @PageSize OFFSET @Offset
        """;
}
