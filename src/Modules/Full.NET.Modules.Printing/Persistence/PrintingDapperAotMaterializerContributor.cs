#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.Printing.Persistence;

/// <summary>打印草稿、版本及租户获授投影的静态物化器，不使用运行时反射。</summary>
internal sealed class PrintingDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    /// <inheritdoc />
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<PrintingTemplateRecord>(ReadTemplate);
        registrar.Register<PrintingTemplateVersionRecord>(ReadVersion);
        registrar.Register<PrintingPublishedTemplateRecord>(ReadCatalog);
        registrar.Register<PrintingGrantedVersionRecord>(ReadGranted);
    }

    private static PrintingTemplateRecord ReadTemplate(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")),
        TemplateKey = Text(reader, "TemplateKey"), Name = Text(reader, "Name"),
        FormSchemaKey = Text(reader, "FormSchemaKey"), LayoutHtml = Text(reader, "LayoutHtml"),
        LatestPublishedVersionNumber = Number(reader, "LatestPublishedVersionNumber"),
        IsEnabled = AotDataReaderExtensions.ReadBoolean(reader, reader.GetOrdinal("IsEnabled")),
        CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, reader.GetOrdinal("CreatedAtUtc")),
        UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, reader.GetOrdinal("UpdatedAtUtc")),
        Version = Number(reader, "Version"),
    };
    private static PrintingTemplateVersionRecord ReadVersion(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")), TemplateId = reader.GetGuid(reader.GetOrdinal("TemplateId")),
        VersionNumber = Number(reader, "VersionNumber"), LayoutHtml = Text(reader, "LayoutHtml"),
        ChangeNote = AotDataReaderExtensions.ReadNullableString(reader, reader.GetOrdinal("ChangeNote")),
        PublishedByUserId = reader.GetGuid(reader.GetOrdinal("PublishedByUserId")),
        PublishedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, reader.GetOrdinal("PublishedAtUtc")),
    };
    private static PrintingPublishedTemplateRecord ReadCatalog(DbDataReader reader) => Fill(reader, new PrintingPublishedTemplateRecord());
    private static PrintingGrantedVersionRecord ReadGranted(DbDataReader reader)
    {
        var row = Fill(reader, new PrintingGrantedVersionRecord());
        row.LayoutHtml = Text(reader, "LayoutHtml");
        return row;
    }
    private static T Fill<T>(DbDataReader reader, T row) where T : PrintingPublishedTemplateRecord
    {
        row.TemplateId = reader.GetGuid(reader.GetOrdinal("TemplateId"));
        row.TemplateKey = Text(reader, "TemplateKey"); row.TemplateName = Text(reader, "TemplateName");
        row.FormSchemaKey = Text(reader, "FormSchemaKey"); row.VersionNumber = Number(reader, "VersionNumber");
        return row;
    }
    private static string Text(DbDataReader reader, string name) => reader.GetString(reader.GetOrdinal(name));
    private static int Number(DbDataReader reader, string name) => AotDataReaderExtensions.ReadInt32(reader, reader.GetOrdinal(name));
}
#endif
