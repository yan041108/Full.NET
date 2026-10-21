using Full.NET.Modules.Identity.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>知识用例的权限替身按集合检查全部规则；保留既有逐权限拒绝和撤销故障注入。</summary>
internal static class KnowledgeAuthorizationStubExtensions
{
    internal static void UsePermissionSetChecks(this ICurrentSessionAuthorization authorization)
    {
        authorization.AuthorizeAllAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                AuthorizedSessionActor? actor = null;
                foreach (var code in call.ArgAt<IReadOnlyCollection<string>>(0))
                {
                    var current = await authorization.AuthorizeAsync(code, call.ArgAt<CancellationToken>(1));
                    if (current is null || actor is not null && (current.UserId != actor.UserId || current.TenantId != actor.TenantId)) return null;
                    actor = current;
                }
                return actor;
            });
    }
}
