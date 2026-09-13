using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.Modules.Ai.Mcp;

/// <summary>保护 MCP 服务主体令牌；不得与用户访问令牌混用。</summary>
internal sealed class AiMcpRemoteTokenProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("Full.NET.Ai.Mcp.Remote.ServiceToken");

    internal string Protect(string token) => protector.Protect(token);

    internal string Unprotect(string protectedToken) => protector.Unprotect(protectedToken);
}
