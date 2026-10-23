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
        registrar.Register<ReceivedHostAnnouncementRecord>(ReadReceivedHostAnnouncementRecord);
        registrar.Register<AnnouncementReadReceiptRecord>(ReadAnnouncementReadReceiptRecord);
        registrar.Register<InboxMessageRecord>(ReadInboxMessageRecord);
        registrar.Register<NotificationTemplateRecord>(ReadTemplate);
        registrar.Register<NotificationTemplateListRecord>(ReadTemplateList);
        registrar.Register<NotificationTemplateVersionRecord>(ReadTemplateVersion);
        registrar.Register<NotificationTemplateLocaleStateRecord>(ReadTemplateLocaleState);
        registrar.Register<NotificationTemplateKeyLocaleStateRecord>(reader => new(
            reader.GetString(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
            AotDataReaderExtensions.ReadNullableGuid(reader, 4)));
        registrar.Register<NotificationIntentRecord>(ReadIntent);
        registrar.Register<NotificationIntentAttachmentRecord>(ReadIntentAttachment);
        registrar.Register<NotificationRecipientRecord>(ReadRecipient);
        registrar.Register<NotificationDeliveryRecord>(ReadDelivery);
        registrar.Register<NotificationPreferenceRecord>(ReadPreference);
        registrar.Register<NotificationProducerSceneRecord>(ReadProducerScene);
        registrar.Register<NotificationDeliveryAttemptRecord>(ReadDeliveryAttempt);
        registrar.Register<NotificationReceiptRecord>(ReadReceipt);
        registrar.Register<NotificationRecipientEndpointRecord>(ReadRecipientEndpoint);
        registrar.Register<NotificationRecipientEndpointProtectedRecord>(ReadRecipientEndpointProtected);
        registrar.Register<NotificationRecipientEndpointChallengeRecord>(ReadRecipientEndpointChallenge);
        registrar.Register<NotificationProviderProfileRecord>(ReadProfile);
        registrar.Register<NotificationProviderProfileVersionRecord>(ReadProfileVersion);
        registrar.Register<NotificationBindingRecord>(ReadBinding);
        registrar.Register<NotificationBindingVersionRecord>(ReadBindingVersion);
        registrar.Register<NotificationQuotaPolicyRecord>(ReadQuotaPolicy);
        registrar.Register<NotificationQuotaResolutionRecord>(ReadQuotaResolution);
        registrar.Register<NotificationQuotaCounterRecord>(ReadQuotaCounter);
        registrar.Register<NotificationQuotaReservationRecord>(ReadQuotaReservation);
        registrar.Register<NotificationQuotaAbandonedReservationRecord>(ReadQuotaAbandonedReservation);
        registrar.Register<NotificationQuotaOutstandingRecord>(ReadQuotaOutstanding);
        registrar.Register<WeChatMiniProgramBindingRecord>(reader => new WeChatMiniProgramBindingRecord
        {
            Id = reader.GetGuid(0), TenantScopeKey = reader.GetString(1), UserId = reader.GetGuid(2),
            AppId = reader.GetString(3), ProviderProfileVersionId = reader.GetGuid(4),
            OpenIdProtected = reader.GetString(5), OpenIdMask = reader.GetString(6), OpenIdSha256Hex = reader.GetString(7),
            UnionIdProtected = AotDataReaderExtensions.ReadNullableString(reader, 8),
            UnionIdMask = AotDataReaderExtensions.ReadNullableString(reader, 9), VerificationStatusKey = reader.GetString(10),
            RecipientEndpointId = AotDataReaderExtensions.ReadNullableGuid(reader, 11),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 12),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 13),
        });
        registrar.Register<WeChatMiniProgramSubscriptionRecord>(reader => new WeChatMiniProgramSubscriptionRecord
        {
            BindingId = reader.GetGuid(0), TemplateId = reader.GetString(1), StatusKey = reader.GetString(2),
            AuthorizedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 3),
        });
        registrar.Register<NotificationDeliverySummaryRecord>(reader => new NotificationDeliverySummaryRecord(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), Convert.ToInt64(reader.GetValue(3), System.Globalization.CultureInfo.InvariantCulture),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 4)));
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

    private static ReceivedHostAnnouncementRecord ReadReceivedHostAnnouncementRecord(
        DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            Title = reader.GetString(1),
            Content = reader.GetString(2),
            Kind = reader.GetString(3),
            AudienceKind = reader.GetString(4),
            PublishedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 5),
            PublishedByUserId = AotDataReaderExtensions.ReadNullableGuid(reader, 6),
            ReadAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 7),
        };

    private static AnnouncementReadReceiptRecord ReadAnnouncementReadReceiptRecord(
        DbDataReader reader) =>
        new()
        {
            UserId = reader.GetGuid(0),
            ReadAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 1),
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
            ArchivedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 12),
            RevokedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 13),
            RevocationReason = AotDataReaderExtensions.ReadNullableString(reader, 14),
            DeepLinkJson = AotDataReaderExtensions.ReadNullableString(reader, 15),
            ProducerKey = AotDataReaderExtensions.ReadNullableString(reader, 16),
            SceneKey = AotDataReaderExtensions.ReadNullableString(reader, 17),
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

    /// <summary>按模板 SQL 的完整投影顺序读取，包括内容类别及草稿参数架构。</summary>
    /// <param name="reader">当前模板投影读取器。</param>
    /// <returns>字段顺序与查询一致的模板记录。</returns>
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
            reader.GetString(11),
            AotDataReaderExtensions.ReadInt64(reader, 12),
            AotDataReaderExtensions.ReadNullableGuid(reader, 13),
            reader.GetGuid(14),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 15),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 16),
            AotDataReaderExtensions.ReadInt64(reader, 17));

    /// <summary>按模板 SQL 的完整投影顺序读取，包括内容类别及草稿参数架构。</summary>
    /// <param name="reader">当前模板投影读取器。</param>
    /// <returns>字段顺序与查询一致的模板记录。</returns>
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
            reader.GetString(11),
            AotDataReaderExtensions.ReadInt64(reader, 12),
            AotDataReaderExtensions.ReadNullableGuid(reader, 13),
            reader.IsDBNull(14) ? null : AotDataReaderExtensions.ReadInt32(reader, 14),
            AotDataReaderExtensions.ReadNullableString(reader, 15),
            AotDataReaderExtensions.ReadNullableString(reader, 16),
            reader.GetGuid(17),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 18),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 19),
            AotDataReaderExtensions.ReadInt64(reader, 20));

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

    private static NotificationIntentAttachmentRecord ReadIntentAttachment(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetInt32(3),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 4));

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
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 13),
            AotDataReaderExtensions.ReadNullableString(reader, 14),
            reader.IsDBNull(15) ? null : AotDataReaderExtensions.ReadInt64(reader, 15),
            AotDataReaderExtensions.ReadNullableString(reader, 16),
            AotDataReaderExtensions.ReadNullableString(reader, 17));

    private static NotificationPreferenceRecord ReadPreference(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetGuid(4),
            reader.GetString(5),
            reader.GetBoolean(6),
            reader.GetBoolean(7),
            AotDataReaderExtensions.ReadNullableString(reader, 8),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 9),
            AotDataReaderExtensions.ReadInt64(reader, 10));

    /// <summary>读取受控 Producer/Scene 目录行；列顺序必须与 NotificationPlatformSql 的目录投影完全一致。</summary>
    private static NotificationProducerSceneRecord ReadProducerScene(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            AotDataReaderExtensions.ReadBoolean(reader, 8),
            AotDataReaderExtensions.ReadBoolean(reader, 9),
            reader.GetGuid(10),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 11),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 12),
            AotDataReaderExtensions.ReadInt64(reader, 13));

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

    /// <summary>读取配额政策行；列顺序必须与 NotificationPlatformSql 的配额政策投影完全一致。</summary>
    private static NotificationQuotaPolicyRecord ReadQuotaPolicy(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            AotDataReaderExtensions.ReadInt32(reader, 6),
            AotDataReaderExtensions.ReadInt32(reader, 7),
            AotDataReaderExtensions.ReadBoolean(reader, 8),
            reader.GetGuid(9),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 10),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 11),
            AotDataReaderExtensions.ReadInt64(reader, 12));

    /// <summary>读取按维度解析出的生效配额政策行。</summary>
    private static NotificationQuotaResolutionRecord ReadQuotaResolution(DbDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetGuid(1),
            AotDataReaderExtensions.ReadInt32(reader, 2),
            AotDataReaderExtensions.ReadInt32(reader, 3));

    private static NotificationQuotaCounterRecord ReadQuotaCounter(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadInt32(reader, 1),
            AotDataReaderExtensions.ReadInt32(reader, 2),
            AotDataReaderExtensions.ReadInt32(reader, 3));

    private static NotificationQuotaReservationRecord ReadQuotaReservation(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 3));

    private static NotificationQuotaAbandonedReservationRecord ReadQuotaAbandonedReservation(
        DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 3),
            AotDataReaderExtensions.ReadInt64(reader, 4));

    private static NotificationQuotaOutstandingRecord ReadQuotaOutstanding(DbDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            AotDataReaderExtensions.ReadInt64(reader, 2));
}
#endif
