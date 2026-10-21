using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;

/// <summary>模块内端口适配或空实现；供测试或未启用能力时闭合 DI。</summary>
namespace Full.NET.Modules.Identity.Features.AccountChallenges;

internal sealed class NullIdentityChallengeDeliveryPort : IIdentityChallengeDeliveryPort
{
    public Task<Result<bool>> SendAsync(
        IdentityChallengeDeliveryIntent intent,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<bool>.Failure(new Error(
            IdentityErrorCodes.AccountChallengeDeliveryFailed,
            "Notifications module is not available.",
            ErrorType.BusinessRule)));
}
