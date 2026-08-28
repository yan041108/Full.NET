using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
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
    ILocaleNormalizer localeNormalizer,
    IClock clock)
    : ICommandHandler<Command, LocalePreferenceResponse>
{
    public async Task<Result<LocalePreferenceResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        if (!TryReadIdentity(
                command.Principal,
                out var userId,
                out var sessionId,
                out var scopeKey))
        {
            return Unauthorized();
        }

        var session = await FindSessionAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (!IsOwnedActiveSession(session, userId, scopeKey, command.Principal))
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
                IdentitySqlParameters.Create(
                    ("UserId", userId),
                    ("SessionId", sessionId),
                    ("ScopeKey", scopeKey),
                    ("SecurityStamp", session!.SecurityStamp),
                    ("NowUtc", clock.UtcNow),
                    ("PreferredLocale", preferredLocale),
                    ("ProfileVersion", command.ProfileVersion)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            var currentSession = await FindSessionAsync(sessionId, cancellationToken)
                .ConfigureAwait(false);
            if (!IsOwnedActiveSession(
                    currentSession,
                    userId,
                    scopeKey,
                    command.Principal))
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
        out Guid sessionId,
        out string scopeKey)
    {
        sessionId = Guid.Empty;
        scopeKey = principal.FindFirstValue(IdentityClaimTypes.ActorScope) ?? string.Empty;
        return Guid.TryParse(
                principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
                out userId)
            && Guid.TryParse(
                principal.FindFirstValue(IdentityClaimTypes.SessionId),
                out sessionId)
            && !string.IsNullOrWhiteSpace(scopeKey);
    }

    private Task<RefreshSessionRecord?> FindSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
            IdentitySql.FindRefreshSessionById,
            IdentitySqlParameters.Create(("SessionId", sessionId)),
            cancellationToken);

    private bool IsOwnedActiveSession(
        RefreshSessionRecord? session,
        Guid userId,
        string scopeKey,
        ClaimsPrincipal principal) =>
        session is not null
        && session.UserId == userId
        && string.Equals(session.ScopeKey, scopeKey, StringComparison.Ordinal)
        && string.Equals(
            session.SecurityStamp,
            principal.FindFirstValue(IdentityClaimTypes.SecurityStamp),
            StringComparison.Ordinal)
        && session.IsActive
        && session.ExpiresAtUtc > clock.UtcNow
        && !session.ConsumedAtUtc.HasValue
        && !session.RevokedAtUtc.HasValue;

    private static Result<LocalePreferenceResponse> Unauthorized() =>
        Result<LocalePreferenceResponse>.Failure(new Error(
            Code: IdentityErrorCodes.SessionNotActive,
            Message: "The current session is no longer active.",
            Type: ErrorType.Unauthorized));
}
