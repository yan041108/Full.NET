using Full.NET.Data.Abstractions;
using global::MemoryPack;

namespace Full.NET.Modules.Identity.Features.HostFileReferences;

/// <summary>已退役的媒体绑定事实；ClaimKey 指定旧代际，不从消费时的当前档案推导。</summary>
[MemoryPackable]
internal sealed partial record IdentityProfileMediaUnboundIntegrationEvent(Guid UserId, Guid FileId, string MediaKind, string ClaimKey);

/// <summary>媒体变更的事务事实入口；使用可信环境信封，不覆盖租户或请求元数据。</summary>
internal static class IdentityProfileMediaUnboundOutbox
{
    internal const string EventType = "fullnet.identity.profile_media.unbound";
    internal const int SchemaVersion = 1;
    internal static Task PublishAsync(IOutboxWriter writer, Guid userId, Guid fileId, string kind, string key, CancellationToken ct) =>
        writer.AddAsync(EventType, SchemaVersion, new IdentityProfileMediaUnboundIntegrationEvent(userId, fileId, kind, key), ct);
}
