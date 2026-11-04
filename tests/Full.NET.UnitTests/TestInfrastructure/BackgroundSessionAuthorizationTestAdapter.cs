using Full.NET.Modules.Identity.Contracts;
using NSubstitute;
namespace Full.NET.UnitTests.TestInfrastructure;
/// <summary>授权测试以单项停点模拟权威目录，批量Port逐项委托同一替身，保持撤权和迟到响应注入有效。</summary>
internal static class BackgroundSessionAuthorizationTestAdapter
{
    internal static void DelegateAllToSingle(IBackgroundSessionAuthorization authorization)
    {
        authorization.AuthorizeAllAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var binding = call.ArgAt<SessionBindingSnapshot>(0);
                AuthorizedSessionActor? actor = null;
                foreach (var permission in call.ArgAt<IReadOnlyCollection<string>>(1))
                {
                    actor = await authorization.AuthorizeAsync(binding, permission, call.ArgAt<CancellationToken>(2));
                    if (actor is null) return null;
                }
                return actor;
            });
    }
}
