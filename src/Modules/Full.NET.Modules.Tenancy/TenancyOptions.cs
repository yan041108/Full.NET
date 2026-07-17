namespace Full.NET.Modules.Tenancy;

internal sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    public string[] HostDomains { get; set; } = [];
}
