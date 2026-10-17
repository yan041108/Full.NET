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

#if FULLNET_AOT_COMPILE
/// <summary>按生成 SQL 的固定投影顺序物化记录，避免原生运行时反射构造。</summary>
internal static class ProductRecordAotMaterializer
{
    internal static void Register() => new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar()
        .Register<ProductRecord>(reader => new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? (string?)null : reader.GetString(3),
            global::Full.NET.Data.Dapper.AotDataReaderExtensions.ReadBoolean(reader, 4),
            global::Full.NET.Data.Dapper.AotDataReaderExtensions.ReadInt64(reader, 5),
            global::Full.NET.Data.Dapper.AotDataReaderExtensions.ReadDateTimeOffset(reader, 6)));
}
#endif
