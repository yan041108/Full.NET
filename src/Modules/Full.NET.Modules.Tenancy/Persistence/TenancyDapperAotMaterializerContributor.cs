#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Tenancy.Features.ManageHostTenants;

namespace Full.NET.Modules.Tenancy.Persistence;

/// <summary>
/// Tenancy 模块 Native AOT 行物化器注册。
/// </summary>
internal sealed class TenancyDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<HostTenantRecord>(ReadHostTenantRecord);
    }

    private static HostTenantRecord ReadHostTenantRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetBoolean(4),
            reader.GetInt32(5),
            reader.GetString(6),
            AotDataReaderExtensions.ReadNullableGuid(reader, 7),
            AotDataReaderExtensions.ReadNullableString(reader, 8),
            AotDataReaderExtensions.ReadNullableString(reader, 9));
}
#endif
