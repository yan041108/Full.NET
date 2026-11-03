using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Persistence;

/// <summary>固定 30 列恢复投影；原会话及来源摘要不接受后续请求覆盖。</summary>
internal sealed record EnterpriseRequestDeliveryRecord(Guid Id, Guid TenantId, Guid RequestId, Guid RequestKey, string Kind,
    long RequestedVersion, Guid ActorUserId, Guid SessionId, string SecurityStamp, string ActorScope, string EffectiveScope,
    string SessionKind, string SourceDigest, bool IncludesAmounts, string LocaleCode, Guid? FileId, string FileName,
    string ContentType, long? SizeBytes, string? ContentHash, string Phase, string? ErrorCode, Guid? LeaseId,
    DateTimeOffset? LeaseUntilUtc, int Attempt, DateTimeOffset DueAtUtc, DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, long Version)
{
    internal SessionBindingSnapshot Binding() => new(ActorUserId, TenantId, SessionId, SecurityStamp, ActorScope, EffectiveScope, SessionKind);
    internal string Owner => Kind switch { "Attachment" => "enterprise_request_attachment", "Report" => "enterprise_request_report",
        _ => throw new InvalidOperationException("Unknown delivery resource kind.") };
    internal EnterpriseRequestAttachmentResponse Attachment() => new(Id, RequestId, FileName, ContentType, SizeBytes ?? 0, Phase, Version, CreatedAtUtc);
    internal EnterpriseRequestReportResponse Report() => new(Id, RequestId, Phase, ErrorCode, Attempt, IncludesAmounts, CreatedAtUtc, ExpiresAtUtc, Version);
}

/// <summary>应用自有物化器保持 Native AOT 静态闭包。</summary>
internal static class EnterpriseRequestDeliveryMaterializer
{
    internal static void Register()
    {
#if FULLNET_AOT_COMPILE
        Full.NET.Data.Dapper.DapperAotMaterializerRegistry.Register<EnterpriseRequestSearchHit>(reader =>
            new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        Full.NET.Data.Dapper.DapperAotMaterializerRegistry.Register<EnterpriseRequestDeliveryRecord>(reader => new(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetString(4),
            reader.GetInt64(5), reader.GetGuid(6), reader.GetGuid(7), reader.GetString(8), reader.GetString(9), reader.GetString(10),
            reader.GetString(11), reader.GetString(12), reader.GetBoolean(13), reader.GetString(14), reader.IsDBNull(15) ? null : reader.GetGuid(15),
            reader.GetString(16), reader.GetString(17), reader.IsDBNull(18) ? null : reader.GetInt64(18), reader.IsDBNull(19) ? null : reader.GetString(19),
            reader.GetString(20), reader.IsDBNull(21) ? null : reader.GetString(21), reader.IsDBNull(22) ? null : reader.GetGuid(22),
            reader.IsDBNull(23) ? null : global::Full.NET.Data.Dapper.AotDataReaderExtensions.ReadDateTimeOffset(reader, 23), reader.GetInt32(24),
            global::Full.NET.Data.Dapper.AotDataReaderExtensions.ReadDateTimeOffset(reader, 25), global::Full.NET.Data.Dapper.AotDataReaderExtensions.ReadDateTimeOffset(reader, 26),
            global::Full.NET.Data.Dapper.AotDataReaderExtensions.ReadDateTimeOffset(reader, 27), global::Full.NET.Data.Dapper.AotDataReaderExtensions.ReadDateTimeOffset(reader, 28), reader.GetInt64(29)));
#endif
    }
}
