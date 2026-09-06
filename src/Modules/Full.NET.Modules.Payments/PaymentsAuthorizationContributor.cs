using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Payments;

/// <summary>向 Identity 授权目录注册支付权限、导航与页面操作。</summary>
internal sealed class PaymentsAuthorizationContributor : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("payments", "Payments", 119);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(PaymentMerchantPermissions.Read, "读取支付商户配置", AuthorizationScope.Host),
        new(PaymentMerchantPermissions.Create, "创建支付商户配置", AuthorizationScope.Host),
        new(PaymentMerchantPermissions.Update, "更新支付商户配置", AuthorizationScope.Host),
        new(PaymentOrderPermissions.Read, "读取支付订单", AuthorizationScope.Host),
        new(PaymentOrderPermissions.Create, "创建支付订单", AuthorizationScope.Host),
        new(PaymentOrderPermissions.Reconcile, "对账同步支付订单", AuthorizationScope.Host),
        new(PaymentRefundPermissions.Read, "读取支付退款", AuthorizationScope.Host),
        new(PaymentRefundPermissions.Create, "创建支付退款", AuthorizationScope.Host),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "payments-merchant-configs",
            null,
            "payments-merchant-configs",
            "/payments/merchant-configs",
            "payments-merchant-configs",
            "支付商户配置",
            "Payment Merchant Configs",
            "wallet",
            10,
            PaymentMerchantPermissions.Read),
        new NavigationDefinition(
            "payments-orders",
            null,
            "payments-orders",
            "/payments/orders",
            "payments-orders",
            "支付订单",
            "Payment Orders",
            "money",
            20,
            PaymentOrderPermissions.Read),
        new NavigationDefinition(
            "payments-refunds",
            null,
            "payments-refunds",
            "/payments/refunds",
            "payments-refunds",
            "支付退款",
            "Payment Refunds",
            "refresh-left",
            30,
            PaymentRefundPermissions.Read),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "payments.merchants.create",
            "payments-merchant-configs",
            PaymentMerchantPermissions.Create,
            "新建商户配置",
            "create",
            10),
        new AuthorizationActionDefinition(
            "payments.merchants.update",
            "payments-merchant-configs",
            PaymentMerchantPermissions.Update,
            "编辑商户配置",
            "update",
            20),
        new AuthorizationActionDefinition(
            "payments.orders.create",
            "payments-orders",
            PaymentOrderPermissions.Create,
            "创建支付订单",
            "create",
            10),
        new AuthorizationActionDefinition(
            "payments.orders.reconcile",
            "payments-orders",
            PaymentOrderPermissions.Reconcile,
            "对账同步订单",
            "reconcile",
            20),
        new AuthorizationActionDefinition(
            "payments.orders.refund",
            "payments-orders",
            PaymentRefundPermissions.Create,
            "发起退款",
            "refund",
            30),
    ];
}
