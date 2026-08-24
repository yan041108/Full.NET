#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Organization.Persistence;

namespace Full.NET.Modules.Organization.Persistence;

/// <summary>
/// Organization 模块 Native AOT 行物化器注册。
/// </summary>
internal sealed class OrganizationDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<OrganizationUnitListRow>(ReadOrganizationUnitListRow);
    }

    private static OrganizationUnitListRow ReadOrganizationUnitListRow(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            ParentId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            Code = reader.GetString(2),
            Name = reader.GetString(3),
            DisplayOrder = reader.GetInt32(4),
            IsActive = AotDataReaderExtensions.ReadBoolean(reader, 5),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 7),
            Version = reader.GetInt32(8),
        };
}
#endif
