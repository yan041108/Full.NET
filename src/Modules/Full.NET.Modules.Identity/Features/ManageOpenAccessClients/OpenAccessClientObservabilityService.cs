using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageOpenAccessClients;

/// <summary>接入方应用访问日志、用量与有界签名调试查询服务。</summary>
internal sealed class OpenAccessClientObservabilityService(
    IQueryExecutor queryExecutor,
    OpenAccessClientAccessSupport accessSupport,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>签名调试允许的最大请求体字节数。</summary>
    internal const int MaxDebugBodyBytes = 65_536;

    /// <summary>签名调试允许的最大路径长度。</summary>
    internal const int MaxDebugPathLength = 2_048;

    /// <summary>签名调试允许的最大 Secret 长度。</summary>
    internal const int MaxDebugSecretLength = 256;

    /// <summary>分页查询指定接入方应用的认证访问日志。</summary>
    public async Task<Result<PagedResult<OpenAccessClientAccessLogEntry>>> ListAccessLogsAsync(
        Guid clientId,
        int page,
        int pageSize,
        bool? succeeded,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default)
    {
        var accessKey = await ResolveAccessKeyAsync(clientId, cancellationToken)
            .ConfigureAwait(false);
        if (accessKey is null)
        {
            return NotFoundAccessLogs();
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var parameters = IdentitySqlParameters.Create(
            ("AccessKeyFingerprint", OpenAccessClientAccessSupport.ComputeAccessKeyFingerprint(
                accessKey.AccessKeyId)),
            ("Succeeded", succeeded),
            ("FromUtc", fromUtc),
            ("ToUtc", toUtc),
            ("Offset", offset),
            ("PageSize", pageSize));
        var (countStatement, listStatement) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (
                OpenAccessClientObservabilitySql.CountAccessLogsSqlServer,
                OpenAccessClientObservabilitySql.ListAccessLogsSqlServer),
            DatabaseProvider.MySql => (
                OpenAccessClientObservabilitySql.CountAccessLogsMySql,
                OpenAccessClientObservabilitySql.ListAccessLogsMySql),
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<OpenAccessClientAccessLogRow>(
                listStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows
            .Select(row => new OpenAccessClientAccessLogEntry(
                row.Id,
                row.EventType,
                row.ResultCode,
                row.Succeeded,
                row.IpAddress,
                row.UserAgent,
                row.OccurredAtUtc))
            .ToArray();
        return Result<PagedResult<OpenAccessClientAccessLogEntry>>.Success(
            new PagedResult<OpenAccessClientAccessLogEntry>(items, page, pageSize, total));
    }

    /// <summary>读取指定接入方应用当日用量与配额状态。</summary>
    public async Task<Result<OpenAccessClientUsageResponse>> GetUsageAsync(
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<OpenAccessClientDetailRow>(
                OpenAccessClientSql.FindById,
                IdentitySqlParameters.Create(("ClientId", clientId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return NotFoundUsage();
        }

        var (windowStartUtc, windowEndUtc) = OpenAccessClientAccessSupport.ResolveUtcDayWindow(
            clock.UtcNow);
        var usage = await accessSupport.CountUsageAsync(
                row.AccessKeyId,
                windowStartUtc,
                windowEndUtc,
                cancellationToken)
            .ConfigureAwait(false);
        var quotaExceeded = row.DailyRequestQuota is int dailyQuota
            && dailyQuota > 0
            && usage.SuccessCount >= dailyQuota;
        return Result<OpenAccessClientUsageResponse>.Success(
            new OpenAccessClientUsageResponse(
                row.Id,
                row.DailyRequestQuota,
                (int)Math.Min(usage.SuccessCount, int.MaxValue),
                (int)Math.Min(usage.FailureCount, int.MaxValue),
                windowStartUtc,
                windowEndUtc,
                quotaExceeded));
    }

    /// <summary>
    /// 在受控边界内验算 HMAC 签名；操作者提供客户端 Secret，服务端不回显存储密钥。
    /// </summary>
    public async Task<Result<OpenAccessClientSignatureDebugResponse>> DebugSignatureAsync(
        Guid clientId,
        OpenAccessClientSignatureDebugRequest request,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<OpenAccessClientDetailRow>(
                OpenAccessClientSql.FindById,
                IdentitySqlParameters.Create(("ClientId", clientId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return NotFoundDebug();
        }

        var validationError = ValidateDebugRequest(request);
        if (validationError is not null)
        {
            return Result<OpenAccessClientSignatureDebugResponse>.Failure(validationError);
        }

        var diagnostics = new List<string>();
        if (!string.Equals(
                request.SignatureVersion,
                SignatureAuthenticationOptions.SupportedVersion,
                StringComparison.Ordinal))
        {
            diagnostics.Add("Signature version does not match the supported protocol version.");
        }

        byte[] body;
        try
        {
            body = DecodeBody(request.BodyBase64);
        }
        catch (FormatException)
        {
            return DebugInvalid("BodyBase64 is not valid Base64.");
        }

        if (body.Length > MaxDebugBodyBytes)
        {
            return DebugInvalid("Body exceeds the debug size limit.");
        }

        string canonicalString;
        string contentHash;
        try
        {
            var method = SignatureCanonicalRequest.NormalizeMethod(request.Method);
            diagnostics.Add($"Normalized method: {method}");
            var path = request.Path.Trim();
            if (path.Length == 0 || path.Length > MaxDebugPathLength)
            {
                return DebugInvalid("Path is empty or exceeds the debug length limit.");
            }

            if (!path.StartsWith("/", StringComparison.Ordinal))
            {
                path = "/" + path;
            }

            var query = string.IsNullOrWhiteSpace(request.Query)
                ? string.Empty
                : request.Query!.Trim();
            if (query.StartsWith('?'))
            {
                query = query[1..];
            }

            var canonicalQuery = string.IsNullOrEmpty(query)
                ? string.Empty
                : SignatureCanonicalRequest.BuildCanonicalQuery(
                    new QueryString("?" + query));
            diagnostics.Add($"Canonical query: {(string.IsNullOrEmpty(canonicalQuery) ? "(empty)" : canonicalQuery)}");
            contentHash = SignatureCanonicalRequest.ComputeContentHash(body);
            diagnostics.Add($"Content hash: {contentHash}");
            canonicalString = SignatureCanonicalRequest.BuildCanonicalString(
                method,
                path,
                canonicalQuery,
                contentHash,
                row.AccessKeyId,
                request.Timestamp.Trim(),
                request.Nonce.Trim());
            diagnostics.Add("Canonical string assembled.");
        }
        catch (SignatureCanonicalizationException exception)
        {
            diagnostics.Add($"Canonicalization failed: {exception.Message}");
            return Result<OpenAccessClientSignatureDebugResponse>.Success(
                new OpenAccessClientSignatureDebugResponse(
                    false,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    request.Signature.Trim().ToLowerInvariant(),
                    diagnostics));
        }

        var keyHash = TokenHash.Compute(request.Secret.Trim());
        var signingKey = SignatureCanonicalRequest.TryParseSigningKeyBytes(
            keyHash,
            out var signingKeyBytes)
            ? signingKeyBytes
            : null;
        if (signingKey is null)
        {
            diagnostics.Add("Secret cannot be parsed as a signing key.");
            return Result<OpenAccessClientSignatureDebugResponse>.Success(
                new OpenAccessClientSignatureDebugResponse(
                    false,
                    canonicalString,
                    contentHash,
                    string.Empty,
                    request.Signature.Trim().ToLowerInvariant(),
                    diagnostics));
        }

        var expectedSignature = SignatureCanonicalRequest.ComputeSignature(
            canonicalString,
            signingKey);
        var providedSignature = request.Signature.Trim().ToLowerInvariant();
        var signaturesMatch = SignatureCanonicalRequest.FixedTimeEqualsSignatures(
            providedSignature,
            expectedSignature);
        diagnostics.Add(signaturesMatch
            ? "Provided signature matches the expected signature."
            : "Provided signature does not match the expected signature.");
        return Result<OpenAccessClientSignatureDebugResponse>.Success(
            new OpenAccessClientSignatureDebugResponse(
                signaturesMatch,
                canonicalString,
                contentHash,
                expectedSignature,
                providedSignature,
                diagnostics));
    }

    internal static Result<int?> ValidateDailyRequestQuota(int? dailyRequestQuota)
    {
        if (dailyRequestQuota is null)
        {
            return Result<int?>.Success(null);
        }

        if (dailyRequestQuota < 1 || dailyRequestQuota > 10_000_000)
        {
            return Result<int?>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Daily request quota must be between 1 and 10000000, or null for unlimited.",
                ErrorType.Validation));
        }

        return Result<int?>.Success(dailyRequestQuota);
    }

    private async Task<OpenAccessClientAccessKeyRow?> ResolveAccessKeyAsync(
        Guid clientId,
        CancellationToken cancellationToken) =>
        await queryExecutor.QuerySingleOrDefaultAsync<OpenAccessClientAccessKeyRow>(
                OpenAccessClientObservabilitySql.FindClientAccessKeyById,
                IdentitySqlParameters.Create(("ClientId", clientId)),
                cancellationToken)
            .ConfigureAwait(false);

    private static byte[] DecodeBody(string? bodyBase64)
    {
        if (string.IsNullOrWhiteSpace(bodyBase64))
        {
            return [];
        }

        return Convert.FromBase64String(bodyBase64.Trim());
    }

    private static Error? ValidateDebugRequest(OpenAccessClientSignatureDebugRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Secret)
            || request.Secret.Length > MaxDebugSecretLength)
        {
            return DebugInvalidError("Secret is required and must be within the debug length limit.");
        }

        if (string.IsNullOrWhiteSpace(request.Method)
            || string.IsNullOrWhiteSpace(request.Timestamp)
            || string.IsNullOrWhiteSpace(request.Nonce)
            || string.IsNullOrWhiteSpace(request.Signature)
            || string.IsNullOrWhiteSpace(request.SignatureVersion))
        {
            return DebugInvalidError("Method, timestamp, nonce, signature and version are required.");
        }

        return null;
    }

    private static Result<OpenAccessClientSignatureDebugResponse> DebugInvalid(string message) =>
        Result<OpenAccessClientSignatureDebugResponse>.Failure(DebugInvalidError(message));

    private static Error DebugInvalidError(string message) =>
        new(
            IdentityErrorCodes.OpenAccessClientSignatureDebugInvalid,
            message,
            ErrorType.Validation);

    private static Result<PagedResult<OpenAccessClientAccessLogEntry>> NotFoundAccessLogs() =>
        Result<PagedResult<OpenAccessClientAccessLogEntry>>.Failure(new Error(
            IdentityErrorCodes.OpenAccessClientNotFound,
            "The OpenAccess client was not found.",
            ErrorType.NotFound));

    private static Result<OpenAccessClientUsageResponse> NotFoundUsage() =>
        Result<OpenAccessClientUsageResponse>.Failure(new Error(
            IdentityErrorCodes.OpenAccessClientNotFound,
            "The OpenAccess client was not found.",
            ErrorType.NotFound));

    private static Result<OpenAccessClientSignatureDebugResponse> NotFoundDebug() =>
        Result<OpenAccessClientSignatureDebugResponse>.Failure(new Error(
            IdentityErrorCodes.OpenAccessClientNotFound,
            "The OpenAccess client was not found.",
            ErrorType.NotFound));
}
