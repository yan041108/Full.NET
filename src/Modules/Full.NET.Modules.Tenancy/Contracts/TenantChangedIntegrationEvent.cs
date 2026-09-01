using global::MemoryPack;

namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>表示会影响租户解析结果的租户状态已经提交变更。</summary>
/// <param name="TenantId">发生状态变更的租户标识。</param>
/// <param name="Domain">变更后仍生效的主域名，用于缓存失效键。</param>
[MemoryPackable]
public partial record TenantChangedIntegrationEvent(
    Guid TenantId,
    string Domain);
