using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Payments.Persistence;

/// <summary>支付退款管理 SQL。</summary>
internal static class PaymentRefundSql
{
    private const string SelectColumns = """
        refunds.Id,
               refunds.TenantId,
               refunds.OrderId,
               refunds.MerchantConfigId,
               refunds.OutTradeNo,
               refunds.OutRefundNo,
               refunds.RefundStateKey,
               refunds.AmountMinor,
               refunds.Currency,
               refunds.Reason,
               refunds.ProviderRefundId,
               refunds.FailMessage,
               refunds.CreatedAtUtc,
               refunds.UpdatedAtUtc,
               refunds.CompletedAtUtc,
               refunds.Version
        """;

    public static readonly SqlStatement Insert = new(
        "payments.insert_refund",
        """
        INSERT INTO fn_payment_refund
            (Id, TenantId, OrderId, MerchantConfigId, OutTradeNo, OutRefundNo, RefundStateKey,
             AmountMinor, Currency, Reason, ProviderRefundId, FailMessage,
             CreatedAtUtc, UpdatedAtUtc, CompletedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @OrderId, @MerchantConfigId, @OutTradeNo, @OutRefundNo, @RefundStateKey,
             @AmountMinor, @Currency, @Reason, @ProviderRefundId, @FailMessage,
             @CreatedAtUtc, @UpdatedAtUtc, @CompletedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "payments.find_refund_by_id",
        $"""
        SELECT {SelectColumns}
        FROM fn_payment_refund AS refunds
        WHERE refunds.Id = @RefundId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateProviderResult = new(
        "payments.update_refund_provider_result",
        """
        UPDATE fn_payment_refund
        SET RefundStateKey = @RefundStateKey,
            ProviderRefundId = @ProviderRefundId,
            FailMessage = @FailMessage,
            UpdatedAtUtc = @UpdatedAtUtc,
            CompletedAtUtc = @CompletedAtUtc,
            Version = Version + 1
        WHERE Id = @RefundId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly string CountSqlServer = """
        SELECT COUNT(1)
        FROM fn_payment_refund AS refunds
        WHERE (@TenantId IS NULL OR refunds.TenantId = @TenantId)
          AND (@OrderId IS NULL OR refunds.OrderId = @OrderId)
          AND (@RefundStateKey IS NULL OR refunds.RefundStateKey = @RefundStateKey)
        """;

    public static readonly string ListSqlServer = $"""
        SELECT {SelectColumns}
        FROM fn_payment_refund AS refunds
        WHERE (@TenantId IS NULL OR refunds.TenantId = @TenantId)
          AND (@OrderId IS NULL OR refunds.OrderId = @OrderId)
          AND (@RefundStateKey IS NULL OR refunds.RefundStateKey = @RefundStateKey)
        ORDER BY refunds.CreatedAtUtc DESC, refunds.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """;

    public static readonly string CountMySql = """
        SELECT COUNT(1)
        FROM fn_payment_refund AS refunds
        WHERE (@TenantId IS NULL OR refunds.TenantId = @TenantId)
          AND (@OrderId IS NULL OR refunds.OrderId = @OrderId)
          AND (@RefundStateKey IS NULL OR refunds.RefundStateKey = @RefundStateKey)
        """;

    public static readonly string ListMySql = $"""
        SELECT {SelectColumns}
        FROM fn_payment_refund AS refunds
        WHERE (@TenantId IS NULL OR refunds.TenantId = @TenantId)
          AND (@OrderId IS NULL OR refunds.OrderId = @OrderId)
          AND (@RefundStateKey IS NULL OR refunds.RefundStateKey = @RefundStateKey)
        ORDER BY refunds.CreatedAtUtc DESC, refunds.Id
        LIMIT @PageSize OFFSET @Offset
        """;
}
