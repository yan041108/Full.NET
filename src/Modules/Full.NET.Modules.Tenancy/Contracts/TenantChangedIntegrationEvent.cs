using global::MemoryPack;

namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>表示会影响租户解析结果的租户状态已经提交变更。</summary>
[MemoryPackable]
public partial record TenantChangedIntegrationEvent(
    Guid TenantId,
    string Domain);
