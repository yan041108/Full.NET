using global::MemoryPack;

namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>
/// 表示新租户已成功开通并完成持久化提交的集成事件。
/// </summary>
/// <param name="TenantId">新开通租户的稳定标识。</param>
/// <param name="Identifier">新租户的稳定编码。</param>
/// <param name="Domain">新租户的主域名。</param>
[MemoryPackable]
public partial record TenantProvisionedIntegrationEvent(
    Guid TenantId,
    string Identifier,
    string Domain);
