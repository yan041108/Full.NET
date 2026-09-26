namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>常用权益目录编码；跨模块功能门禁与回填/种子引用这些稳定机器码。</summary>
public static class TenantEntitlementCatalogCodes
{
    /// <summary>兼容阶段默认绑定的基线权益编码。</summary>
    public const string CompatibilityBaseline = "compatibility.baseline";

    /// <summary>工作流实例启动等功能入口依赖的功能权益。</summary>
    public const string Workflow = "feature.workflow";
}
