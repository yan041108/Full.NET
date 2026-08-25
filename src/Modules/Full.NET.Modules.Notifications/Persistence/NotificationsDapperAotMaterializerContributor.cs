#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>
/// Notifications 模块 Native AOT 行物化器注册，覆盖公告与站内信查询投影。
/// </summary>
internal sealed class NotificationsDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<AnnouncementRecord>(ReadAnnouncementRecord);
        registrar.Register<InboxMessageRecord>(ReadInboxMessageRecord);
    }

    private static AnnouncementRecord ReadAnnouncementRecord(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            TenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            Title = reader.GetString(2),
            Content = reader.GetString(3),
            Status = reader.GetString(4),
            PublishedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 5),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 7),
            CreatedByUserId = reader.GetGuid(8),
            UpdatedByUserId = AotDataReaderExtensions.ReadNullableGuid(reader, 9),
            Version = reader.GetInt32(10),
        };

    private static InboxMessageRecord ReadInboxMessageRecord(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            TenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            RecipientUserId = reader.GetGuid(2),
            Title = reader.GetString(3),
            Content = reader.GetString(4),
            Status = reader.GetString(5),
            ReadAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 6),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 7),
            CreatedByUserId = AotDataReaderExtensions.ReadNullableGuid(reader, 8),
        };
}
#endif
