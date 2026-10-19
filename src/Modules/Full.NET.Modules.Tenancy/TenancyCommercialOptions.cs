// 租户商业化配置选项；计费开关与宽限期，单位与默认见配置注释。
﻿namespace Full.NET.Modules.Tenancy;

/// <summary>租户商业化配置选项；计费开关与宽限期，单位与默认见配置注释。</summary>
internal sealed class TenancyCommercialOptions
{
    public const string SectionName = "Tenancy:Commercial";
    public bool RequirePackageOrSubscriptionOnReactivate { get; set; }
    public string? BootstrapEntitlementEnforcementPhase { get; set; }
    public string? CompatibilityBackfillEntitlementCode { get; set; }
}
