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

    /// <summary>领取已提交的退款意图，避免崩溃恢复窗口内两个请求同时调用渠道。</summary>
    public static readonly SqlStatement ClaimInvocation = new(
        "payments.claim_refund_invocation",
        """
        UPDATE fn_payment_refund
        SET UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @RefundId
          AND Version = @Version
          AND RefundStateKey IN ('created', 'provider_unknown')
        """,
        SqlDataScope.HostOnly);

    /// <summary>查找可恢复的退款意图，供订单进入退款中后的崩溃重试使用。</summary>
    public static readonly SqlStatement ListRecoverableByOrderId = new(
        "payments.list_recoverable_refunds_by_order",
        $"""
        SELECT {SelectColumns}
        FROM fn_payment_refund AS refunds
        WHERE refunds.OrderId = @OrderId
          AND refunds.RefundStateKey IN ('created', 'provider_unknown')
        ORDER BY refunds.CreatedAtUtc, refunds.Id
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
