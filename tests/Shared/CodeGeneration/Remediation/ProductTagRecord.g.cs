#nullable enable

using System;

namespace Acme.Remediation.Generated;

internal sealed record ProductTagRecord(
    Guid Id,
    Guid TenantId,
    Guid ProductReferenceId,
    Guid TagId,
    long Version,
    bool IsDeleted,
    DateTimeOffset? DeletedAtUtc,
    Guid? DeletedById);

// 物化器在模块注册时同步安装，禁止首个请求依赖反射推导 Record 构造函数。
internal static class ProductTagGeneratedMaterializer
{
#if FULLNET_AOT_COMPILE
    internal static void Register() =>
        Full.NET.Data.Dapper.DapperAotMaterializerRegistry.Register<ProductTagRecord>(Read);
#endif

    internal static ProductTagRecord Read(System.Data.Common.DbDataReader reader) => new(
        reader.GetGuid(reader.GetOrdinal("Id")),
        reader.GetGuid(reader.GetOrdinal("TenantId")),
        reader.GetGuid(reader.GetOrdinal("ProductReferenceId")),
        reader.GetGuid(reader.GetOrdinal("TagId")),
        Convert.ToInt64(reader.GetValue(reader.GetOrdinal("Version")), System.Globalization.CultureInfo.InvariantCulture),
        Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("IsDeleted")), System.Globalization.CultureInfo.InvariantCulture),
        reader.IsDBNull(reader.GetOrdinal("DeletedAtUtc")) ? null : (reader.GetFieldType(reader.GetOrdinal("DeletedAtUtc")) == typeof(DateTimeOffset) ? reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("DeletedAtUtc")).ToUniversalTime() : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("DeletedAtUtc")), DateTimeKind.Utc))),
        reader.IsDBNull(reader.GetOrdinal("DeletedById")) ? null : reader.GetGuid(reader.GetOrdinal("DeletedById")));
}
