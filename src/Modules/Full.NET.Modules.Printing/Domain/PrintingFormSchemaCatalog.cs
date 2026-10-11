using Full.NET.Modules.Printing.Contracts;

namespace Full.NET.Modules.Printing.Domain;

/// <summary>Printing 布局与预览边界常量。</summary>
internal static class PrintingLayoutPolicy
{
    /// <summary>布局 HTML 最大字符数。</summary>
    public const int MaxLayoutHtmlLength = 64 * 1024;
}

/// <summary>内置固定表单 Schema 目录；字段集合由服务端固定，客户端不可扩展。</summary>
internal sealed class PrintingFormSchemaCatalog
{
    private static readonly IReadOnlyList<PrintingFormSchemaDefinition> Schemas =
    [
        new(
            PrintingFormSchemaKeys.TenantProfileCard,
            "租户档案卡片",
            "用于浏览器预览/打印的租户基础信息卡片。",
            [
                new PrintingFormFieldDefinition("tenantName", "租户名称"),
                new PrintingFormFieldDefinition("tenantCode", "租户编码"),
                new PrintingFormFieldDefinition("tenantDomain", "租户域名"),
                new PrintingFormFieldDefinition("printedByDisplayName", "打印人"),
                new PrintingFormFieldDefinition("printedAtUtc", "打印时间"),
            ]),
    ];

    private readonly IReadOnlyList<PrintingFormSchemaDefinition> schemas;

    /// <summary>组合内置和显式贡献的静态目录，重复键必须失败，避免绑定所有权被覆盖。</summary>
    public PrintingFormSchemaCatalog(IEnumerable<IPrintingFormSchemaContributor> contributors)
    {
        var all = Schemas.Concat(contributors.SelectMany(item => item.Schemas)).ToArray();
        if (all.GroupBy(item => item.FormSchemaKey, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidOperationException("Printing form schema keys must be unique.");
        schemas = all;
    }

    /// <summary>返回全部固定表单 Schema。</summary>
    public IReadOnlyList<PrintingFormSchemaDefinition> List() => schemas;

    /// <summary>按键解析 Schema；不存在时返回 <see langword="null"/>。</summary>
    public PrintingFormSchemaDefinition? TryGet(string formSchemaKey) =>
        schemas.FirstOrDefault(schema =>
            string.Equals(schema.FormSchemaKey, formSchemaKey, StringComparison.Ordinal));
}
