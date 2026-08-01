namespace Full.NET.Modules.Tenancy.Auditing;

/// <summary>
/// Tenancy 模块 B0 域内审计使用的稳定 ActionKey 常量；取值必须与
/// <c>Full.NET.Modules.Auditing.AuditReliabilityCatalog</c> 中登记的分类保持一致。
/// </summary>
internal static class TenancyDomainAuditActionKeys
{
    /// <summary>Host 管理员禁用租户。</summary>
    public const string HostTenantDisable = "tenancy.host_tenant.disable";
}

/// <summary>Tenancy 模块 B0 域内审计记录的固定结果取值。</summary>
internal static class TenancyDomainAuditOutcomes
{
    public const string Success = "success";

    public const string Failure = "failure";
}
