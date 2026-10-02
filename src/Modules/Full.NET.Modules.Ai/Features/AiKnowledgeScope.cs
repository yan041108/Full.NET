using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Features;

/// <summary>知识库范围仅由可信 Host 或租户上下文确定。</summary>
internal static class AiKnowledgeScope
{
    internal static bool IsTenant(ICurrentTenant tenant)
    {
        if (tenant.IsHost) return false;
        if (tenant.IsAvailable && tenant.Id is { } id && id != Guid.Empty) return true;
        throw new TenantContextMissingException("ai.knowledge.tenant_context_required");
    }
}
