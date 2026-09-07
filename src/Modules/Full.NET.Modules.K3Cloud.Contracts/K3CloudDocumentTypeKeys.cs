namespace Full.NET.Modules.K3Cloud.Contracts;

/// <summary>首种受支持的金蝶单据类型键。</summary>
public static class K3CloudDocumentTypeKeys
{
    /// <summary>销售订单；映射金蝶 FormId <c>SAL_SaleOrder</c>。</summary>
    public const string SalSaleOrder = "k3cloud.sal_sale_order";
}

/// <summary>单据同步状态键。</summary>
public static class K3CloudDocumentSyncStatusKeys
{
    /// <summary>等待执行 Save/Submit。</summary>
    public const string Pending = "pending";

    /// <summary>Save 成功，等待 Submit。</summary>
    public const string SaveSucceeded = "save_succeeded";

    /// <summary>Save 与 Submit 均成功。</summary>
    public const string Submitted = "submitted";

    /// <summary>Save 失败。</summary>
    public const string SaveFailed = "save_failed";

    /// <summary>Save 成功但 Submit 失败。</summary>
    public const string SubmitFailed = "submit_failed";

    /// <summary>已向金蝶发出 Save/Submit，但本地无法确认成败。</summary>
    public const string ProviderUnknown = "provider_unknown";
}

/// <summary>单据同步最近执行步骤键。</summary>
public static class K3CloudDocumentSyncStepKeys
{
    /// <summary>执行 Save。</summary>
    public const string Save = "save";

    /// <summary>执行 Submit。</summary>
    public const string Submit = "submit";
}
