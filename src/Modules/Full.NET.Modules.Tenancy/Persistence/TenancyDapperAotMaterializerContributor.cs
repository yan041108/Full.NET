#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Tenancy.Features.ManageHostTenantPackages;
using Full.NET.Modules.Tenancy.Features.ManageHostTenants;
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
        registrar.Register<TenantPackageRecord>(ReadTenantPackageRecord);
        registrar.Register<TenantPackageIdentityRecord>(ReadTenantPackageIdentityRecord);
        registrar.Register<LocalTenantSeedSummary>(ReadLocalTenantSeedSummary);
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
            AotDataReaderExtensions.ReadNullableString(reader, 9));

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
}
#endif
