using global::MessagePack;

namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>表示会影响租户解析结果的租户状态已经提交变更。</summary>
[MessagePackObject]
public sealed record TenantChangedIntegrationEvent(
    [property: Key(0)] Guid TenantId,
    [property: Key(1)] string Domain);
