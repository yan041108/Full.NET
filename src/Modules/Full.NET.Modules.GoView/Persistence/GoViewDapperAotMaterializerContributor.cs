#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.GoView.Persistence;

/// <summary>大屏项目原生物化器；摘要与明细保持同一固定投影序号，正文仅由明细查询读取。</summary>
internal sealed class GoViewDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    /// <inheritdoc />
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<GoViewProjectRecord>(ReadProject);
        registrar.Register<GoViewProjectVersionRecord>(ReadVersion);
    }

    private static GoViewProjectRecord ReadProject(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        ProjectKey = reader.GetString(1),
        Name = reader.GetString(2),
        CanvasJson = reader.GetString(3),
        LatestPublishedVersionNumber = AotDataReaderExtensions.ReadInt32(reader, 4),
        IsEnabled = AotDataReaderExtensions.ReadBoolean(reader, 5),
        CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
        UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 7),
        Version = AotDataReaderExtensions.ReadInt32(reader, 8),
    };

    private static GoViewProjectVersionRecord ReadVersion(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        ProjectId = reader.GetGuid(1),
        VersionNumber = AotDataReaderExtensions.ReadInt32(reader, 2),
        CanvasJson = reader.GetString(3),
        ChangeNote = AotDataReaderExtensions.ReadNullableString(reader, 4),
        PublishedByUserId = reader.GetGuid(5),
        PublishedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
    };
}
#endif
