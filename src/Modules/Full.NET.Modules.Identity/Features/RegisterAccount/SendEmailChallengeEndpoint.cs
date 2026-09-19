using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Features.ManageRegistrationPolicy;
using Full.NET.Modules.Identity.Features.RegistrationInvitations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.RegisterAccount;

internal static class SendEmailChallengeEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/register/email-challenge", async (
            SendRegistrationEmailChallengeRequest request,
            RegistrationPolicyService policyService,
            RegistrationInvitationService invitationService,
            AccountChallengeService challengeService,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var policy = await policyService.GetAsync(cancellationToken).ConfigureAwait(false);
            if (!policy.IsSuccess)
            {
                return mapper.Map(
                    Result<AccountChallengeAcceptedResponse>.Failure(policy.Error!),
                    httpContext);
            }

            var mode = policy.Value!.RegistrationMode;
            if (mode == IdentityRegistrationMode.Disabled)
            {
                return mapper.Map(
                    Result<AccountChallengeAcceptedResponse>.Failure(new Error(
                        IdentityErrorCodes.RegistrationDisabled,
                        "Registration is disabled.",
                        ErrorType.Forbidden)),
                    httpContext);
            }

            if (mode == IdentityRegistrationMode.InvitationOnly
                && request.Purpose == IdentityAccountChallengePurpose.RegistrationEmailVerification)
            {
                return mapper.Map(
                    Result<AccountChallengeAcceptedResponse>.Failure(new Error(
                        IdentityErrorCodes.RegistrationDisabled,
                        "Registration is disabled.",
                        ErrorType.Forbidden)),
                    httpContext);
            }

            if (request.Purpose == IdentityAccountChallengePurpose.InvitationEmailVerification)
            {
                if (!request.InvitationId.HasValue || string.IsNullOrWhiteSpace(request.Email))
                {
                    return mapper.Map(
                        Result<AccountChallengeAcceptedResponse>.Failure(new Error(
                            IdentityErrorCodes.RegistrationInvitationInvalid,
                            "A valid invitation is required.",
                            ErrorType.Validation)),
                        httpContext);
                }

                var invitation = await invitationService.VerifyAsync(
                        request.InvitationId.Value,
                        request.InvitationToken ?? string.Empty,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!invitation.IsSuccess)
                {
                    return mapper.Map(
                        Result<AccountChallengeAcceptedResponse>.Failure(invitation.Error!),
                        httpContext);
                }

                var normalizedEmail = AccountChallengeService.NormalizeEmail(request.Email);
                if (normalizedEmail is null
                    || !string.Equals(
                        normalizedEmail,
                        invitation.Value!.Email,
                        StringComparison.Ordinal))
                {
                    return mapper.Map(
                        Result<AccountChallengeAcceptedResponse>.Failure(new Error(
                            IdentityErrorCodes.RegistrationInvitationInvalid,
                            "A valid invitation is required.",
                            ErrorType.Validation)),
                        httpContext);
                }
            }

            var result = await challengeService.CreateAndDeliverAsync(
                    request.Purpose,
                    request.Email,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identitySendRegistrationEmailChallenge")
        .Produces<AccountChallengeAcceptedResponse>(StatusCodes.Status200OK)
        .AllowAnonymous()
        .RequireRateLimiting(IdentityModule.SessionMutationRateLimitPolicy);
    }
}
