#nullable enable

using System;

namespace Acme.Modules.Catalog.Generated;

internal sealed record ProductRecord(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Description,
    bool IsActive,
    long Version,
    DateTimeOffset CreatedAtUtc);

// 物化器在模块注册时同步安装，禁止首个请求依赖反射推导 Record 构造函数。
internal static class ProductGeneratedMaterializer
{
#if FULLNET_AOT_COMPILE
    internal static void Register() =>
        Full.NET.Data.Dapper.DapperAotMaterializerRegistry.Register<ProductRecord>(Read);
#endif

    internal static ProductRecord Read(System.Data.Common.DbDataReader reader) => new(
        reader.GetGuid(reader.GetOrdinal("Id")),
        reader.GetGuid(reader.GetOrdinal("TenantId")),
        reader.GetString(reader.GetOrdinal("Name")),
        reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
        Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("IsActive")), System.Globalization.CultureInfo.InvariantCulture),
        Convert.ToInt64(reader.GetValue(reader.GetOrdinal("Version")), System.Globalization.CultureInfo.InvariantCulture),
        (reader.GetFieldType(reader.GetOrdinal("CreatedAtUtc")) == typeof(DateTimeOffset) ? reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("CreatedAtUtc")).ToUniversalTime() : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("CreatedAtUtc")), DateTimeKind.Utc))));
}
