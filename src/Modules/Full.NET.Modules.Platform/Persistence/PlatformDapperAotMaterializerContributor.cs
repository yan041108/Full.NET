#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.Platform.Persistence;

/// <summary>
/// Platform Native AOT 行物化器。读取序号必须与 ReleaseNoteSql 中对应投影保持一致。
/// </summary>
internal sealed class PlatformDapperAotMaterializerContributor
    : IDapperAotMaterializerContributor
{
    /// <summary>注册 Platform 模块所需的 Dapper AOT 行物化器。</summary>
    /// <param name="registrar">Native AOT 物化器注册器。</param>
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<ReleaseNoteRecord>(ReadReleaseNoteRecord);
        registrar.Register<ReleaseNoteReadRecord>(ReadReleaseNoteReadRecord);
        registrar.Register<MyReleaseNoteRecord>(ReadMyReleaseNoteRecord);
    }

    private static ReleaseNoteRecord ReadReleaseNoteRecord(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        VersionLabel = reader.GetString(1),
        VersionSortKey = reader.GetInt64(2),
        Title = reader.GetString(3),
        Content = reader.GetString(4),
        Status = reader.GetString(5),
        PublishedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 6),
        PublishedByUserId = AotDataReaderExtensions.ReadNullableGuid(reader, 7),
        RetractedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 8),
        RetractedByUserId = AotDataReaderExtensions.ReadNullableGuid(reader, 9),
        CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 10),
        UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11),
        CreatedByUserId = reader.GetGuid(12),
        UpdatedByUserId = AotDataReaderExtensions.ReadNullableGuid(reader, 13),
        Version = reader.GetInt32(14),
    };

    private static ReleaseNoteReadRecord ReadReleaseNoteReadRecord(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        ReleaseNoteId = reader.GetGuid(1),
        UserId = reader.GetGuid(2),
        ReadAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 3),
    };

    private static MyReleaseNoteRecord ReadMyReleaseNoteRecord(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        VersionLabel = reader.GetString(1),
        VersionSortKey = reader.GetInt64(2),
        Title = reader.GetString(3),
        Content = reader.GetString(4),
        PublishedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 5),
        IsRead = reader.GetInt32(6) != 0,
        ReadAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 7),
    };
}
#endif
