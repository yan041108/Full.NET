using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>请求签名认证纵向切片验收：规范化、重放、Key 状态、作用域与脱敏。</summary>
internal static class IdentitySignatureAuthenticationAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var adminUserId = await ResolveAdminUserIdAsync(client, adminToken, cancellationToken);
        var created = await CreateApiKeyAsync(
            client,
            adminToken,
            adminUserId,
            cancellationToken);

        await VerifyCanonicalQueryIsOrderIndependentAsync(
            client,
            created,
            cancellationToken);
        await VerifyInvalidPercentEncodingIsRejectedAsync(
            client,
            created,
            cancellationToken);
        await VerifyEmptyAndNonEmptyBodyAsync(
            client,
            created,
            cancellationToken);
        await VerifyBodyTamperingIsRejectedAsync(
            client,
            created,
            cancellationToken);
        await VerifyExpiredAndFutureTimestampsAsync(
            client,
            created,
            cancellationToken);
        await VerifyNonceReplayAndConcurrentReplayAsync(
            client,
            created,
            cancellationToken);
        await VerifyHostKeyCannotBindTenantHeaderAsync(
            client,
            created,
            cancellationToken);
        await VerifyTenantKeyCannotCrossTenantAsync(
            factory,
            client,
            cancellationToken);
        await VerifyFailureLoggingDoesNotLeakSecretsAsync(
            factory,
            client,
            created,
            cancellationToken);
        await VerifyOversizedBodyIsRejectedAsync(
            factory,
            client,
            created,
            cancellationToken);
        await VerifyDuplicateHeadersAreRejectedAsync(
            client,
            created,
            cancellationToken);
        await VerifyCorruptKeyHashIsRejectedAsync(
            factory,
            client,
            created,
            cancellationToken);
        await VerifyRotatedDisabledAndExpiredKeysAsync(
            factory,
            client,
            adminToken,
            adminUserId,
            created,
            cancellationToken);
        await OpenApiSignatureSecuritySchemeAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyCanonicalQueryIsOrderIndependentAsync(
        HttpClient client,
        CreateHostApiKeyResponse created,
        CancellationToken cancellationToken)
    {
        using var first = await SendSignedAsync(
            client,
            HttpMethod.Get,
            "/api/v1/identity/users?page=2&pageSize=1",
            created.Secret,
            created.Key.KeyPrefix,
            body: null,
            cancellationToken: cancellationToken);
        using var second = await SendSignedAsync(
            client,
            HttpMethod.Get,
            "/api/v1/identity/users?pageSize=1&page=2",
            created.Secret,
            created.Key.KeyPrefix,
            body: null,
            cancellationToken: cancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
    }

    private static async Task VerifyInvalidPercentEncodingIsRejectedAsync(
        HttpClient client,
        CreateHostApiKeyResponse created,
        CancellationToken cancellationToken)
    {
        const string requestPath = "/api/v1/identity/users?name=a%2bb";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString("N")[..24];
        var canonical = SignatureCanonicalRequest.BuildCanonicalString(
            "GET",
            "/api/v1/identity/users",
            "name=a%2bb",
            SignatureCanonicalRequest.ComputeContentHash([]),
            created.Key.KeyPrefix,
            timestamp,
            nonce);
        var signingKey = SignatureCanonicalRequest.ParseSigningKeyBytes(
            TokenHash.Compute(created.Secret));
        var signature = SignatureCanonicalRequest.ComputeSignature(canonical, signingKey);
        request.Headers.Add(SignatureAuthenticationOptions.AccessKeyIdHeader, created.Key.KeyPrefix);
        request.Headers.Add(SignatureAuthenticationOptions.TimestampHeader, timestamp);
        request.Headers.Add(SignatureAuthenticationOptions.NonceHeader, nonce);
        request.Headers.Add(SignatureAuthenticationOptions.SignatureHeader, signature);
        request.Headers.Add(
            SignatureAuthenticationOptions.SignatureVersionHeader,
            SignatureAuthenticationOptions.SupportedVersion);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertProblemCodeAsync(
            response,
            IdentityErrorCodes.SignatureInvalidEncoding,
            cancellationToken);
    }

    private static async Task VerifyEmptyAndNonEmptyBodyAsync(
        HttpClient client,
        CreateHostApiKeyResponse created,
        CancellationToken cancellationToken)
    {
        using var empty = await SendSignedAsync(
            client,
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1",
            created.Secret,
            created.Key.KeyPrefix,
            body: null,
            cancellationToken: cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, empty.StatusCode);

        var payload = """{"probe":true}"""u8.ToArray();
        using var nonEmpty = await SendSignedAsync(
            client,
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1",
            created.Secret,
            created.Key.KeyPrefix,
            body: payload,
            cancellationToken: cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, nonEmpty.StatusCode);
    }

    private static async Task VerifyBodyTamperingIsRejectedAsync(
        HttpClient client,
        CreateHostApiKeyResponse created,
        CancellationToken cancellationToken)
    {
        var payload = "tamper"u8.ToArray();
        const string requestPath = "/api/v1/identity/users?page=1&pageSize=1";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
        request.Content = new ByteArrayContent(payload);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString("N")[..24];
        var canonical = SignatureCanonicalRequest.BuildCanonicalString(
            "GET",
            "/api/v1/identity/users",
            "page=1&pageSize=1",
            SignatureCanonicalRequest.ComputeContentHash([]),
            created.Key.KeyPrefix,
            timestamp,
            nonce);
        var signingKey = SignatureCanonicalRequest.ParseSigningKeyBytes(
            TokenHash.Compute(created.Secret));
        var signature = SignatureCanonicalRequest.ComputeSignature(canonical, signingKey);
        request.Headers.Add(SignatureAuthenticationOptions.AccessKeyIdHeader, created.Key.KeyPrefix);
        request.Headers.Add(SignatureAuthenticationOptions.TimestampHeader, timestamp);
        request.Headers.Add(SignatureAuthenticationOptions.NonceHeader, nonce);
        request.Headers.Add(SignatureAuthenticationOptions.SignatureHeader, signature);
        request.Headers.Add(
            SignatureAuthenticationOptions.SignatureVersionHeader,
            SignatureAuthenticationOptions.SupportedVersion);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertProblemCodeAsync(
            response,
            IdentityErrorCodes.SignatureInvalidSignature,
            cancellationToken);
    }

    private static async Task VerifyExpiredAndFutureTimestampsAsync(
        HttpClient client,
        CreateHostApiKeyResponse created,
        CancellationToken cancellationToken)
    {
        using var expired = await SendSignedAsync(
            client,
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1",
            created.Secret,
            created.Key.KeyPrefix,
            timestampOverride: DateTimeOffset.UtcNow.AddHours(-2),
            cancellationToken: cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, expired.StatusCode);
        await AssertProblemCodeAsync(
            expired,
            IdentityErrorCodes.SignatureTimestampExpired,
            cancellationToken);

        using var future = await SendSignedAsync(
            client,
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1",
            created.Secret,
            created.Key.KeyPrefix,
            timestampOverride: DateTimeOffset.UtcNow.AddHours(2),
            cancellationToken: cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, future.StatusCode);
        await AssertProblemCodeAsync(
            future,
            IdentityErrorCodes.SignatureTimestampInFuture,
            cancellationToken);
    }

    private static async Task VerifyNonceReplayAndConcurrentReplayAsync(
        HttpClient client,
        CreateHostApiKeyResponse created,
        CancellationToken cancellationToken)
    {
        const string nonce = "nonceabcdefghijklm";
        using var first = await SendSignedAsync(
            client,
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1",
            created.Secret,
            created.Key.KeyPrefix,
            nonce: nonce,
            cancellationToken: cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);

        using var replay = await SendSignedAsync(
            client,
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1",
            created.Secret,
            created.Key.KeyPrefix,
            nonce: nonce,
            cancellationToken: cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, replay.StatusCode);
        await AssertProblemCodeAsync(
            replay,
            IdentityErrorCodes.SignatureReplayDetected,
            cancellationToken);

        var concurrentNonce = $"concurrent{Guid.NewGuid():N}"[..24];
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => SendSignedAsync(
                client,
                HttpMethod.Get,
                "/api/v1/identity/users?page=1&pageSize=1",
                created.Secret,
                created.Key.KeyPrefix,
                nonce: concurrentNonce,
                cancellationToken: cancellationToken))
            .ToArray();
        var responses = await Task.WhenAll(tasks);
        Assert.AreEqual(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.AreEqual(
            7,
            responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    private static async Task VerifyRotatedDisabledAndExpiredKeysAsync(
        FullNetApiFactory factory,
        HttpClient client,
        string adminToken,
        Guid adminUserId,
        CreateHostApiKeyResponse created,
        CancellationToken cancellationToken)
    {
        using var rotateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/api-keys/{created.Key.Id:D}/rotate",
            adminToken,
            new { });
        using var rotateResponse = await client.SendAsync(rotateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, rotateResponse.StatusCode);
        var rotated = await rotateResponse.Content
            .ReadFromJsonAsync<CreateHostApiKeyResponse>(cancellationToken);
        Assert.IsNotNull(rotated);

        using var oldKeyResponse = await SendSignedAsync(
            client,
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1",
            created.Secret,
            created.Key.KeyPrefix,
            cancellationToken: cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, oldKeyResponse.StatusCode);
        await AssertProblemCodeAsync(
            oldKeyResponse,
            IdentityErrorCodes.SignatureAccessKeyDisabled,
            cancellationToken);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/api-keys/{rotated.Key.Id:D}/disable",
            adminToken,
            new { });
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);

        using var disabledResponse = await SendSignedAsync(
            client,
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1",
            rotated.Secret,
            rotated.Key.KeyPrefix,
            cancellationToken: cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, disabledResponse.StatusCode);

        var expiredKey = await CreateApiKeyAsync(
            client,
            adminToken,
            adminUserId,
            cancellationToken);
        await ExpireApiKeyAsync(factory, expiredKey.Key.Id, cancellationToken);
        using var expiredResponse = await SendSignedAsync(
            client,
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1",
            expiredKey.Secret,
            expiredKey.Key.KeyPrefix,
            cancellationToken: cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, expiredResponse.StatusCode);
        await AssertProblemCodeAsync(
            expiredResponse,
            IdentityErrorCodes.SignatureAccessKeyExpired,
            cancellationToken);
    }

    private static async Task VerifyHostKeyCannotBindTenantHeaderAsync(
        HttpClient client,
        CreateHostApiKeyResponse created,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1");
        ApplySignatureHeaders(
            request,
            created.Secret,
            created.Key.KeyPrefix,
            "/api/v1/identity/users?page=1&pageSize=1");
        request.Headers.Add(
            SignatureAuthenticationOptions.TenantIdHeader,
            Guid.CreateVersion7().ToString("D"));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemCodeAsync(
            response,
            IdentityErrorCodes.SignatureTenantScopeMismatch,
            cancellationToken);
    }

    private static async Task VerifyTenantKeyCannotCrossTenantAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var secret = $"fnk_{Guid.NewGuid():N}{Guid.NewGuid():N}"[..36];
        var prefix = secret[..16];
        await InsertTenantApiKeyAsync(
            factory,
            tenantA,
            prefix,
            TokenHash.Compute(secret),
            cancellationToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1");
        ApplySignatureHeaders(request, secret, prefix, "/api/v1/identity/users?page=1&pageSize=1");
        request.Headers.Add(
            SignatureAuthenticationOptions.TenantIdHeader,
            tenantB.ToString("D"));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemCodeAsync(
            response,
            IdentityErrorCodes.SignatureTenantScopeMismatch,
            cancellationToken);
    }

    private static async Task VerifyFailureLoggingDoesNotLeakSecretsAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CreateHostApiKeyResponse created,
        CancellationToken cancellationToken)
    {
        var auditCountBefore = await factory.GetAuthenticationAuditCountAsync(cancellationToken);
        using var response = await SendSignedAsync(
            client,
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1",
            created.Secret,
            created.Key.KeyPrefix,
            signatureOverride: new string('a', 64),
            cancellationToken: cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.IsTrue(
            await factory.GetAuthenticationAuditCountAsync(cancellationToken) > auditCountBefore);

        await using var scope = factory.Services.CreateAsyncScope();
        var sql = factory.Provider == DatabaseProvider.SqlServer
            ? """
              SELECT TOP (1) CONCAT(
                  ResultCode COLLATE DATABASE_DEFAULT,
                  N'|',
                  UsernameFingerprint COLLATE DATABASE_DEFAULT,
                  N'|',
                  ISNULL(IpAddress COLLATE DATABASE_DEFAULT, N''))
              FROM fn_identity_auth_audit
              WHERE EventType = 'signature_authentication'
                AND Succeeded = 0
              ORDER BY OccurredAtUtc DESC, Id DESC
              """
            : """
              SELECT CONCAT(ResultCode, '|', UsernameFingerprint, '|', IFNULL(IpAddress, ''))
              FROM fn_identity_auth_audit
              WHERE EventType = 'signature_authentication'
                AND Succeeded = 0
              ORDER BY OccurredAtUtc DESC, Id DESC
              LIMIT 1
              """;
        var latest = await scope.ServiceProvider
            .GetRequiredService<IQueryExecutor>()
            .QuerySingleOrDefaultAsync<string>(
                new SqlStatement(
                    "integration.identity.read_latest_signature_audit",
                    sql,
                    SqlDataScope.Global),
                cancellationToken: cancellationToken);
        Assert.IsNotNull(latest);
        Assert.IsFalse(latest.Contains(created.Secret, StringComparison.Ordinal));
        Assert.IsFalse(latest.Contains(new string('a', 64), StringComparison.Ordinal));
    }

    private static async Task VerifyDuplicateHeadersAreRejectedAsync(
        HttpClient client,
        CreateHostApiKeyResponse created,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1");
        ApplySignatureHeaders(
            request,
            created.Secret,
            created.Key.KeyPrefix,
            "/api/v1/identity/users?page=1&pageSize=1");
        request.Headers.Remove(SignatureAuthenticationOptions.NonceHeader);
        request.Headers.TryAddWithoutValidation(
            SignatureAuthenticationOptions.NonceHeader,
            new[] { "nonceabcdefghijklm", "nonceabcdefghijklmn" });
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertProblemCodeAsync(
            response,
            IdentityErrorCodes.SignatureDuplicateHeaders,
            cancellationToken);
    }

    private static async Task VerifyCorruptKeyHashIsRejectedAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CreateHostApiKeyResponse created,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        await executor.ExecuteAsync(
            new SqlStatement(
                "integration.identity.corrupt_api_key_hash",
                """
                UPDATE fn_identity_api_key
                SET KeyHash = @KeyHash
                WHERE Id = @ApiKeyId
                """,
                SqlDataScope.Global),
            new
            {
                ApiKeyId = created.Key.Id,
                KeyHash = "not-a-valid-hash-value-0123456789abcdef",
            },
            cancellationToken);

        using var response = await SendSignedAsync(
            client,
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1",
            created.Secret,
            created.Key.KeyPrefix,
            cancellationToken: cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertProblemCodeAsync(
            response,
            IdentityErrorCodes.SignatureAccessKeyDisabled,
            cancellationToken);
    }

    private static async Task VerifyOversizedBodyIsRejectedAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CreateHostApiKeyResponse created,
        CancellationToken cancellationToken)
    {
        var payload = new byte[1_048_577];
        Random.Shared.NextBytes(payload);
        using var response = await SendSignedAsync(
            client,
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1",
            created.Secret,
            created.Key.KeyPrefix,
            body: payload,
            cancellationToken: cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertProblemCodeAsync(
            response,
            IdentityErrorCodes.SignatureRequestBodyTooLarge,
            cancellationToken);
    }

    private static async Task<HttpResponseMessage> SendSignedAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string secret,
        string accessKeyId,
        byte[]? body = null,
        string? nonce = null,
        DateTimeOffset? timestampOverride = null,
        string? signatureOverride = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
        }

        ApplySignatureHeaders(
            request,
            secret,
            accessKeyId,
            path,
            body,
            nonce,
            timestampOverride,
            signatureOverride);
        return await client.SendAsync(request, cancellationToken);
    }

    private static void ApplySignatureHeaders(
        HttpRequestMessage request,
        string secret,
        string accessKeyId,
        string requestPath,
        byte[]? body = null,
        string? nonce = null,
        DateTimeOffset? timestampOverride = null,
        string? signatureOverride = null)
    {
        var timestamp = (timestampOverride ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds()
            .ToString();
        nonce ??= Guid.NewGuid().ToString("N")[..24];
        var queryIndex = requestPath.IndexOf('?', StringComparison.Ordinal);
        var canonicalPath = queryIndex >= 0
            ? requestPath[..queryIndex]
            : requestPath;
        var rawQuery = queryIndex >= 0
            ? requestPath[(queryIndex + 1)..]
            : string.Empty;
        var canonicalQuery = string.IsNullOrEmpty(rawQuery)
            ? string.Empty
            : SignatureCanonicalRequest.BuildCanonicalQuery(new QueryString($"?{rawQuery}"));
        var bodyBytes = body ?? [];
        if (signatureOverride is null && request.Content is not null)
        {
            bodyBytes = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            request.Content = new ByteArrayContent(bodyBytes);
            if (request.Content.Headers.ContentType is null)
            {
                request.Content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            }
        }

        var contentHash = SignatureCanonicalRequest.ComputeContentHash(bodyBytes);
        var canonical = SignatureCanonicalRequest.BuildCanonicalString(
            request.Method.Method.ToUpperInvariant(),
            canonicalPath,
            canonicalQuery,
            contentHash,
            accessKeyId,
            timestamp,
            nonce);
        var signingKey = SignatureCanonicalRequest.ParseSigningKeyBytes(
            TokenHash.Compute(secret));
        var signature = signatureOverride
            ?? SignatureCanonicalRequest.ComputeSignature(canonical, signingKey);

        request.Headers.Add(SignatureAuthenticationOptions.AccessKeyIdHeader, accessKeyId);
        request.Headers.Add(SignatureAuthenticationOptions.TimestampHeader, timestamp);
        request.Headers.Add(SignatureAuthenticationOptions.NonceHeader, nonce);
        request.Headers.Add(SignatureAuthenticationOptions.SignatureHeader, signature);
        request.Headers.Add(
            SignatureAuthenticationOptions.SignatureVersionHeader,
            SignatureAuthenticationOptions.SupportedVersion);
    }

    private static async Task AssertProblemCodeAsync(
        HttpResponseMessage response,
        string expectedCode,
        CancellationToken cancellationToken)
    {
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            expectedCode,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task<CreateHostApiKeyResponse> CreateApiKeyAsync(
        HttpClient client,
        string adminToken,
        Guid adminUserId,
        CancellationToken cancellationToken,
        DateTimeOffset? expiresAtUtc = null)
    {
        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/api-keys",
            adminToken,
            new CreateHostApiKeyRequest(
                adminUserId,
                $"签名测试-{Guid.NewGuid():N}",
                [IdentityUserManagementPermissions.Read],
                expiresAtUtc));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content
            .ReadFromJsonAsync<CreateHostApiKeyResponse>(cancellationToken);
        Assert.IsNotNull(created);
        return created;
    }

    private static async Task ExpireApiKeyAsync(
        FullNetApiFactory factory,
        Guid apiKeyId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<ICommandExecutor>()
            .ExecuteAsync(
                new SqlStatement(
                    "integration.identity.expire_api_key",
                    """
                    UPDATE fn_identity_api_key
                    SET ExpiresAtUtc = @ExpiresAtUtc
                    WHERE Id = @ApiKeyId
                    """,
                    SqlDataScope.Global),
                new
                {
                    ApiKeyId = apiKeyId,
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                },
                cancellationToken);
    }

    private static async Task InsertTenantApiKeyAsync(
        FullNetApiFactory factory,
        Guid tenantId,
        string keyPrefix,
        string keyHash,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var userId = Guid.CreateVersion7();
        var apiKeyId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        await executor.ExecuteAsync(
            new SqlStatement(
                "integration.identity.insert_tenant_user",
                """
                INSERT INTO fn_identity_user
                    (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
                     PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc, SecurityStamp,
                     CreatedAtUtc, UpdatedAtUtc, Version)
                VALUES
                    (@Id, @TenantId, @ScopeKey, @Username, @NormalizedUsername, @DisplayName,
                     @PasswordHash, 1, 0, NULL, @SecurityStamp,
                     @NowUtc, @NowUtc, 1)
                """,
                SqlDataScope.Global),
            new
            {
                Id = userId,
                TenantId = tenantId,
                ScopeKey = $"tenant:{tenantId:N}",
                Username = $"tenant-{tenantId:N}"[..32],
                NormalizedUsername = $"TENANT-{tenantId:N}"[..32],
                DisplayName = "Tenant API User",
                PasswordHash = "integration-only",
                SecurityStamp = Guid.NewGuid().ToString("N"),
                NowUtc = now,
            },
            cancellationToken);
        await executor.ExecuteAsync(
            new SqlStatement(
                "integration.identity.insert_tenant_api_key",
                """
                INSERT INTO fn_identity_api_key
                    (Id, UserId, DisplayName, KeyPrefix, KeyHash, PermissionsJson,
                     ExpiresAtUtc, IsActive, LastUsedAtUtc, DisabledAtUtc,
                     CreatedAtUtc, UpdatedAtUtc, Version)
                VALUES
                    (@Id, @UserId, @DisplayName, @KeyPrefix, @KeyHash, @PermissionsJson,
                     @ExpiresAtUtc, @IsActive, @LastUsedAtUtc, @DisabledAtUtc,
                     @CreatedAtUtc, @UpdatedAtUtc, @Version)
                """,
                SqlDataScope.Global),
            new
            {
                Id = apiKeyId,
                UserId = userId,
                DisplayName = "Tenant Signature Key",
                KeyPrefix = keyPrefix,
                KeyHash = keyHash,
                PermissionsJson = """["identity.users.read"]""",
                ExpiresAtUtc = (DateTimeOffset?)null,
                IsActive = true,
                LastUsedAtUtc = (DateTimeOffset?)null,
                DisabledAtUtc = (DateTimeOffset?)null,
                CreatedAtUtc = now,
                UpdatedAtUtc = (DateTimeOffset?)null,
                Version = 1,
            },
            cancellationToken);
    }

    private static async Task<Guid> ResolveAdminUserIdAsync(
        HttpClient client,
        string adminToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=50");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var response = await client.SendAsync(request, cancellationToken);
        var page = await response.Content
            .ReadFromJsonAsync<PagedResult<HostUserResponse>>(cancellationToken);
        Assert.IsNotNull(page);
        return page.Items.Single(item => item.Username == "admin").Id;
    }

    private static async Task<string> LoginAsHostAdminAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        Assert.IsNotNull(token);
        return token.AccessToken;
    }

    private static HttpRequestMessage CreateBearerJsonRequest<TRequest>(
        HttpMethod method,
        string path,
        string accessToken,
        TRequest body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }
}
