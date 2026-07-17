using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Identity.Authorization;

namespace Full.NET.Modules.Identity.Features.RefreshSession;

internal sealed class Handler(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator,
    IPermissionSnapshotReader permissionSnapshotReader,
    IAccessTokenIssuer accessTokenIssuer,
    IRandomTokenGenerator randomTokenGenerator,
    IOptions<IdentityOptions> options)
    : ICommandHandler<Command, RefreshSessionResult>
{
    private readonly IdentityOptions _options = options.Value;

    public async Task<Result<RefreshSessionResult>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var presentedHash = TokenHash.Compute(command.RefreshToken);
        var record = await FindAsync(presentedHash, cancellationToken).ConfigureAwait(false);
        if (record is null || record.ExpiresAtUtc <= clock.UtcNow || record.RevokedAtUtc.HasValue)
        {
            if (record is not null)
            {
                await WriteAuditAsync(
                    record,
                    "identity.refresh-rejected",
                    false,
                    command,
                    cancellationToken).ConfigureAwait(false);
            }

            return InvalidRefreshToken();
        }

        if (record.ConsumedAtUtc.HasValue)
        {
            return await RejectReuseAsync(record, command, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!record.IsActive)
        {
            await RevokeFamilyAsync(record.FamilyId, cancellationToken).ConfigureAwait(false);
            await WriteAuditAsync(
                record,
                "identity.user-disabled",
                false,
                command,
                cancellationToken).ConfigureAwait(false);
            return InvalidRefreshToken();
        }

        var permissions = await permissionSnapshotReader.ReadAsync(
                record.UserId,
                record.ScopeKey,
                record.TenantId,
                cancellationToken)
            .ConfigureAwait(false);

        var replacementId = idGenerator.NewId();
        var replacementToken = randomTokenGenerator.Generate(32);
        var csrfToken = randomTokenGenerator.Generate(32);
        var consumed = false;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var consumedRows = await commandExecutor.ExecuteAsync(
                    IdentitySql.ConsumeRefreshSession,
                    new ConsumeRefreshSessionUpdate(
                        record.SessionId,
                        clock.UtcNow,
                        replacementId,
                        record.SessionVersion),
                    cancellationToken)
                .ConfigureAwait(false);
            if (consumedRows == 1)
            {
                consumed = true;
                break;
            }

            var concurrent = await FindAsync(presentedHash, cancellationToken)
                .ConfigureAwait(false);
            if (concurrent?.ConsumedAtUtc.HasValue == true)
            {
                return await RejectReuseAsync(concurrent, command, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!IsSameActiveSession(record, concurrent))
            {
                return InvalidRefreshToken();
            }

            // 上下文切换只推进 Version；重读后重试一次可避免误清理仍活动的会话。
            record = concurrent!;
        }

        if (!consumed)
        {
            return InvalidRefreshToken();
        }

        var replacement = new Full.NET.Modules.Identity.Domain.RefreshSession(
            replacementId,
            record.UserId,
            record.FamilyId,
            record.ClientId,
            TokenHash.Compute(replacementToken),
            clock.UtcNow.AddDays(_options.RefreshTokenDays),
            null,
            null,
            null,
            record.ActiveTenantId,
            clock.UtcNow,
            1);
        await EnsureSingleRowAsync(
            IdentitySql.InsertRefreshSession,
            replacement,
            "replacement refresh session insert",
            cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync(
            record,
            "identity.refresh-succeeded",
            true,
            command,
            cancellationToken).ConfigureAwait(false);

        var accessToken = accessTokenIssuer.Issue(
            ToUser(record),
            replacementId,
            record.ActiveTenantId,
            permissions);
        return Result<RefreshSessionResult>.Success(new RefreshSessionResult(
            new TokenResponse(
                accessToken.AccessToken,
                "Bearer",
                accessToken.ExpiresAtUtc),
            replacementToken,
            csrfToken));
    }

    private async Task<Result<RefreshSessionResult>> RejectReuseAsync(
        RefreshSessionRecord record,
        Command command,
        CancellationToken cancellationToken)
    {
        await RevokeFamilyAsync(record.FamilyId, cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync(
            record,
            "identity.refresh_token_reuse_detected",
            false,
            command,
            cancellationToken).ConfigureAwait(false);
        return Result<RefreshSessionResult>.Failure(new Error(
            Code: IdentityErrorCodes.RefreshTokenReuseDetected,
            Message: "Refresh token reuse was detected and the session was revoked.",
            Type: ErrorType.Unauthorized));
    }

    private Task<RefreshSessionRecord?> FindAsync(
        string tokenHash,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
            IdentitySql.FindRefreshSessionByHash,
            new { TokenHash = tokenHash },
            cancellationToken);

    private bool IsSameActiveSession(
        RefreshSessionRecord previous,
        RefreshSessionRecord? current)
    {
        return current is not null
            && current.SessionId == previous.SessionId
            && current.UserId == previous.UserId
            && current.FamilyId == previous.FamilyId
            && current.IsActive
            && current.TenantId == previous.TenantId
            && string.Equals(
                current.ScopeKey,
                previous.ScopeKey,
                StringComparison.Ordinal)
            && string.Equals(
                current.SecurityStamp,
                previous.SecurityStamp,
                StringComparison.Ordinal)
            && string.Equals(
                current.TokenHash,
                previous.TokenHash,
                StringComparison.Ordinal)
            && current.ExpiresAtUtc > clock.UtcNow
            && !current.ConsumedAtUtc.HasValue
            && !current.RevokedAtUtc.HasValue;
    }

    private Task<int> RevokeFamilyAsync(
        Guid familyId,
        CancellationToken cancellationToken) =>
        commandExecutor.ExecuteAsync(
            IdentitySql.RevokeRefreshFamily,
            new { FamilyId = familyId, RevokedAtUtc = clock.UtcNow },
            cancellationToken);

    private async Task WriteAuditAsync(
        RefreshSessionRecord record,
        string resultCode,
        bool succeeded,
        Command command,
        CancellationToken cancellationToken)
    {
        var audit = new AuthAuditEvent(
            idGenerator.NewId(),
            record.UserId,
            record.SessionId,
            TokenHash.Compute(record.NormalizedUsername),
            "refresh",
            resultCode,
            succeeded,
            Truncate(command.Client.IpAddress, 64),
            Truncate(command.Client.UserAgent, 512),
            record.ActiveTenantId,
            clock.UtcNow);
        await EnsureSingleRowAsync(
            IdentitySql.InsertAuthAudit,
            audit,
            "refresh audit insert",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureSingleRowAsync(
        SqlStatement statement,
        object parameters,
        string operation,
        CancellationToken cancellationToken)
    {
        var rows = await commandExecutor.ExecuteAsync(
                statement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        if (rows != 1)
        {
            throw new InvalidOperationException(
                $"Identity {operation} affected {rows} rows instead of one.");
        }
    }

    private static IdentityUser ToUser(RefreshSessionRecord record) => new(
        record.UserId,
        record.TenantId,
        record.ScopeKey,
        record.Username,
        record.NormalizedUsername,
        record.DisplayName,
        record.PasswordHash,
        record.IsActive,
        record.FailedLoginCount,
        record.LockoutEndUtc,
        record.SecurityStamp,
        record.UserCreatedAtUtc,
        record.UserUpdatedAtUtc,
        record.UserVersion,
        record.PreferredLocale,
        record.ProfileVersion);

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];

    private static Result<RefreshSessionResult> InvalidRefreshToken() =>
        Result<RefreshSessionResult>.Failure(new Error(
            Code: IdentityErrorCodes.InvalidRefreshToken,
            Message: "The refresh token is invalid or expired.",
            Type: ErrorType.Unauthorized));
}
