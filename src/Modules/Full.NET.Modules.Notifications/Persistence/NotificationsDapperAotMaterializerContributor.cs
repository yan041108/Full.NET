#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>
/// Notifications 模块 Native AOT 行物化器注册，覆盖公告、站内信与平台内核查询投影。
/// </summary>
internal sealed class NotificationsDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    /// <summary>
    /// 在首个数据库请求前注册 Notifications 模块的全部 Native AOT 行物化器。
    /// </summary>
    /// <param name="registrar">用于写入进程级静态物化器注册表的注册器。</param>
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<AnnouncementRecord>(ReadAnnouncementRecord);
        registrar.Register<AnnouncementTargetUserRecord>(ReadAnnouncementTargetUserRecord);
        registrar.Register<AnnouncementTargetOrganizationRecord>(ReadAnnouncementTargetOrganizationRecord);
        registrar.Register<InboxMessageRecord>(ReadInboxMessageRecord);
        registrar.Register<NotificationTemplateRecord>(ReadTemplate);
        registrar.Register<NotificationTemplateListRecord>(ReadTemplateList);
        registrar.Register<NotificationTemplateVersionRecord>(ReadTemplateVersion);
        registrar.Register<NotificationTemplateLocaleStateRecord>(ReadTemplateLocaleState);
        registrar.Register<NotificationIntentRecord>(ReadIntent);
        registrar.Register<NotificationRecipientRecord>(ReadRecipient);
        registrar.Register<NotificationDeliveryRecord>(ReadDelivery);
        registrar.Register<NotificationDeliveryAttemptRecord>(ReadDeliveryAttempt);
        registrar.Register<NotificationReceiptRecord>(ReadReceipt);
        registrar.Register<NotificationRecipientEndpointRecord>(ReadRecipientEndpoint);
        registrar.Register<NotificationRecipientEndpointProtectedRecord>(ReadRecipientEndpointProtected);
        registrar.Register<NotificationRecipientEndpointChallengeRecord>(ReadRecipientEndpointChallenge);
        registrar.Register<NotificationProviderProfileRecord>(ReadProfile);
        registrar.Register<NotificationProviderProfileVersionRecord>(ReadProfileVersion);
        registrar.Register<NotificationBindingRecord>(ReadBinding);
        registrar.Register<NotificationBindingVersionRecord>(ReadBindingVersion);
    }

    private static AnnouncementRecord ReadAnnouncementRecord(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            TenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            Title = reader.GetString(2),
            Content = reader.GetString(3),
            Kind = reader.GetString(4),
            AudienceKind = reader.GetString(5),
            Status = reader.GetString(6),
            PublishedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 7),
            PublishedByUserId = AotDataReaderExtensions.ReadNullableGuid(reader, 8),
            RetractedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 9),
            RetractedByUserId = AotDataReaderExtensions.ReadNullableGuid(reader, 10),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 11),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 12),
            CreatedByUserId = reader.GetGuid(13),
            UpdatedByUserId = AotDataReaderExtensions.ReadNullableGuid(reader, 14),
            Version = reader.GetInt32(15),
        };

    /// <summary>
    /// 按公告用户受众查询的固定列顺序物化目标用户记录。
    /// </summary>
    /// <param name="reader">已经定位到当前结果行的数据读取器。</param>
    /// <returns>包含公告与用户关联标识的受众记录。</returns>
    private static AnnouncementTargetUserRecord ReadAnnouncementTargetUserRecord(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            AnnouncementId = reader.GetGuid(1),
            UserId = reader.GetGuid(2),
        };

    /// <summary>
    /// 按公告机构受众查询的固定列顺序物化目标机构记录。
    /// </summary>
    /// <param name="reader">已经定位到当前结果行的数据读取器。</param>
    /// <returns>包含租户与机构单元边界的受众记录。</returns>
    private static AnnouncementTargetOrganizationRecord ReadAnnouncementTargetOrganizationRecord(
        DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            AnnouncementId = reader.GetGuid(1),
            TenantId = reader.GetGuid(2),
            OrganizationUnitId = reader.GetGuid(3),
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
            ScopeKey = reader.GetString(9),
            TenantScopeKey = reader.GetString(10),
            IntentId = AotDataReaderExtensions.ReadNullableGuid(reader, 11),
        };

    private static NotificationRecipientEndpointRecord ReadRecipientEndpoint(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetGuid(4),
            reader.GetGuid(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 9),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 10));

    /// <summary>
    /// 仅在可信验证边界内物化待验证端点的受保护值，禁止把结果传入 HTTP 响应。
    /// </summary>
    /// <param name="reader">已经定位到当前结果行的数据读取器。</param>
    /// <returns>包含端点受保护值及验证状态的内部记录。</returns>
    private static NotificationRecipientEndpointProtectedRecord ReadRecipientEndpointProtected(
        DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5));

    private static NotificationRecipientEndpointChallengeRecord ReadRecipientEndpointChallenge(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 7),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 8),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 9));

    private static NotificationTemplateRecord ReadTemplate(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            AotDataReaderExtensions.ReadInt64(reader, 11),
            AotDataReaderExtensions.ReadNullableGuid(reader, 12),
            reader.GetGuid(13),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 14),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 15),
            AotDataReaderExtensions.ReadInt64(reader, 16));

    private static NotificationTemplateListRecord ReadTemplateList(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            AotDataReaderExtensions.ReadInt64(reader, 11),
            AotDataReaderExtensions.ReadNullableGuid(reader, 12),
            reader.IsDBNull(13) ? null : AotDataReaderExtensions.ReadInt32(reader, 13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            reader.GetGuid(16),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 17),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 18),
            AotDataReaderExtensions.ReadInt64(reader, 19));

    private static NotificationTemplateVersionRecord ReadTemplateVersion(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            AotDataReaderExtensions.ReadInt32(reader, 3),
            AotDataReaderExtensions.ReadInt32(reader, 4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetGuid(10),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 11));

    private static NotificationTemplateLocaleStateRecord ReadTemplateLocaleState(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            AotDataReaderExtensions.ReadNullableGuid(reader, 3));

    private static NotificationIntentRecord ReadIntent(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetGuid(7),
            AotDataReaderExtensions.ReadNullableGuid(reader, 8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetGuid(14),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 15),
            AotDataReaderExtensions.ReadInt64(reader, 16));

    private static NotificationRecipientRecord ReadRecipient(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadNullableGuid(reader, 4),
            AotDataReaderExtensions.ReadNullableString(reader, 5),
            reader.GetString(6),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 7));

    private static NotificationDeliveryRecord ReadDelivery(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadNullableGuid(reader, 4),
            AotDataReaderExtensions.ReadNullableGuid(reader, 5),
            reader.GetString(6),
            AotDataReaderExtensions.ReadInt64(reader, 7),
            AotDataReaderExtensions.ReadNullableString(reader, 8),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 9),
            AotDataReaderExtensions.ReadInt64(reader, 10),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 12),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 13));

    private static NotificationDeliveryAttemptRecord ReadDeliveryAttempt(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            AotDataReaderExtensions.ReadInt32(reader, 2),
            AotDataReaderExtensions.ReadNullableString(reader, 3),
            AotDataReaderExtensions.ReadInt64(reader, 4),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 5),
            AotDataReaderExtensions.ReadNullableString(reader, 6),
            reader.GetString(7),
            AotDataReaderExtensions.ReadNullableString(reader, 8),
            AotDataReaderExtensions.ReadNullableString(reader, 9),
            AotDataReaderExtensions.ReadNullableString(reader, 10),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 11),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 12));

    private static NotificationReceiptRecord ReadReceipt(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            AotDataReaderExtensions.ReadNullableString(reader, 2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadNullableGuid(reader, 4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 8),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 9),
            reader.GetString(10));

    private static NotificationProviderProfileRecord ReadProfile(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            AotDataReaderExtensions.ReadNullableString(reader, 7),
            AotDataReaderExtensions.ReadBoolean(reader, 8),
            AotDataReaderExtensions.ReadInt64(reader, 9),
            AotDataReaderExtensions.ReadNullableGuid(reader, 10),
            reader.GetGuid(11),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 12),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 13),
            AotDataReaderExtensions.ReadInt64(reader, 14));

    private static NotificationProviderProfileVersionRecord ReadProfileVersion(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            AotDataReaderExtensions.ReadInt32(reader, 2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            AotDataReaderExtensions.ReadNullableString(reader, 6),
            reader.GetString(7),
            reader.GetGuid(8),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 9));

    private static NotificationBindingRecord ReadBinding(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            AotDataReaderExtensions.ReadInt64(reader, 7),
            AotDataReaderExtensions.ReadNullableGuid(reader, 8),
            reader.GetGuid(9),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 10),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11),
            AotDataReaderExtensions.ReadInt64(reader, 12));

    private static NotificationBindingVersionRecord ReadBindingVersion(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            AotDataReaderExtensions.ReadInt32(reader, 2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetGuid(9),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 10));
}
#endif
