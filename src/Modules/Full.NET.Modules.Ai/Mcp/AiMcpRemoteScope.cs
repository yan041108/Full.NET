using Full.NET.Abstractions.Tenancy;

namespace Full.NET.Modules.Ai.Mcp;

/// <summary>解析 MCP 远端连接的权威作用域键。</summary>
internal static class AiMcpRemoteScope
{
    internal static (string ScopeKey, Guid? TenantId) Resolve(ICurrentTenant tenant) =>
        tenant.Id is { } tenantId
            ? (tenantId.ToString("N"), tenantId)
            : tenant.IsHost
                ? ("host", null)
                : throw new InvalidOperationException("Tenant scope is required for MCP remote connections.");
}
