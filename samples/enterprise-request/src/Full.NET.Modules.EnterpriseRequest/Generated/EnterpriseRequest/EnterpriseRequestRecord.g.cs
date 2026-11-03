#nullable enable

using System;

namespace Full.NET.Modules.EnterpriseRequest.Generated;

internal sealed record EnterpriseRequestRecord(
    Guid Id,
    Guid TenantId,
    Guid OrganizationUnitId,
    string RequestNumber,
    string Title,
    string Status,
    decimal TotalAmount,
    Guid ApplicantUserId,
    long Version,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedById,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedById,
    bool IsDeleted,
    DateTimeOffset? DeletedAtUtc,
    Guid? DeletedById);

// 物化器在模块注册时同步安装，禁止首个请求依赖反射推导 Record 构造函数。
internal static class EnterpriseRequestGeneratedMaterializer
{
#if FULLNET_AOT_COMPILE
    internal static void Register() =>
        Full.NET.Data.Dapper.DapperAotMaterializerRegistry.Register<EnterpriseRequestRecord>(Read);
#endif

    internal static EnterpriseRequestRecord Read(System.Data.Common.DbDataReader reader) => new(
        reader.GetGuid(reader.GetOrdinal("Id")),
        reader.GetGuid(reader.GetOrdinal("TenantId")),
        reader.GetGuid(reader.GetOrdinal("OrganizationUnitId")),
        reader.GetString(reader.GetOrdinal("RequestNumber")),
        reader.GetString(reader.GetOrdinal("Title")),
        reader.GetString(reader.GetOrdinal("Status")),
        Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("TotalAmount")), System.Globalization.CultureInfo.InvariantCulture),
        reader.GetGuid(reader.GetOrdinal("ApplicantUserId")),
        Convert.ToInt64(reader.GetValue(reader.GetOrdinal("Version")), System.Globalization.CultureInfo.InvariantCulture),
        (reader.GetFieldType(reader.GetOrdinal("CreatedAtUtc")) == typeof(DateTimeOffset) ? reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("CreatedAtUtc")).ToUniversalTime() : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("CreatedAtUtc")), DateTimeKind.Utc))),
        reader.GetGuid(reader.GetOrdinal("CreatedById")),
        reader.IsDBNull(reader.GetOrdinal("UpdatedAtUtc")) ? null : (reader.GetFieldType(reader.GetOrdinal("UpdatedAtUtc")) == typeof(DateTimeOffset) ? reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("UpdatedAtUtc")).ToUniversalTime() : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("UpdatedAtUtc")), DateTimeKind.Utc))),
        reader.IsDBNull(reader.GetOrdinal("UpdatedById")) ? null : reader.GetGuid(reader.GetOrdinal("UpdatedById")),
        Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("IsDeleted")), System.Globalization.CultureInfo.InvariantCulture),
        reader.IsDBNull(reader.GetOrdinal("DeletedAtUtc")) ? null : (reader.GetFieldType(reader.GetOrdinal("DeletedAtUtc")) == typeof(DateTimeOffset) ? reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("DeletedAtUtc")).ToUniversalTime() : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("DeletedAtUtc")), DateTimeKind.Utc))),
        reader.IsDBNull(reader.GetOrdinal("DeletedById")) ? null : reader.GetGuid(reader.GetOrdinal("DeletedById")));
}
