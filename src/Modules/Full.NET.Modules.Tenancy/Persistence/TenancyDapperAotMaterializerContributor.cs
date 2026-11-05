#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements;
using Full.NET.Modules.Tenancy.Features.ManageHostTenantPackages;
using Full.NET.Modules.Tenancy.Features.ManageHostTenants;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements.Persistence;
using Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions.Persistence;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota.Persistence;
using Full.NET.Modules.Tenancy.Seeding;
using Full.NET.Modules.Tenancy.Features.SubscriptionBilling;

namespace Full.NET.Modules.Tenancy.Persistence;

/// <summary>
/// Tenancy 模块 Native AOT 行物化器注册。
/// </summary>
internal sealed class TenancyDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    /// <summary>迁移播种只注册租户摘要、权益目录与启动状态；不引入后台或 HTTP 服务。</summary>
    internal static void RegisterMigrationMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<LocalTenantSeedSummary>(ReadLocalTenantSeedSummary);
        registrar.Register<TenantEntitlementCatalogRecord>(ReadTenantEntitlementCatalogRecord);
        registrar.Register<TenantEntitlementEnforcementRecord>(ReadTenantEntitlementEnforcementRecord);
    }

    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        RegisterMigrationMaterializers(registrar);
        registrar.Register<Features.ManageTenantQuota.TenantQuotaUsageRow>(r => new(r.GetGuid(0), AotDataReaderExtensions.ReadBoolean(r, 1),
            AotDataReaderExtensions.ReadNullableGuid(r, 2), AotDataReaderExtensions.ReadNullableString(r, 3), AotDataReaderExtensions.ReadNullableString(r, 4),
            r.IsDBNull(5) ? null : r.GetInt64(5), r.IsDBNull(6) ? null : r.GetInt64(6), r.IsDBNull(7) ? null : r.GetInt64(7)));
        registrar.Register<TenantPackageOfferRecord>(r => new(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2),
            r.GetInt64(3), r.GetString(4), r.GetInt64(5), r.GetInt64(6), AotDataReaderExtensions.ReadInt32(r, 7),
            AotDataReaderExtensions.ReadBoolean(r, 8), AotDataReaderExtensions.ReadInt32(r, 9)));
        registrar.Register<SubscriptionEntitlementRecord>(r => new(r.GetGuid(0), r.GetString(1), r.GetString(2), AotDataReaderExtensions.ReadBoolean(r, 3)));
        registrar.Register<InvoiceEntitlementRecord>(r => new(r.GetGuid(0), r.GetGuid(1), r.GetString(2)));
        registrar.Register<SubscriptionInvoiceRecord>(r => new(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetString(3),
            r.GetGuid(4), AotDataReaderExtensions.ReadInt32(r, 5), AotDataReaderExtensions.ReadInt32(r, 6), r.GetGuid(7), r.GetGuid(8),
            r.GetGuid(9), AotDataReaderExtensions.ReadNullableGuid(r, 10), AotDataReaderExtensions.ReadNullableString(r, 11),
            r.GetInt64(12), r.GetString(13), r.GetInt64(14), r.GetInt64(15), AotDataReaderExtensions.ReadDateTimeOffset(r, 16),
            AotDataReaderExtensions.ReadDateTimeOffset(r, 17), r.GetString(18), AotDataReaderExtensions.ReadNullableGuid(r, 19),
            r.GetInt64(20), AotDataReaderExtensions.ReadDateTimeOffset(r, 21), AotDataReaderExtensions.ReadDateTimeOffset(r, 22),
            AotDataReaderExtensions.ReadInt32(r, 23), r.GetString(24)));
        registrar.Register<HostTenantRecord>(ReadHostTenantRecord);
        registrar.Register<TenantResolutionRecord>(ReadTenantResolutionRecord);
        registrar.Register<TenantBrandingRecord>(ReadTenantBrandingRecord);
        registrar.Register<TenantPackageRecord>(ReadTenantPackageRecord);
        registrar.Register<TenantPackageIdentityRecord>(ReadTenantPackageIdentityRecord);
        registrar.Register<TenantEntitlementBindingRecord>(ReadTenantEntitlementBindingRecord);
        registrar.Register<TenantSubscriptionRecord>(ReadTenantSubscriptionRecord);
        registrar.Register<TenantQuotaMetricRecord>(ReadTenantQuotaMetricRecord);
        registrar.Register<TenantQuotaUsageBaselineRecord>(ReadTenantQuotaUsageBaselineRecord);
        registrar.Register<TenantQuotaReservationRecord>(ReadTenantQuotaReservationRecord);
        registrar.Register<OutstandingReservationRow>(ReadOutstandingReservationRow);
        registrar.Register<TenantQuotaMetricBackfillCandidate>(ReadTenantQuotaMetricBackfillCandidate);
        registrar.Register<TenantEntitlementBackfillCandidate>(ReadTenantEntitlementBackfillCandidate);
    }

    private static TenantResolutionRecord ReadTenantResolutionRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadBoolean(reader, 4),
            reader.GetInt32(5),
            reader.GetString(6));

    private static HostTenantRecord ReadHostTenantRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadBoolean(reader, 4),
            reader.GetInt32(5),
            reader.GetString(6),
            AotDataReaderExtensions.ReadNullableGuid(reader, 7),
            AotDataReaderExtensions.ReadNullableString(reader, 8),
            AotDataReaderExtensions.ReadNullableString(reader, 9),
            reader.GetString(10),
            AotDataReaderExtensions.ReadNullableGuid(reader, 11),
            reader.GetString(12),
            AotDataReaderExtensions.ReadNullableString(reader, 13));

    private static TenantBrandingRecord ReadTenantBrandingRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableString(reader, 1),
            AotDataReaderExtensions.ReadNullableGuid(reader, 2),
            AotDataReaderExtensions.ReadNullableString(reader, 3),
            AotDataReaderExtensions.ReadNullableString(reader, 4),
            AotDataReaderExtensions.ReadNullableString(reader, 5),
            AotDataReaderExtensions.ReadNullableString(reader, 6),
            reader.GetInt32(7),
            reader.FieldCount > 8 ? AotDataReaderExtensions.ReadNullableString(reader, 8) : null);

    private static TenantPackageRecord ReadTenantPackageRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            AotDataReaderExtensions.ReadNullableString(reader, 3),
            AotDataReaderExtensions.ReadBoolean(reader, 4),
            reader.GetInt32(5),
            reader.GetInt64(6));

    private static TenantPackageIdentityRecord ReadTenantPackageIdentityRecord(
        DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            AotDataReaderExtensions.ReadNullableString(reader, 3),
            AotDataReaderExtensions.ReadBoolean(reader, 4),
            reader.GetInt32(5));

    private static LocalTenantSeedSummary ReadLocalTenantSeedSummary(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadBoolean(reader, 4),
            reader.GetInt32(5));

    private static TenantEntitlementCatalogRecord ReadTenantEntitlementCatalogRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            AotDataReaderExtensions.ReadNullableString(reader, 3),
            reader.GetString(4),
            AotDataReaderExtensions.ReadBoolean(reader, 5),
            reader.GetInt32(6));

    private static TenantEntitlementBindingRecord ReadTenantEntitlementBindingRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 5),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 6),
            AotDataReaderExtensions.ReadNullableGuid(reader, 7),
            reader.GetInt32(8));

    private static TenantEntitlementEnforcementRecord ReadTenantEntitlementEnforcementRecord(
        DbDataReader reader) =>
        new(reader.GetString(0), reader.GetInt32(1));

    private static TenantSubscriptionRecord ReadTenantSubscriptionRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            AotDataReaderExtensions.ReadNullableGuid(reader, 2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 4),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 5),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 7),
            reader.GetInt32(8));

    private static TenantQuotaMetricRecord ReadTenantQuotaMetricRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt32(7),
            AotDataReaderExtensions.ReadNullableGuid(reader, 8));

    /// <summary>缺失指标与未指定游标使用数据库 NULL，原生路径同样保留可选标识。</summary>
    private static TenantQuotaUsageBaselineRecord ReadTenantQuotaUsageBaselineRecord(DbDataReader reader) =>
        new(
            reader.IsDBNull(0) ? null : reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt32(7));

    private static TenantQuotaReservationRecord ReadTenantQuotaReservationRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt64(4),
            reader.GetString(5),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
            reader.GetInt32(7),
            reader.IsDBNull(8) ? null : reader.GetGuid(8));

    // 对账查询与普通指标读取共享双库 UUID 路径，保持历史预留投影的四列顺序。
    private static OutstandingReservationRow ReadOutstandingReservationRow(DbDataReader reader) =>
        new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetInt32(3));

    private static TenantQuotaMetricBackfillCandidate ReadTenantQuotaMetricBackfillCandidate(DbDataReader reader) =>
        new(reader.GetGuid(0));

    private static TenantEntitlementBackfillCandidate ReadTenantEntitlementBackfillCandidate(
        DbDataReader reader) =>
        new(reader.GetGuid(0));
}
#endif
