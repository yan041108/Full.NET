using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;

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
