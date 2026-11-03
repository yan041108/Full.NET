using MemoryPack;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>本模块 API 到 Worker 的可靠退出意图；客户端目的地在撤销事务中冻结，不含令牌。</summary>
[MemoryPackable]
internal partial record IdentityOidcLogoutRequested(Guid Id, Guid UserId, Guid CenterSessionId,
    string ClientId, string TargetUri)
{
    public const string EventType = "fullnet.identity.oidc_logout.requested";
}
