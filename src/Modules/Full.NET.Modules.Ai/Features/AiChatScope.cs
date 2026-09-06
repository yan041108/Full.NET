using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Features;

/// <summary>将可信租户上下文收敛为聊天会话作用域。</summary>
internal readonly record struct AiChatScope(Guid? TenantId)
{
    /// <summary>从受信租户上下文解析聊天读写作用域。</summary>
    /// <param name="currentTenant">当前可信租户上下文。</param>
    /// <returns>Host 返回 null TenantId，Tenant 返回当前租户标识。</returns>
    /// <exception cref="TenantContextMissingException">租户会话缺少可用租户标识时抛出。</exception>
    public static AiChatScope Resolve(ICurrentTenant currentTenant)
    {
        if (currentTenant.IsHost)
        {
            return new(null);
        }

        if (currentTenant.IsAvailable && currentTenant.Id is { } tenantId)
        {
            return new(tenantId);
        }

        throw new TenantContextMissingException("ai.chat.tenant_context_required");
    }
}
