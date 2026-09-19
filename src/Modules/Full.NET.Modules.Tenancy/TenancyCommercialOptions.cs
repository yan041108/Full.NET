namespace Full.NET.Modules.Tenancy;

internal sealed class TenancyCommercialOptions
{
    public const string SectionName = "Tenancy:Commercial";
    public bool RequirePackageOrSubscriptionOnReactivate { get; set; }
    public string? BootstrapEntitlementEnforcementPhase { get; set; }
}
