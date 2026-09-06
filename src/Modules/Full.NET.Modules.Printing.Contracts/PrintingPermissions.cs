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
