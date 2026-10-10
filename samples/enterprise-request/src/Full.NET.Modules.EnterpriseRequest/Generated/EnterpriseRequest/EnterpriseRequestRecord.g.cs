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

#if FULLNET_AOT_COMPILE
/// <summary>按生成 SQL 的固定投影顺序物化记录，避免原生运行时反射构造。</summary>
internal static class EnterpriseRequestRecordAotMaterializer
{
    internal static void Register() => new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar()
        .Register<EnterpriseRequestRecord>(reader => new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetDecimal(6),
            reader.GetGuid(7),
            global::Full.NET.Data.Dapper.AotDataReaderExtensions.ReadInt64(reader, 8),
            global::Full.NET.Data.Dapper.AotDataReaderExtensions.ReadDateTimeOffset(reader, 9),
            reader.GetGuid(10),
            reader.IsDBNull(11) ? (DateTimeOffset?)null : global::Full.NET.Data.Dapper.AotDataReaderExtensions.ReadDateTimeOffset(reader, 11),
            reader.IsDBNull(12) ? (Guid?)null : reader.GetGuid(12),
            global::Full.NET.Data.Dapper.AotDataReaderExtensions.ReadBoolean(reader, 13),
            reader.IsDBNull(14) ? (DateTimeOffset?)null : global::Full.NET.Data.Dapper.AotDataReaderExtensions.ReadDateTimeOffset(reader, 14),
            reader.IsDBNull(15) ? (Guid?)null : reader.GetGuid(15)));
}
#endif
