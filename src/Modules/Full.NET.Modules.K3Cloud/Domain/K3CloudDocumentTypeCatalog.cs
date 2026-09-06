using Full.NET.Modules.K3Cloud.Contracts;

namespace Full.NET.Modules.K3Cloud.Domain;

/// <summary>首切片允许的金蝶单据类型目录；禁止万能远程方法调用。</summary>
internal static class K3CloudDocumentTypeCatalog
{
    /// <summary>解析稳定单据类型键到金蝶 FormId。</summary>
    /// <param name="documentTypeKey">稳定单据类型键。</param>
    /// <returns>金蝶 FormId；未知键返回 null。</returns>
    public static string? ResolveFormId(string documentTypeKey) =>
        documentTypeKey switch
        {
            K3CloudDocumentTypeKeys.SalSaleOrder => "SAL_SaleOrder",
            _ => null,
        };

    /// <summary>判断单据类型键是否受支持。</summary>
    /// <param name="documentTypeKey">稳定单据类型键。</param>
    /// <returns>受支持返回 true。</returns>
    public static bool IsSupported(string documentTypeKey) =>
        ResolveFormId(documentTypeKey) is not null;
}
