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
        registrar.Register<BackupTaskRecord>(ReadBackupTaskRecord);
        registrar.Register<BackupRunRecord>(ReadBackupRunRecord);
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

    private static BackupTaskRecord ReadBackupTaskRecord(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        TaskKey = reader.GetString(1),
        DisplayName = reader.GetString(2),
        Description = AotDataReaderExtensions.ReadNullableString(reader, 3),
        DatabaseProvider = reader.GetString(4),
        IsEnabled = AotDataReaderExtensions.ReadBoolean(reader, 5),
        SortOrder = reader.GetInt32(6),
        CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 7),
        UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 8),
    };

    private static BackupRunRecord ReadBackupRunRecord(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        TaskId = reader.GetGuid(1),
        TaskKey = reader.GetString(2),
        TaskDisplayName = reader.GetString(3),
        Status = reader.GetString(4),
        StartedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 5),
        CompletedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 6),
        ArtifactFileName = AotDataReaderExtensions.ReadNullableString(reader, 7),
        ArtifactSizeBytes = reader.IsDBNull(8)
            ? null
            : AotDataReaderExtensions.ReadInt64(reader, 8),
        ArtifactContentType = AotDataReaderExtensions.ReadNullableString(reader, 9),
        SummaryMessage = AotDataReaderExtensions.ReadNullableString(reader, 10),
        CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 11),
    };
}
#endif
