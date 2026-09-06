namespace Full.NET.Modules.Ai.Contracts;

/// <summary>AI 租户配额权限码。</summary>
public static class AiTenantQuotaPermissions
{
    /// <summary>读取租户配额。</summary>
    public const string Read = "ai.quotas.read";

    /// <summary>更新租户配额。</summary>
    public const string Update = "ai.quotas.update";
}
