using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.RecoverAccount;

internal sealed class RequestHandler(
    AccountChallengeService challengeService,
    IQueryExecutor queryExecutor) : ICommandHandler<RequestCommand, AccountChallengeAcceptedResponse>
{
    public async Task<Result<AccountChallengeAcceptedResponse>> HandleAsync(
        RequestCommand command,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = AccountChallengeService.NormalizeEmail(command.Request.Email);
        if (normalizedEmail is null)
        {
            return AcceptedPlaceholder();
        }

        var user = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                AccountLifecycleSql.FindUserByProfileEmail,
                IdentitySqlParameters.Create(("Email", normalizedEmail)),
                cancellationToken)
            .ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return AcceptedPlaceholder();
        }

        var created = await challengeService.CreateAndDeliverAsync(
                IdentityAccountChallengePurpose.PasswordRecovery,
                normalizedEmail,
                cancellationToken)
            .ConfigureAwait(false);
        return created.IsSuccess
            ? created
            : AcceptedPlaceholder();
    }

    private static Result<AccountChallengeAcceptedResponse> AcceptedPlaceholder() =>
        Result<AccountChallengeAcceptedResponse>.Success(
            new AccountChallengeAcceptedResponse(Guid.Empty, DateTimeOffset.UtcNow));
}
