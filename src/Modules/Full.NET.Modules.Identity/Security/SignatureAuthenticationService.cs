using System.Security.Claims;
using System.Text.RegularExpressions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Security;

/// <summary>校验签名请求、写入 Nonce 并构造可授权主体。</summary>
internal sealed partial class SignatureAuthenticationService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<SignatureAuthenticationOptions> options,
    ILogger<SignatureAuthenticationService> logger)
{
    private static readonly TimeSpan LastUsedObservationWindow = TimeSpan.FromMinutes(5);

    [GeneratedRegex("^[A-Za-z0-9]+$")]
    private static partial Regex NoncePattern();

    public async Task<SignatureAuthenticationResult> AuthenticateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var headers = ParseHeaders(httpContext.Request.Headers, settings);
        if (headers.Error is not null)
        {
            return SignatureAuthenticationResult.Failure(headers.Error);
        }

        if (!string.Equals(
                headers.SignatureVersion,
                SignatureAuthenticationOptions.SupportedVersion,
                StringComparison.Ordinal))
        {
            return Failure(
                IdentitySignatureErrorCodes.InvalidVersion,
                ErrorType.Unauthorized);
        }

        if (!SignatureCanonicalRequest.TryParseUnixTimestamp(
                headers.Timestamp,
                out var requestTime))
        {
            return Failure(
                IdentitySignatureErrorCodes.InvalidTimestamp,
                ErrorType.Unauthorized);
        }

        var now = clock.UtcNow;
        var skew = TimeSpan.FromSeconds(
            Math.Clamp(
                settings.ClockSkewSeconds,
                settings.MinClockSkewSeconds,
                settings.MaxClockSkewSeconds));
        if (requestTime < now - skew)
        {
            return Failure(
                IdentitySignatureErrorCodes.TimestampExpired,
                ErrorType.Unauthorized);
        }

        if (requestTime > now + skew)
        {
            return Failure(
                IdentitySignatureErrorCodes.TimestampInFuture,
                ErrorType.Unauthorized);
        }

        if (headers.Nonce.Length < settings.MinNonceLength
            || headers.Nonce.Length > settings.MaxNonceLength
            || !NoncePattern().IsMatch(headers.Nonce))
        {
            return Failure(
                IdentitySignatureErrorCodes.InvalidNonce,
                ErrorType.Unauthorized);
        }

        var rows = await queryExecutor.QueryAsync<ApiKeyAuthenticationRow>(
                ApiKeySql.FindForSignatureAuthentication,
                new { AccessKeyId = headers.AccessKeyId },
                cancellationToken)
            .ConfigureAwait(false);
        var candidates = rows
            .Where(row => row.KeyPrefix == headers.AccessKeyId)
            .ToArray();
        if (candidates.Length == 0)
        {
            await WriteAuditAsync(
                    null,
                    headers.AccessKeyId,
                    "signature_authentication",
                    IdentitySignatureErrorCodes.AccessKeyNotFound,
                    false,
                    httpContext,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
            return Failure(
                IdentitySignatureErrorCodes.AccessKeyNotFound,
                ErrorType.Unauthorized);
        }

        if (candidates.Length > 1)
        {
            logger.LogWarning(
                "Access key id {AccessKeyId} is ambiguous across multiple API keys.",
                headers.AccessKeyId);
            return Failure(
                IdentitySignatureErrorCodes.AccessKeyNotFound,
                ErrorType.Unauthorized);
        }

        var row = candidates[0];
        var scopeError = ValidateScopeBinding(row, headers.TenantId);
        if (scopeError is not null)
        {
            await WriteAuditAsync(
                    row.UserId,
                    headers.AccessKeyId,
                    "signature_authentication",
                    scopeError.Code,
                    false,
                    httpContext,
                    row.TenantId,
                    cancellationToken)
                .ConfigureAwait(false);
            return SignatureAuthenticationResult.Failure(scopeError);
        }

        if (!IsActive(row, now))
        {
            var code = !row.IsActive
                ? IdentitySignatureErrorCodes.AccessKeyDisabled
                : IdentitySignatureErrorCodes.AccessKeyExpired;
            await WriteAuditAsync(
                    row.UserId,
                    headers.AccessKeyId,
                    "signature_authentication",
                    code,
                    false,
                    httpContext,
                    row.TenantId,
                    cancellationToken)
                .ConfigureAwait(false);
            return Failure(code, ErrorType.Unauthorized);
        }

        var permissions = ApiKeyAuthenticationService.DeserializePermissions(row.PermissionsJson);
        if (permissions.Count == 0)
        {
            return Failure(
                IdentitySignatureErrorCodes.AccessKeyDisabled,
                ErrorType.Unauthorized);
        }

        string canonicalString;
        try
        {
            var method = SignatureCanonicalRequest.NormalizeMethod(httpContext.Request.Method);
            var canonicalPath = SignatureCanonicalRequest.NormalizePath(
                httpContext.Request.PathBase,
                httpContext.Request.Path);
            var canonicalQuery = SignatureCanonicalRequest.BuildCanonicalQuery(
                httpContext.Request.QueryString);
            var body = await SignatureCanonicalRequest.ReadBodyAsync(
                    httpContext.Request,
                    settings.MaxBodyBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            var contentHash = SignatureCanonicalRequest.ComputeContentHash(body);
            canonicalString = SignatureCanonicalRequest.BuildCanonicalString(
                method,
                canonicalPath,
                canonicalQuery,
                contentHash,
                headers.AccessKeyId,
                headers.Timestamp,
                headers.Nonce);
        }
        catch (SignatureCanonicalizationException exception)
        {
            return Failure(exception.ErrorCode, ErrorType.Unauthorized);
        }

        var signingKey = SignatureCanonicalRequest.TryParseSigningKeyBytes(
            row.KeyHash,
            out var signingKeyBytes)
            ? signingKeyBytes
            : null;
        if (signingKey is null)
        {
            await WriteAuditAsync(
                    row.UserId,
                    headers.AccessKeyId,
                    "signature_authentication",
                    IdentitySignatureErrorCodes.AccessKeyDisabled,
                    false,
                    httpContext,
                    row.TenantId,
                    cancellationToken)
                .ConfigureAwait(false);
            return Failure(
                IdentitySignatureErrorCodes.AccessKeyDisabled,
                ErrorType.Unauthorized);
        }

        var expectedSignature = SignatureCanonicalRequest.ComputeSignature(
            canonicalString,
            signingKey);
        if (!SignatureCanonicalRequest.FixedTimeEqualsSignatures(
                expectedSignature,
                headers.Signature))
        {
            await WriteAuditAsync(
                    row.UserId,
                    headers.AccessKeyId,
                    "signature_authentication",
                    IdentitySignatureErrorCodes.InvalidSignature,
                    false,
                    httpContext,
                    row.TenantId,
                    cancellationToken)
                .ConfigureAwait(false);
            return Failure(
                IdentitySignatureErrorCodes.InvalidSignature,
                ErrorType.Unauthorized);
        }

        var nonceDigest = SignatureCanonicalRequest.ComputeNonceDigest(headers.Nonce);
        var expiresAtUtc = requestTime
            + skew
            + TimeSpan.FromSeconds(settings.NonceRetentionSeconds);
        try
        {
            await commandExecutor.ExecuteAsync(
                    SignatureNonceSql.TryInsert,
                    new
                    {
                        Id = idGenerator.NewId(),
                        AccessKeyId = headers.AccessKeyId,
                        NonceDigest = nonceDigest,
                        CreatedAtUtc = now,
                        ExpiresAtUtc = expiresAtUtc,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DataCommandException exception)
            when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            await WriteAuditAsync(
                    row.UserId,
                    headers.AccessKeyId,
                    "signature_authentication",
                    IdentitySignatureErrorCodes.ReplayDetected,
                    false,
                    httpContext,
                    row.TenantId,
                    cancellationToken)
                .ConfigureAwait(false);
            return Failure(
                IdentitySignatureErrorCodes.ReplayDetected,
                ErrorType.Unauthorized);
        }

        await TouchLastUsedIfNeededAsync(row, now, cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync(
                row.UserId,
                headers.AccessKeyId,
                "signature_authentication",
                "identity.signature.succeeded",
                true,
                httpContext,
                row.TenantId,
                cancellationToken)
            .ConfigureAwait(false);

        return SignatureAuthenticationResult.Success(
            BuildPrincipal(row, permissions, headers.TenantId));
    }

    private static SignatureAuthenticationResult Failure(string code, ErrorType type) =>
        SignatureAuthenticationResult.Failure(new Error(
            code,
            "Signature authentication failed.",
            type));

    private static Error? ValidateScopeBinding(
        ApiKeyAuthenticationRow row,
        Guid? requestedTenantId)
    {
        var isHostScope = string.Equals(
            row.ScopeKey,
            "host",
            StringComparison.Ordinal)
            && row.TenantId is null;
        if (isHostScope)
        {
            return requestedTenantId is not null
                ? new Error(
                    IdentitySignatureErrorCodes.TenantScopeMismatch,
                    "Host access keys cannot bind to a tenant context.",
                    ErrorType.Forbidden)
                : null;
        }

        if (!row.ScopeKey.StartsWith("tenant:", StringComparison.Ordinal)
            || row.TenantId is null)
        {
            return new Error(
                IdentitySignatureErrorCodes.TenantScopeMismatch,
                "The access key scope is invalid.",
                ErrorType.Forbidden);
        }

        if (requestedTenantId is null || requestedTenantId != row.TenantId)
        {
            return new Error(
                IdentitySignatureErrorCodes.TenantScopeMismatch,
                "The access key does not match the requested tenant context.",
                ErrorType.Forbidden);
        }

        return null;
    }

    private static bool IsActive(ApiKeyAuthenticationRow row, DateTimeOffset now) =>
        row.IsActive
        && row.UserIsActive
        && !(row.UserLockoutEndUtc > now)
        && (row.ExpiresAtUtc is null || row.ExpiresAtUtc > now);

    private async Task TouchLastUsedIfNeededAsync(
        ApiKeyAuthenticationRow row,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var lastUsedBeforeUtc = now - LastUsedObservationWindow;
        if (row.LastUsedAtUtc is not null && row.LastUsedAtUtc > lastUsedBeforeUtc)
        {
            return;
        }

        await commandExecutor.ExecuteAsync(
                ApiKeySql.TouchLastUsed,
                new
                {
                    ApiKeyId = row.ApiKeyId,
                    LastUsedAtUtc = now,
                    LastUsedBeforeUtc = lastUsedBeforeUtc,
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task WriteAuditAsync(
        Guid? userId,
        string accessKeyId,
        string eventType,
        string resultCode,
        bool succeeded,
        HttpContext httpContext,
        Guid? contextTenantId,
        CancellationToken cancellationToken)
    {
        var audit = new AuthAuditEvent(
            idGenerator.NewId(),
            userId,
            null,
            TokenHash.Compute(accessKeyId),
            eventType,
            resultCode,
            succeeded,
            Truncate(httpContext.Connection.RemoteIpAddress?.ToString(), 64),
            Truncate(httpContext.Request.Headers.UserAgent.ToString(), 512),
            contextTenantId,
            clock.UtcNow);
        await commandExecutor.ExecuteAsync(
                IdentitySql.InsertAuthAudit,
                audit,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static ClaimsPrincipal BuildPrincipal(
        ApiKeyAuthenticationRow row,
        IReadOnlyList<string> permissions,
        Guid? activeTenantId)
    {
        var isHostScope = string.Equals(row.ScopeKey, "host", StringComparison.Ordinal);
        var actorScope = isHostScope ? "host" : "tenant";
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, row.UserId.ToString("D")),
            new(JwtRegisteredClaimNames.Name, row.DisplayName),
            new("preferred_username", row.Username),
            new(IdentityClaimTypes.ActorScope, actorScope),
            new(IdentityClaimTypes.Scope, isHostScope ? "host" : row.ScopeKey),
            new(IdentityClaimTypes.SecurityStamp, row.SecurityStamp),
            new(IdentityClaimTypes.ApiKeyId, row.ApiKeyId.ToString("D")),
        };
        if (activeTenantId is not null)
        {
            claims.Add(new Claim(IdentityClaimTypes.TenantId, activeTenantId.Value.ToString("D")));
        }

        foreach (var permission in permissions)
        {
            claims.Add(new Claim(IdentityClaimTypes.Permission, permission));
        }

        var identity = new ClaimsIdentity(
            claims,
            SignatureAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    private static (string AccessKeyId, string Timestamp, string Nonce, string Signature, string SignatureVersion, Guid? TenantId, Error? Error)
        ParseHeaders(
            IHeaderDictionary headers,
            SignatureAuthenticationOptions settings)
    {
        var hasAny = HasHeader(headers, SignatureAuthenticationOptions.AccessKeyIdHeader)
            || HasHeader(headers, SignatureAuthenticationOptions.TimestampHeader)
            || HasHeader(headers, SignatureAuthenticationOptions.NonceHeader)
            || HasHeader(headers, SignatureAuthenticationOptions.SignatureHeader)
            || HasHeader(headers, SignatureAuthenticationOptions.SignatureVersionHeader);
        if (!hasAny)
        {
            return default;
        }

        if (!TryGetRequiredHeader(headers, SignatureAuthenticationOptions.AccessKeyIdHeader, out var accessKeyId, out var headerError)
            || !TryGetRequiredHeader(headers, SignatureAuthenticationOptions.TimestampHeader, out var timestamp, out headerError)
            || !TryGetRequiredHeader(headers, SignatureAuthenticationOptions.NonceHeader, out var nonce, out headerError)
            || !TryGetRequiredHeader(headers, SignatureAuthenticationOptions.SignatureHeader, out var signature, out headerError)
            || !TryGetRequiredHeader(
                headers,
                SignatureAuthenticationOptions.SignatureVersionHeader,
                out var signatureVersion,
                out headerError))
        {
            return (default!, default!, default!, default!, default!, null,
                headerError ?? new Error(
                    IdentitySignatureErrorCodes.MissingHeaders,
                    "Signature headers are incomplete.",
                    ErrorType.Unauthorized));
        }

        if (accessKeyId.Length > settings.MaxAccessKeyIdLength)
        {
            return (default!, default!, default!, default!, default!, null,
                new Error(
                    IdentitySignatureErrorCodes.AccessKeyNotFound,
                    "The access key identifier is invalid.",
                    ErrorType.Unauthorized));
        }

        Guid? tenantId = null;
        if (headers.TryGetValue(SignatureAuthenticationOptions.TenantIdHeader, out var tenantValues))
        {
            if (tenantValues.Count > 1)
            {
                return (default!, default!, default!, default!, default!, null,
                    new Error(
                        IdentitySignatureErrorCodes.DuplicateHeaders,
                        "Signature headers must not be duplicated.",
                        ErrorType.Unauthorized));
            }

            var tenantValue = tenantValues.ToString().Trim();
            if (!string.IsNullOrEmpty(tenantValue))
            {
                if (!Guid.TryParse(tenantValue, out var parsedTenantId))
                {
                    return (default!, default!, default!, default!, default!, null,
                        new Error(
                            IdentitySignatureErrorCodes.TenantScopeMismatch,
                            "The tenant header is invalid.",
                            ErrorType.Forbidden));
                }

                tenantId = parsedTenantId;
            }
        }

        return (accessKeyId, timestamp, nonce, signature, signatureVersion, tenantId, null);
    }

    private static bool HasHeader(IHeaderDictionary headers, string name) =>
        headers.TryGetValue(name, out var values) && !string.IsNullOrWhiteSpace(values.ToString());

    private static bool TryGetRequiredHeader(
        IHeaderDictionary headers,
        string name,
        out string value,
        out Error? error)
    {
        error = null;
        value = string.Empty;
        if (!headers.TryGetValue(name, out var values))
        {
            return false;
        }

        if (values.Count > 1)
        {
            error = new Error(
                IdentitySignatureErrorCodes.DuplicateHeaders,
                "Signature headers must not be duplicated.",
                ErrorType.Unauthorized);
            return false;
        }

        value = values.ToString().Trim();
        return !string.IsNullOrEmpty(value);
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength
            ? value
            : value[..maxLength];
}

internal sealed class SignatureAuthenticationResult
{
    private SignatureAuthenticationResult(ClaimsPrincipal? principal, Error? error)
    {
        Principal = principal;
        Error = error;
    }

    public ClaimsPrincipal? Principal { get; }

    public Error? Error { get; }

    public bool Succeeded => Principal is not null;

    public static SignatureAuthenticationResult Success(ClaimsPrincipal principal) =>
        new(principal, null);

    public static SignatureAuthenticationResult Failure(Error error) =>
        new(null, error);
}
