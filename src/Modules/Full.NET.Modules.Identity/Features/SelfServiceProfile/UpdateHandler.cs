using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Features.SelfServiceProfile;

/// <summary>处理当前用户自助档案更新。</summary>
internal sealed class UpdateHandler(SelfServiceProfileService service)
    : ICommandHandler<UpdateCommand, SelfServiceProfileResponse>
{
    public Task<Result<SelfServiceProfileResponse>> HandleAsync(
        UpdateCommand command,
        CancellationToken cancellationToken)
    {
        if (!TryReadIdentity(
                command.Principal,
                out var userId,
                out var actorScope))
        {
            return Task.FromResult(Unauthorized());
        }

        return service.UpdateAsync(
            userId,
            actorScope,
            command.Request,
            cancellationToken);
    }

    private static bool TryReadIdentity(
        ClaimsPrincipal principal,
        out Guid userId,
        out string actorScope)
    {
        actorScope = principal.FindFirstValue(IdentityClaimTypes.ActorScope) ?? string.Empty;
        return Guid.TryParse(
                   principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
                   out userId)
               && !string.IsNullOrWhiteSpace(actorScope);
    }

    private static Result<SelfServiceProfileResponse> Unauthorized() =>
        Result<SelfServiceProfileResponse>.Failure(new Error(
            Code: IdentityErrorCodes.SessionNotActive,
            Message: "The current session is no longer active.",
            Type: ErrorType.Unauthorized));
}
