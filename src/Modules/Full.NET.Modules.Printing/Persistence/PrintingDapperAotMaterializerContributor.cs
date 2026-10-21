#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.Printing.Persistence;

/// <summary>打印模板原生物化器；读取序号与模板及版本的摘要、明细 SQL 投影一致。</summary>
internal sealed class PrintingDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    /// <inheritdoc />
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<PrintingTemplateRecord>(ReadTemplate);
        registrar.Register<PrintingTemplateVersionRecord>(ReadVersion);
    }

    private static PrintingTemplateRecord ReadTemplate(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        TemplateKey = reader.GetString(1),
        Name = reader.GetString(2),
        FormSchemaKey = reader.GetString(3),
        LayoutHtml = reader.GetString(4),
        LatestPublishedVersionNumber = AotDataReaderExtensions.ReadInt32(reader, 5),
        IsEnabled = AotDataReaderExtensions.ReadBoolean(reader, 6),
        CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 7),
        UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 8),
        Version = AotDataReaderExtensions.ReadInt32(reader, 9),
    };

    private static PrintingTemplateVersionRecord ReadVersion(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        TemplateId = reader.GetGuid(1),
        VersionNumber = AotDataReaderExtensions.ReadInt32(reader, 2),
        LayoutHtml = reader.GetString(3),
        ChangeNote = AotDataReaderExtensions.ReadNullableString(reader, 4),
        PublishedByUserId = reader.GetGuid(5),
        PublishedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
    };
}
#endif
