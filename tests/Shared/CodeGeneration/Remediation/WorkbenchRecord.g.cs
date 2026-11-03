#nullable enable

using System;

namespace Acme.Remediation.Generated;

internal sealed record WorkbenchRecord(
    Guid Id,
    Guid TenantId,
    string Name,
    long Quantity,
    decimal Price,
    bool? IsReady,
    Guid? ReferenceId,
    DateTimeOffset? OccurredAtUtc,
    long Version,
    bool IsDeleted,
    DateTimeOffset? DeletedAtUtc,
    Guid? DeletedById);

// 物化器在模块注册时同步安装，禁止首个请求依赖反射推导 Record 构造函数。
internal static class WorkbenchGeneratedMaterializer
{
#if FULLNET_AOT_COMPILE
    internal static void Register() =>
        Full.NET.Data.Dapper.DapperAotMaterializerRegistry.Register<WorkbenchRecord>(Read);
#endif

    internal static WorkbenchRecord Read(System.Data.Common.DbDataReader reader) => new(
        reader.GetGuid(reader.GetOrdinal("Id")),
        reader.GetGuid(reader.GetOrdinal("TenantId")),
        reader.GetString(reader.GetOrdinal("Name")),
        Convert.ToInt64(reader.GetValue(reader.GetOrdinal("Quantity")), System.Globalization.CultureInfo.InvariantCulture),
        Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("Price")), System.Globalization.CultureInfo.InvariantCulture),
        reader.IsDBNull(reader.GetOrdinal("IsReady")) ? null : Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("IsReady")), System.Globalization.CultureInfo.InvariantCulture),
        reader.IsDBNull(reader.GetOrdinal("ReferenceId")) ? null : reader.GetGuid(reader.GetOrdinal("ReferenceId")),
        reader.IsDBNull(reader.GetOrdinal("OccurredAtUtc")) ? null : (reader.GetFieldType(reader.GetOrdinal("OccurredAtUtc")) == typeof(DateTimeOffset) ? reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("OccurredAtUtc")).ToUniversalTime() : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("OccurredAtUtc")), DateTimeKind.Utc))),
        Convert.ToInt64(reader.GetValue(reader.GetOrdinal("Version")), System.Globalization.CultureInfo.InvariantCulture),
        Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("IsDeleted")), System.Globalization.CultureInfo.InvariantCulture),
        reader.IsDBNull(reader.GetOrdinal("DeletedAtUtc")) ? null : (reader.GetFieldType(reader.GetOrdinal("DeletedAtUtc")) == typeof(DateTimeOffset) ? reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("DeletedAtUtc")).ToUniversalTime() : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("DeletedAtUtc")), DateTimeKind.Utc))),
        reader.IsDBNull(reader.GetOrdinal("DeletedById")) ? null : reader.GetGuid(reader.GetOrdinal("DeletedById")));
}
