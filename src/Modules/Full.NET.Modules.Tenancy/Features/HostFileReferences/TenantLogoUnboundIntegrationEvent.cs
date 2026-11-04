using Full.NET.Data.Abstractions;
using global::MemoryPack;

namespace Full.NET.Modules.Tenancy.Features.HostFileReferences;

/// <summary>Logo 已退役绑定的事务事实，租户标识用于校验可信信封而非覆盖作用域。</summary>
[MemoryPackable]
internal sealed partial record TenantLogoUnboundIntegrationEvent(Guid TenantId, Guid FileId, string ClaimKey);

/// <summary>绑定变更的可靠发布入口；必须处于 Tenancy 本地命令事务。</summary>
internal static class TenantLogoUnboundOutbox
{
    internal const string EventType = "fullnet.tenancy.logo.unbound";
    internal const int SchemaVersion = 1;
    internal static Task PublishAsync(IOutboxWriter writer, Guid tenantId, Guid fileId, string key, CancellationToken ct) =>
        writer.AddAsync(EventType, SchemaVersion, new TenantLogoUnboundIntegrationEvent(tenantId, fileId, key), ct);
}
