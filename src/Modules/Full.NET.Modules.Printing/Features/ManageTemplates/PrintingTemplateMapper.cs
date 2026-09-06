using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Printing.Persistence;

namespace Full.NET.Modules.Printing.Features.ManageTemplates;

/// <summary>打印模板 DTO 映射。</summary>
internal static class PrintingTemplateMapper
{
    public static PrintingTemplateResponse MapTemplate(PrintingTemplateRecord record) =>
        new(
            record.Id,
            record.TemplateKey,
            record.Name,
            record.FormSchemaKey,
            record.LayoutHtml,
            record.LatestPublishedVersionNumber,
            record.IsEnabled,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    public static PrintingTemplateVersionResponse MapVersion(PrintingTemplateVersionRecord record) =>
        new(
            record.Id,
            record.TemplateId,
            record.VersionNumber,
            record.LayoutHtml,
            record.ChangeNote,
            record.PublishedByUserId,
            record.PublishedAtUtc);
}
