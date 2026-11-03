#nullable enable

using System;

namespace Acme.Remediation.Generated;

internal sealed record OrganizationTreeNodeRecord(
    Guid Id,
    Guid TenantId,
    Guid OrganizationUnitId,
    string Name,
    Guid? ParentId,
    long Version,
    bool IsDeleted,
    DateTimeOffset? DeletedAtUtc,
    Guid? DeletedById);

// 物化器在模块注册时同步安装，禁止首个请求依赖反射推导 Record 构造函数。
internal static class OrganizationTreeNodeGeneratedMaterializer
{
#if FULLNET_AOT_COMPILE
    internal static void Register() =>
        Full.NET.Data.Dapper.DapperAotMaterializerRegistry.Register<OrganizationTreeNodeRecord>(Read);
#endif

    internal static OrganizationTreeNodeRecord Read(System.Data.Common.DbDataReader reader) => new(
        reader.GetGuid(reader.GetOrdinal("Id")),
        reader.GetGuid(reader.GetOrdinal("TenantId")),
        reader.GetGuid(reader.GetOrdinal("OrganizationUnitId")),
        reader.GetString(reader.GetOrdinal("Name")),
        reader.IsDBNull(reader.GetOrdinal("ParentId")) ? null : reader.GetGuid(reader.GetOrdinal("ParentId")),
        Convert.ToInt64(reader.GetValue(reader.GetOrdinal("Version")), System.Globalization.CultureInfo.InvariantCulture),
        Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("IsDeleted")), System.Globalization.CultureInfo.InvariantCulture),
        reader.IsDBNull(reader.GetOrdinal("DeletedAtUtc")) ? null : (reader.GetFieldType(reader.GetOrdinal("DeletedAtUtc")) == typeof(DateTimeOffset) ? reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("DeletedAtUtc")).ToUniversalTime() : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("DeletedAtUtc")), DateTimeKind.Utc))),
        reader.IsDBNull(reader.GetOrdinal("DeletedById")) ? null : reader.GetGuid(reader.GetOrdinal("DeletedById")));
}
