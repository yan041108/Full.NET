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

namespace Full.NET.Modules.Tenancy.Persistence;

/// <summary>
/// Tenancy 模块 Native AOT 行物化器注册。
/// </summary>
internal sealed class TenancyDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<HostTenantRecord>(ReadHostTenantRecord);
        registrar.Register<TenantResolutionRecord>(ReadTenantResolutionRecord);
        registrar.Register<TenantBrandingRecord>(ReadTenantBrandingRecord);
        registrar.Register<TenantPackageRecord>(ReadTenantPackageRecord);
        registrar.Register<TenantPackageIdentityRecord>(ReadTenantPackageIdentityRecord);
        registrar.Register<LocalTenantSeedSummary>(ReadLocalTenantSeedSummary);
        registrar.Register<TenantEntitlementCatalogRecord>(ReadTenantEntitlementCatalogRecord);
        registrar.Register<TenantEntitlementBindingRecord>(ReadTenantEntitlementBindingRecord);
        registrar.Register<TenantEntitlementEnforcementRecord>(ReadTenantEntitlementEnforcementRecord);
        registrar.Register<TenantSubscriptionRecord>(ReadTenantSubscriptionRecord);
        registrar.Register<TenantQuotaMetricRecord>(ReadTenantQuotaMetricRecord);
        registrar.Register<TenantQuotaReservationRecord>(ReadTenantQuotaReservationRecord);
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
            reader.GetInt32(7));

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

    private static TenantEntitlementBackfillCandidate ReadTenantEntitlementBackfillCandidate(
        DbDataReader reader) =>
        new(reader.GetGuid(0));
}
#endif
