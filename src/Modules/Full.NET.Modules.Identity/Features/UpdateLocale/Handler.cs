using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Localization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Features.UpdateLocale;

internal sealed class Handler(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ILocaleNormalizer localeNormalizer)
    : ICommandHandler<Command, LocalePreferenceResponse>
{
    public async Task<Result<LocalePreferenceResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        if (!TryReadIdentity(command.Principal, out var userId, out var scopeKey))
        {
            return Unauthorized();
        }

        var profile = await queryExecutor.QuerySingleOrDefaultAsync<IdentityProfileRecord>(
                IdentitySql.FindProfileByIdentity,
                new { UserId = userId, ScopeKey = scopeKey },
                cancellationToken)
            .ConfigureAwait(false);
        if (profile is not { IsActive: true })
        {
            return Unauthorized();
        }

        if (!localeNormalizer.IsSupported(command.Locale))
        {
            return Result<LocalePreferenceResponse>.Failure(new Error(
                Code: LocalizationErrorCodes.UnsupportedLocale,
                Message: "The requested locale is not supported.",
                Type: ErrorType.Validation));
        }

        var preferredLocale = localeNormalizer.Normalize(command.Locale);
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.UpdateLocalePreference,
                new
                {
                    UserId = userId,
                    ScopeKey = scopeKey,
                    PreferredLocale = preferredLocale,
                    command.ProfileVersion,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            var currentProfile = await queryExecutor
                .QuerySingleOrDefaultAsync<IdentityProfileRecord>(
                    IdentitySql.FindProfileByIdentity,
                    new { UserId = userId, ScopeKey = scopeKey },
                    cancellationToken)
                .ConfigureAwait(false);
            if (currentProfile is not { IsActive: true })
            {
                return Unauthorized();
            }

            return Result<LocalePreferenceResponse>.Failure(new Error(
                Code: IdentityErrorCodes.ProfileVersionConflict,
                Message: "The account profile was updated concurrently.",
                Type: ErrorType.Conflict));
        }

        return Result<LocalePreferenceResponse>.Success(new LocalePreferenceResponse(
            preferredLocale,
            command.ProfileVersion + 1));
    }

    private static bool TryReadIdentity(
        ClaimsPrincipal principal,
        out Guid userId,
        out string scopeKey)
    {
        scopeKey = principal.FindFirstValue(IdentityClaimTypes.ActorScope) ?? string.Empty;
        return Guid.TryParse(
                principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
                out userId)
            && !string.IsNullOrWhiteSpace(scopeKey);
    }

    private static Result<LocalePreferenceResponse> Unauthorized() =>
        Result<LocalePreferenceResponse>.Failure(new Error(
            Code: IdentityErrorCodes.SessionNotActive,
            Message: "The current session is no longer active.",
            Type: ErrorType.Unauthorized));
}
