using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Printing.Domain;

namespace Full.NET.Modules.Printing.Features.BrowseFormSchemas;

/// <summary>固定表单 Schema 目录只读查询。</summary>
internal sealed class PrintingFormSchemaQueryService(PrintingFormSchemaCatalog catalog)
{
    /// <summary>返回全部固定表单 Schema。</summary>
    public IReadOnlyList<PrintingFormSchemaDefinition> List() => catalog.List();

    /// <summary>按键解析 Schema。</summary>
    public PrintingFormSchemaDefinition? TryGet(string formSchemaKey) =>
        catalog.TryGet(formSchemaKey);
}
