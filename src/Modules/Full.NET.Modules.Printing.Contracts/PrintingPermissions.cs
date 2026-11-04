namespace Full.NET.Modules.Printing.Contracts;

/// <summary>打印模板管理权限码。</summary>
public static class PrintingTemplatePermissions
{
    /// <summary>读取打印模板与版本。</summary>
    public const string Read = "printing.templates.read";

    /// <summary>创建打印模板。</summary>
    public const string Create = "printing.templates.create";

    /// <summary>更新打印模板草稿。</summary>
    public const string Update = "printing.templates.update";

    /// <summary>发布打印模板版本。</summary>
    public const string Publish = "printing.templates.publish";

    /// <summary>向活动租户授予或撤销精确发布版本。</summary>
    public const string GrantTenants = "printing.templates.grant_tenants";

    /// <summary>预览打印模板绑定结果。</summary>
    public const string Preview = "printing.templates.preview";
}

/// <summary>固定表单 Schema 目录权限码。</summary>
public static class PrintingFormSchemaPermissions
{
    /// <summary>读取固定表单 Schema 目录。</summary>
    public const string Read = "printing.form_schemas.read";
}

/// <summary>首种固定表单 Schema 键。</summary>
public static class PrintingFormSchemaKeys
{
    /// <summary>租户档案卡片；字段由 Printing 模块固定声明。</summary>
    public const string TenantProfileCard = "printing.tenant_profile_card";
}

/// <summary>租户已获授打印版本的独立权限，不能访问 Host 草稿管理。</summary>
public static class PrintingPublishedTemplatePermissions
{
    /// <summary>读取当前租户获授的发布版本目录。</summary>
    public const string Read = "printing.published_templates.read";
    /// <summary>使用当前租户数据预览已获授发布版本。</summary>
    public const string Preview = "printing.published_templates.preview";
}
