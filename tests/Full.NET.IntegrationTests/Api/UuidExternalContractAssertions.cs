using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Api;

/// <summary>
/// 验证 Binary16 存储边界下，公共 HTTP/JSON 契约仍以规范 UUID 字符串对外暴露。
/// </summary>
internal static partial class UuidExternalContractAssertions
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> UuidPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "userId",
        "tenantId",
        "targetUserId",
        "actorUserId",
        "sessionId",
    };

    [GeneratedRegex(
        "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
        RegexOptions.Compiled)]
    private static partial Regex CanonicalUuidPattern();

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        using var loginRequest = CreateLoginRequest(FullNetApiFactory.TestPassword);
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginToken = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            WebJson,
            cancellationToken);
        Assert.IsNotNull(loginToken);

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            loginToken.AccessToken);
        using var meResponse = await client.SendAsync(meRequest, cancellationToken);
        var meJson = await meResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, meResponse.StatusCode);
        using (var meDocument = JsonDocument.Parse(meJson))
        {
            AssertCanonicalUuidProperties(meDocument.RootElement);
            Assert.AreEqual(
                JsonValueKind.Null,
                meDocument.RootElement.GetProperty("tenantId").ValueKind);
        }

        using var availableRequest = CreateBearerRequest(
            HttpMethod.Get,
            "/api/v1/tenancy/available",
            loginToken.AccessToken);
        using var availableResponse = await client.SendAsync(
            availableRequest,
            cancellationToken);
        var availableJson = await availableResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, availableResponse.StatusCode);
        using (var availableDocument = JsonDocument.Parse(availableJson))
        {
            AssertCanonicalUuidProperties(availableDocument.RootElement);
        }

        var available = JsonSerializer.Deserialize<TenantContextSummary[]>(
            availableJson,
            WebJson);
        Assert.IsNotNull(available);
        var acme = available.Single(tenant => tenant.Identifier == "acme");

        var targetIdentity = await factory.CreateHostIdentityAsync(
            $"uuid-contract-{Guid.NewGuid():N}",
            [],
            cancellationToken);
        using var grantRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/super-administrators/grant",
            loginToken.AccessToken,
            new GrantSuperAdministratorRequest(
                targetIdentity.Username,
                FullNetApiFactory.TestPassword));
        using var grantResponse = await client.SendAsync(grantRequest, cancellationToken);
        var grantJson = await grantResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, grantResponse.StatusCode);
        using (var grantDocument = JsonDocument.Parse(grantJson))
        {
            AssertCanonicalUuidProperties(grantDocument.RootElement);
            AssertCanonicalUuidString(
                grantDocument.RootElement.GetProperty("targetUserId").GetString());
        }

        using var auditsRequest = CreateBearerRequest(
            HttpMethod.Get,
            "/api/v1/identity/super-administrators/audits?limit=5",
            loginToken.AccessToken);
        using var auditsResponse = await client.SendAsync(auditsRequest, cancellationToken);
        var auditsJson = await auditsResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, auditsResponse.StatusCode);
        using (var auditsDocument = JsonDocument.Parse(auditsJson))
        {
            AssertCanonicalUuidProperties(auditsDocument.RootElement);
        }

        using var enterRequest = CreateContextRequest(acme.Id, loginToken.AccessToken);
        using var enterResponse = await client.SendAsync(enterRequest, cancellationToken);
        var enterJson = await enterResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enterResponse.StatusCode);
        using (var enterDocument = JsonDocument.Parse(enterJson))
        {
            AssertCanonicalUuidProperties(enterDocument.RootElement);
            AssertCanonicalUuidString(
                enterDocument.RootElement
                    .GetProperty("context")
                    .GetProperty("tenantId")
                    .GetString());
        }

        using var forbiddenRequest = CreateBearerRequest(
            HttpMethod.Get,
            "/api/v1/navigation",
            await factory.CreateHostAccessTokenAsync(
                ["platform.dashboard.read"],
                cancellationToken));
        using var forbiddenResponse = await client.SendAsync(
            forbiddenRequest,
            cancellationToken);
        var problemJson = await forbiddenResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
        using (var problemDocument = JsonDocument.Parse(problemJson))
        {
            Assert.IsFalse(problemDocument.RootElement.TryGetProperty("tenantId", out _));
            Assert.IsFalse(problemDocument.RootElement.TryGetProperty("userId", out _));
            Assert.IsFalse(string.IsNullOrWhiteSpace(
                problemDocument.RootElement.GetProperty("traceId").GetString()));
        }
    }

    private static void AssertCanonicalUuidProperties(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String
                        && UuidPropertyNames.Contains(property.Name))
                    {
                        AssertCanonicalUuidString(property.Value.GetString());
                    }

                    AssertCanonicalUuidProperties(property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AssertCanonicalUuidProperties(item);
                }

                break;
        }
    }

    private static void AssertCanonicalUuidString(string? value)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(value));
        Assert.IsTrue(
            CanonicalUuidPattern().IsMatch(value!),
            $"JSON UUID 必须使用小写连字符格式，实际值：{value}");
        Assert.AreEqual(value, value!.ToLowerInvariant());
    }

    private static HttpRequestMessage CreateLoginRequest(string password)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new
            {
                username = "admin",
                password,
            }),
        };
        request.Headers.Add("Origin", "http://localhost");
        return request;
    }

    private static HttpRequestMessage CreateBearerRequest(
        HttpMethod method,
        string path,
        string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
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
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }

    private static HttpRequestMessage CreateContextRequest(Guid tenantId, string accessToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/tenancy/context")
        {
            Content = JsonContent.Create(new ChangeTenantContextRequest(tenantId)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }
}
