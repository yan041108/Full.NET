using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>
/// Native 原生产物上的最小关键 HTTP 链路断言。
/// </summary>
internal static class NativeApiE2EAssertions
{
    public const string AdminPassword = "FullNet!2026Integration";

    /// <summary>仅验证 AI 工具目录与 MCP 受保护资源元数据在 Native 进程上可达。</summary>
    public static async Task VerifyAiModuleNativeClosureFlowAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        _ = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiDatabaseBootstrap.BootstrapAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);

        await using var host = await NativeApiProcessHost.StartAsync(
            NativeApiArtifactLocator.RequireArtifact(),
            provider,
            connectionString,
            NativePdfKnowledgeAssertions.Settings(NativeApiArtifactLocator.RequireArtifact().ExecutablePath),
            NativeAotTestTimeouts.ProcessStartup,
            cancellationToken).ConfigureAwait(false);

        using var client = host.CreateClient();
        var token = await LoginAsync(client, host.LogFilePath, cancellationToken)
            .ConfigureAwait(false);
        await VerifyAiModuleNativeClosureAsync(client, token, provider, connectionString, cancellationToken).ConfigureAwait(false);
        await NativePdfKnowledgeAssertions.VerifyAsync(client, token, provider, connectionString, cancellationToken).ConfigureAwait(false);
        // 专用 AI 探针在末尾切租户，避免让共享 critical flow 的旧令牌失效。
        var tenantToken = await EnterDevelopmentTenantAsync(client, token, cancellationToken).ConfigureAwait(false);
        using var tenantCreate = AuthorizedJson(HttpMethod.Post, "/api/v1/ai/knowledge-bases", tenantToken, new CreateAiKnowledgeBaseRequest("原生租户目录", null));
        using var tenantCreatedResponse = await client.SendAsync(tenantCreate, cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(tenantCreatedResponse, HttpStatusCode.Created, "Create native tenant knowledge catalog", cancellationToken).ConfigureAwait(false);
        var tenantBase = (await tenantCreatedResponse.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>(cancellationToken).ConfigureAwait(false))!;
        using var tenantContextGet = Authorized(HttpMethod.Get, "/api/v1/tenancy/current", tenantToken);
        using var tenantContextResponse = await client.SendAsync(tenantContextGet, cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(tenantContextResponse, HttpStatusCode.OK, "Read native trusted tenant", cancellationToken).ConfigureAwait(false);
        var trustedTenant = (await tenantContextResponse.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken).ConfigureAwait(false))!;
        // 原生机构目录必须物化非空投影，不能以 Host 空候选替代 Tenant SQL 验证。
        var organizationCandidate = await PostAndReadAsync<CreateOrganizationUnitRequest, OrganizationUnitResponse>(client,
            "/api/v1/organization/units", tenantToken, new CreateOrganizationUnitRequest(null, $"aot-candidate-{Guid.NewGuid():N}", "原生机构候选", 0),
            HttpStatusCode.Created, cancellationToken).ConfigureAwait(false);
        await AssertPageContainsAsync<AiKnowledgeOrganizationCandidateResponse>(client,
            $"/api/v1/ai/knowledge-bases/{tenantBase.Id}/organization-candidates", tenantToken,
            unit => unit.Id == organizationCandidate.Id, cancellationToken).ConfigureAwait(false);
        var candidateRoleId = await NativeAiRoleCandidateSeed.InsertAsync(provider, connectionString, trustedTenant.Id, cancellationToken).ConfigureAwait(false);
        using var tenantActorRequest = Authorized(HttpMethod.Get, "/api/v1/me", tenantToken);
        using var tenantActorResponse = await client.SendAsync(tenantActorRequest, cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(tenantActorResponse, HttpStatusCode.OK, "Read native trusted tenant actor", cancellationToken).ConfigureAwait(false);
        var tenantActor = (await tenantActorResponse.Content.ReadFromJsonAsync<CurrentUserResponse>(cancellationToken).ConfigureAwait(false))!;
        await NativeAiRoleCandidateSeed.AssignAsync(provider, connectionString, tenantActor.Id, candidateRoleId, cancellationToken).ConfigureAwait(false);
        await AssertPageContainsAsync<AiKnowledgeRoleCandidateResponse>(client, $"/api/v1/ai/knowledge-bases/{tenantBase.Id}/role-candidates", tenantToken,
            role => role.Id == candidateRoleId, cancellationToken).ConfigureAwait(false);
        await AssertPageContainsAsync<AiKnowledgeBaseResponse>(client, "/api/v1/ai/knowledge-bases", tenantToken,
            item => item.Id == tenantBase.Id, cancellationToken).ConfigureAwait(false);
        // 原生 Tenant 目录 Port 必须物化真实活动成员；空名单无法证明依赖闭包。
        var tenantMember = await PostAndReadAsync<ProvisionTenantMemberRequest, TenantMemberResponse>(client,
            "/api/v1/identity/tenant-members/provision", tenantToken,
            new ProvisionTenantMemberRequest($"naot-kb-tenant-{Guid.NewGuid():N}", "原生成员", AdminPassword, TenantMemberRoles.Member, null),
            HttpStatusCode.OK, cancellationToken).ConfigureAwait(false);
        using var tenantGrant = AuthorizedJson(HttpMethod.Put, $"/api/v1/ai/knowledge-bases/{tenantBase.Id}/members", tenantToken,
            new SetAiKnowledgeMembersRequest([tenantMember.UserId], tenantBase.Version));
        using var tenantGranted = await client.SendAsync(tenantGrant, cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(tenantGranted, HttpStatusCode.OK, "Grant native tenant knowledge member", cancellationToken).ConfigureAwait(false);
        var tenantMembers = (await tenantGranted.Content.ReadFromJsonAsync<AiKnowledgeMembersResponse>(cancellationToken).ConfigureAwait(false))!;
        CollectionAssert.AreEqual(new[] { tenantMember.UserId }, tenantMembers.UserIds.ToArray());
        var tenantDocPath = $"/api/v1/ai/knowledge-bases/{tenantBase.Id}/documents";
        var tenantDocument = await PostAndReadAsync<CreateAiKnowledgeDocumentRequest, AiKnowledgeDocumentResponse>(client, tenantDocPath, tenantToken,
            new CreateAiKnowledgeDocumentRequest("原生租户草稿", null), HttpStatusCode.Created, cancellationToken).ConfigureAwait(false);
        await AssertPageContainsAsync<AiKnowledgeOrganizationCandidateResponse>(client, tenantDocPath + $"/{tenantDocument.Id}/organization-candidates", tenantToken,
            unit => unit.Id == organizationCandidate.Id, cancellationToken).ConfigureAwait(false);
        await AssertPageContainsAsync<AiKnowledgeRoleCandidateResponse>(client, tenantDocPath + $"/{tenantDocument.Id}/role-candidates", tenantToken,
            role => role.Id == candidateRoleId, cancellationToken).ConfigureAwait(false);
        using var tenantDocGrant = AuthorizedJson(HttpMethod.Put, tenantDocPath + $"/{tenantDocument.Id}/members", tenantToken,
            new SetAiKnowledgeDocumentMembersRequest([tenantMember.UserId], tenantDocument.Version));
        using var tenantDocGranted = await client.SendAsync(tenantDocGrant, cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(tenantDocGranted, HttpStatusCode.OK, "Grant native tenant document access", cancellationToken).ConfigureAwait(false);
        await VerifyKnowledgeFileNativeAsync(client, tenantToken, tenantDocPath + $"/{tenantDocument.Id}", tenantDocument.Version + 1, cancellationToken).ConfigureAwait(false);
        // Tenant 原生路径实际绑定非空集合并物化角色授予 DTO；主版本同时供旧名单接口使用。
        foreach (var resource in new[] { $"/api/v1/ai/knowledge-bases/{tenantBase.Id}", tenantDocPath + $"/{tenantDocument.Id}" })
        {
            using var currentGrant = Authorized(HttpMethod.Get, resource + "/roles", tenantToken);
            using var currentGrantResponse = await client.SendAsync(currentGrant, cancellationToken).ConfigureAwait(false);
            await AssertStatusAsync(currentGrantResponse, HttpStatusCode.OK, "Read native tenant role grants", cancellationToken).ConfigureAwait(false);
            var currentRoles = (await currentGrantResponse.Content.ReadFromJsonAsync<AiKnowledgeRoleGrantsResponse>(cancellationToken).ConfigureAwait(false))!;
            using var set = AuthorizedJson(HttpMethod.Put, resource + "/roles", tenantToken, new SetAiKnowledgeRoleGrantsRequest([candidateRoleId], currentRoles.Version));
            using var setResponse = await client.SendAsync(set, cancellationToken).ConfigureAwait(false);
            await AssertStatusAsync(setResponse, HttpStatusCode.OK, "Grant native tenant role", cancellationToken).ConfigureAwait(false);
        }
        await AssertPageContainsAsync<AiKnowledgeDocumentResponse>(client, tenantDocPath, tenantToken,
            item => item.Id == tenantDocument.Id && item.Status == "draft", cancellationToken).ConfigureAwait(false);
        using var tenantRevoke = AuthorizedJson(HttpMethod.Put, $"/api/v1/ai/knowledge-bases/{tenantBase.Id}/members", tenantToken,
            new SetAiKnowledgeMembersRequest([], tenantMembers.Version + 1));
        using var tenantRevoked = await client.SendAsync(tenantRevoke, cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(tenantRevoked, HttpStatusCode.OK, "Revoke native tenant knowledge member", cancellationToken).ConfigureAwait(false);
        // 原生路径必须绑定非空机构集合，并实际读取当前主体的活动机构隶属。
        await PostAndReadAsync<CreateOrganizationUserUnitRequest, OrganizationUserUnitResponse>(client,
            "/api/v1/organization/user-units", tenantToken,
            new CreateOrganizationUserUnitRequest(tenantActor.Id, organizationCandidate.Id, false),
            HttpStatusCode.Created, cancellationToken).ConfigureAwait(false);
        foreach (var resource in new[] { $"/api/v1/ai/knowledge-bases/{tenantBase.Id}", tenantDocPath + $"/{tenantDocument.Id}" })
        {
            using var readOrganizations = Authorized(HttpMethod.Get, resource + "/organizations", tenantToken);
            using var organizationsResponse = await client.SendAsync(readOrganizations, cancellationToken).ConfigureAwait(false);
            await AssertStatusAsync(organizationsResponse, HttpStatusCode.OK, "Read native organization grants", cancellationToken).ConfigureAwait(false);
            var current = (await organizationsResponse.Content.ReadFromJsonAsync<AiKnowledgeOrganizationGrantsResponse>(cancellationToken).ConfigureAwait(false))!;
            using var setOrganizations = AuthorizedJson(HttpMethod.Put, resource + "/organizations", tenantToken,
                new SetAiKnowledgeOrganizationGrantsRequest([organizationCandidate.Id], current.Version));
            using var updated = await client.SendAsync(setOrganizations, cancellationToken).ConfigureAwait(false);
            await AssertStatusAsync(updated, HttpStatusCode.OK, "Set native organization grants", cancellationToken).ConfigureAwait(false);
            var grants = (await updated.Content.ReadFromJsonAsync<AiKnowledgeOrganizationGrantsResponse>(cancellationToken).ConfigureAwait(false))!;
            CollectionAssert.AreEqual(new[] { organizationCandidate.Id }, grants.OrganizationUnitIds.ToArray());
        }
        await AssertPageContainsAsync<AiKnowledgeDocumentResponse>(client, tenantDocPath, tenantToken,
            item => item.Id == tenantDocument.Id, cancellationToken).ConfigureAwait(false);
        await host.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        host.AssertNoFatalMarkersInLogs();
    }

    public static async Task VerifyCriticalHttpFlowAsync(
        DatabaseProvider provider,
        string connectionString,
        IReadOnlyDictionary<string, string?>? settingsOverrides = null,
        CancellationToken cancellationToken = default)
    {
        _ = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiDatabaseBootstrap.BootstrapAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);

        await using var host = await NativeApiProcessHost.StartAsync(
            NativeApiArtifactLocator.RequireArtifact(),
            provider,
            connectionString,
            settingsOverrides ?? new Dictionary<string, string?>(),
            NativeAotTestTimeouts.ProcessStartup,
            cancellationToken).ConfigureAwait(false);

        using var client = host.CreateClient();
        var token = await LoginAsync(client, host.LogFilePath, cancellationToken)
            .ConfigureAwait(false);
        await VerifyAuthenticatedMeAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await VerifyIdentityHostUserFlowAsync(
                client,
                token,
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        await VerifyTenancyReadAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await VerifyTenancyPackageFlowAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await VerifyCodeGenerationCatalogReadAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await VerifySerialNumbersFlowAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await VerifyDocumentFlowAsync(client, token, host.LogFilePath, cancellationToken)
            .ConfigureAwait(false);
        await VerifyAuditingFlowAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await VerifyMessagingDeadLetterFlowAsync(
                provider,
                connectionString,
                client,
                token,
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        await VerifyMessagingDeliveryStatusAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        var tenantToken = await EnterDevelopmentTenantAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await VerifyOrganizationFlowAsync(
                client,
                tenantToken,
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        // 进入租户会轮换 Host 会话绑定；AI Host 闭包检查需要重新登录后的 Host token。
        token = await LoginAsync(client, host.LogFilePath, cancellationToken)
            .ConfigureAwait(false);
        await VerifyAiModuleNativeClosureAsync(client, token, provider, connectionString, cancellationToken).ConfigureAwait(false);
        await VerifyReadinessAsync(client, cancellationToken).ConfigureAwait(false);
        await host.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        host.AssertNoFatalMarkersInLogs();
    }

    public static async Task<string> LoginAsync(
        HttpClient client,
        string? nativeLogFilePath = null,
        CancellationToken cancellationToken = default)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", AdminPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken)
            .ConfigureAwait(false);
        if (loginResponse.StatusCode != HttpStatusCode.OK)
        {
            var errorBody = await loginResponse.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            var logTail = ReadNativeLogTail(nativeLogFilePath);
            Assert.Fail(
                $"Login failed ({loginResponse.StatusCode}): {errorBody}"
                + (string.IsNullOrEmpty(logTail)
                    ? string.Empty
                    : $"\nNative log tail:\n{logTail}"));
        }
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
                cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(token);
        Assert.IsFalse(string.IsNullOrWhiteSpace(token.AccessToken));
        return token.AccessToken;
    }

    private static async Task VerifyAuthenticatedMeAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false));
        Assert.AreEqual("admin", payload.RootElement.GetProperty("username").GetString());
    }

    private static async Task VerifyIdentityHostUserFlowAsync(
        HttpClient client,
        string accessToken,
        string? nativeLogFilePath,
        CancellationToken cancellationToken)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var username = "naoti-" + suffix[..12];
        var phoneNumber = $"139{Random.Shared.NextInt64(10_000_000, 99_999_999)}";
        var profile = new HostUserProfileWriteRequest(
            FieldKeys: ["phone_number", "email", "employee_number", "id_card_type", "id_card_number"],
            Nickname: null,
            PhoneNumber: phoneNumber,
            Email: $" {username}@Example.COM ",
            EmployeeNumber: $" emp-{suffix[..8]} ",
            Gender: null,
            JoinDateUtc: null,
            SortOrder: null,
            IdCardType: "passport",
            IdCardNumber: $" p{suffix[..10]} ",
            BirthDate: null,
            Ethnicity: null,
            Address: null,
            GraduatedSchool: null,
            EducationLevel: null,
            PoliticalStatus: null,
            OfficePhone: null,
            EmergencyContact: null,
            EmergencyContactRelation: null,
            EmergencyContactPhone: null,
            EmergencyContactAddress: null,
            Remark: null,
            Version: null);
        var create = new CreateHostUserRequest(
            username,
            "Native AOT host user",
            AdminPassword,
            Profile: profile);
        var user = await PostAndReadAsync<
                CreateHostUserRequest,
                HostUserResponse>(
                client,
                "/api/v1/identity/users",
                accessToken,
                create,
                HttpStatusCode.Created,
                cancellationToken,
                nativeLogFilePath)
            .ConfigureAwait(false);
        Assert.AreEqual(username, user.Username);
        Assert.AreEqual(phoneNumber, user.Profile?.PhoneNumber);
        Assert.AreEqual($"{username}@example.com", user.Profile?.Email);

        using (var duplicateRequest = AuthorizedJson(
                   HttpMethod.Post,
                   "/api/v1/identity/users",
                   accessToken,
                   create))
        using (var duplicateResponse = await client
                   .SendAsync(duplicateRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    duplicateResponse,
                    HttpStatusCode.Conflict,
                    "Reject duplicate Native AOT host username",
                    cancellationToken)
                .ConfigureAwait(false);
            using var problem = JsonDocument.Parse(
                await duplicateResponse.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false));
            Assert.AreEqual(
                IdentityErrorCodes.UsernameExists,
                problem.RootElement.GetProperty("code").GetString());
        }

        using (var duplicateProfileRequest = AuthorizedJson(
                   HttpMethod.Post,
                   "/api/v1/identity/users",
                   accessToken,
                   create with { Username = $"naoti-profile-{suffix[..8]}" }))
        using (var duplicateProfileResponse = await client
                   .SendAsync(duplicateProfileRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    duplicateProfileResponse,
                    HttpStatusCode.Conflict,
                    "Reject duplicate Native AOT host profile",
                    cancellationToken)
                .ConfigureAwait(false);
            using var problem = JsonDocument.Parse(
                await duplicateProfileResponse.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false));
            Assert.AreEqual(
                IdentityErrorCodes.UserPhoneNumberExists,
                problem.RootElement.GetProperty("code").GetString());
        }

        await AssertPageContainsAsync<HostUserResponse>(
                client,
                "/api/v1/identity/users?page=1&pageSize=100",
                accessToken,
                item => item.Id == user.Id,
                cancellationToken)
            .ConfigureAwait(false);

        using var getRequest = Authorized(
            HttpMethod.Get,
            $"/api/v1/identity/users/{user.Id:D}",
            accessToken);
        using var getResponse = await client
            .SendAsync(getRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                getResponse,
                HttpStatusCode.OK,
                "Read Native AOT host user by id",
                cancellationToken)
            .ConfigureAwait(false);
        var found = await getResponse.Content
            .ReadFromJsonAsync<HostUserResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(user.Id, found?.Id);
    }

    private static async Task VerifyTenancyReadAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/tenants?page=1&pageSize=1");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(
            "application/json",
            response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task VerifyTenancyPackageFlowAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var packageCode = "native-aot-" + suffix;
        var create = new CreateHostTenantPackageRequest(
            packageCode,
            "Native AOT tenant package",
            "Native AOT materializer verification");
        var created = await PostAndReadAsync<
                CreateHostTenantPackageRequest,
                TenantPackageSummary>(
                client,
                "/api/v1/tenancy/tenant-packages",
                accessToken,
                create,
                HttpStatusCode.Created,
                cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(packageCode, created.Code);

        using (var duplicateRequest = AuthorizedJson(
                   HttpMethod.Post,
                   "/api/v1/tenancy/tenant-packages",
                   accessToken,
                   create))
        using (var duplicateResponse = await client
                   .SendAsync(duplicateRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    duplicateResponse,
                    HttpStatusCode.Conflict,
                    "Read Native AOT tenant package identity",
                    cancellationToken)
                .ConfigureAwait(false);
            using var problem = JsonDocument.Parse(
                await duplicateResponse.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false));
            Assert.AreEqual(
                TenancyErrorCodes.PackageCodeExists,
                problem.RootElement.GetProperty("code").GetString());
        }

        await AssertPageContainsAsync<TenantPackageSummary>(
                client,
                "/api/v1/tenancy/tenant-packages?page=1&pageSize=100",
                accessToken,
                item => item.Id == created.Id,
                cancellationToken)
            .ConfigureAwait(false);

        using var getRequest = Authorized(
            HttpMethod.Get,
            $"/api/v1/tenancy/tenant-packages/{created.Id:D}",
            accessToken);
        using var getResponse = await client.SendAsync(getRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                getResponse,
                HttpStatusCode.OK,
                "Read Native AOT tenant package by id",
                cancellationToken)
            .ConfigureAwait(false);
        var found = await getResponse.Content
            .ReadFromJsonAsync<TenantPackageSummary>(cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(created.Id, found?.Id);
    }

    private static async Task VerifyOrganizationFlowAsync(
        HttpClient client,
        string accessToken,
        string? nativeLogFilePath,
        CancellationToken cancellationToken)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var unitCode = "naotu-" + suffix[..12];
        var unitCreate = new CreateOrganizationUnitRequest(
            null,
            unitCode,
            "Native AOT organization unit",
            1);
        var unit = await PostAndReadAsync<
                CreateOrganizationUnitRequest,
                OrganizationUnitResponse>(
                client,
                "/api/v1/organization/units",
                accessToken,
                unitCreate,
                HttpStatusCode.Created,
                cancellationToken,
                nativeLogFilePath)
            .ConfigureAwait(false);
        Assert.AreEqual(unitCode, unit.Code);

        using (var duplicateRequest = AuthorizedJson(
                   HttpMethod.Post,
                   "/api/v1/organization/units",
                   accessToken,
                   unitCreate))
        using (var duplicateResponse = await client
                   .SendAsync(duplicateRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    duplicateResponse,
                    HttpStatusCode.Conflict,
                    "Reject duplicate Native AOT organization unit code",
                    cancellationToken)
                .ConfigureAwait(false);
            using var problem = JsonDocument.Parse(
                await duplicateResponse.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false));
            Assert.AreEqual(
                OrganizationErrorCodes.UnitCodeExists,
                problem.RootElement.GetProperty("code").GetString());
        }

        await AssertPageContainsAsync<OrganizationUnitResponse>(
                client,
                "/api/v1/organization/units?page=1&pageSize=100",
                accessToken,
                item => item.Id == unit.Id,
                cancellationToken)
            .ConfigureAwait(false);

        using (var getUnitRequest = Authorized(
                   HttpMethod.Get,
                   $"/api/v1/organization/units/{unit.Id:D}",
                   accessToken))
        using (var getUnitResponse = await client
                   .SendAsync(getUnitRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    getUnitResponse,
                    HttpStatusCode.OK,
                    "Read Native AOT organization unit by id",
                    cancellationToken)
                .ConfigureAwait(false);
            var foundUnit = await getUnitResponse.Content
                .ReadFromJsonAsync<OrganizationUnitResponse>(cancellationToken)
                .ConfigureAwait(false);
            Assert.AreEqual(unit.Id, foundUnit?.Id);
        }

        var positionLevelCode = "naotl-" + suffix[..12];
        var positionLevel = await PostAndReadAsync<
                CreateOrganizationPositionLevelRequest,
                OrganizationPositionLevelResponse>(
                client,
                "/api/v1/organization/position-levels",
                accessToken,
                new CreateOrganizationPositionLevelRequest(
                    positionLevelCode,
                    "Native AOT position level",
                    1),
                HttpStatusCode.Created,
                cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(positionLevelCode, positionLevel.Code);

        using (var getLevelRequest = Authorized(
                   HttpMethod.Get,
                   $"/api/v1/organization/position-levels/{positionLevel.Id:D}",
                   accessToken))
        using (var getLevelResponse = await client
                   .SendAsync(getLevelRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    getLevelResponse,
                    HttpStatusCode.OK,
                    "Read Native AOT organization position level by id",
                    cancellationToken)
                .ConfigureAwait(false);
            var foundLevel = await getLevelResponse.Content
                .ReadFromJsonAsync<OrganizationPositionLevelResponse>(cancellationToken)
                .ConfigureAwait(false);
            Assert.AreEqual(positionLevel.Id, foundLevel?.Id);
        }

        var positionCode = "naotp-" + suffix[..12];
        var position = await PostAndReadAsync<
                CreateOrganizationPositionRequest,
                OrganizationPositionResponse>(
                client,
                "/api/v1/organization/positions",
                accessToken,
                new CreateOrganizationPositionRequest(
                    positionCode,
                    "Native AOT organization position",
                    1),
                HttpStatusCode.Created,
                cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(positionCode, position.Code);

        await AssertPageContainsAsync<OrganizationPositionResponse>(
                client,
                "/api/v1/organization/positions?page=1&pageSize=100",
                accessToken,
                item => item.Id == position.Id,
                cancellationToken)
            .ConfigureAwait(false);

        using var getPositionRequest = Authorized(
            HttpMethod.Get,
            $"/api/v1/organization/positions/{position.Id:D}",
            accessToken);
        using var getPositionResponse = await client
            .SendAsync(getPositionRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                getPositionResponse,
                HttpStatusCode.OK,
                "Read Native AOT organization position by id",
                cancellationToken)
            .ConfigureAwait(false);
        var foundPosition = await getPositionResponse.Content
            .ReadFromJsonAsync<OrganizationPositionResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(position.Id, foundPosition?.Id);
    }

    public static Task<string> EnterLocalTenantAsync(
        HttpClient client,
        string hostAccessToken,
        CancellationToken cancellationToken = default) =>
        EnterDevelopmentTenantAsync(client, hostAccessToken, cancellationToken);

    private static async Task<string> EnterDevelopmentTenantAsync(
        HttpClient client,
        string hostAccessToken,
        CancellationToken cancellationToken)
    {
        using var availableRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/available");
        availableRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            hostAccessToken);
        using var availableResponse = await client.SendAsync(availableRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                availableResponse,
                HttpStatusCode.OK,
                "List available tenants",
                cancellationToken)
            .ConfigureAwait(false);
        var available = await availableResponse.Content
            .ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(available);
        var developmentTenant = available.SingleOrDefault(tenant =>
            tenant.Identifier == "local");
        Assert.IsNotNull(
            developmentTenant,
            "Available tenants did not contain the Development seed 'local': "
                + string.Join(", ", available.Select(tenant =>
                    $"{tenant.Identifier} ({tenant.Id})")));

        using var enterRequest = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/tenancy/context")
        {
            Content = JsonContent.Create(new ChangeTenantContextRequest(developmentTenant.Id)),
        };
        enterRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            hostAccessToken);
        using var enterResponse = await client.SendAsync(enterRequest, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, enterResponse.StatusCode);
        var entered = await enterResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(entered);
        Assert.IsFalse(string.IsNullOrWhiteSpace(entered.AccessToken));
        return entered.AccessToken;
    }

    private static async Task VerifyCodeGenerationCatalogReadAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using (var request = Authorized(
                   HttpMethod.Get,
                   "/api/v1/code-generation/catalog/tables",
                   accessToken))
        using (var response = await client.SendAsync(request, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    response,
                    HttpStatusCode.OK,
                    "Read Native AOT code generation catalog tables",
                    cancellationToken)
                .ConfigureAwait(false);
            var tables = await response.Content
                .ReadFromJsonAsync<IReadOnlyList<CodeGenerationCatalogTableResponse>>(
                    cancellationToken)
                .ConfigureAwait(false);
            Assert.IsNotNull(tables);
            Assert.IsNotEmpty(tables);

            using var columnsRequest = Authorized(
                HttpMethod.Get,
                "/api/v1/code-generation/catalog/tables/"
                    + Uri.EscapeDataString(tables[0].TableName)
                    + "/columns",
                accessToken);
            using var columnsResponse = await client
                .SendAsync(columnsRequest, cancellationToken)
                .ConfigureAwait(false);
            await AssertStatusAsync(
                    columnsResponse,
                    HttpStatusCode.OK,
                    "Read Native AOT code generation catalog columns",
                    cancellationToken)
                .ConfigureAwait(false);
            var columns = await columnsResponse.Content
                .ReadFromJsonAsync<CodeGenerationCatalogColumnListResponse>(cancellationToken)
                .ConfigureAwait(false);
            Assert.IsNotNull(columns);
            Assert.IsNotEmpty(columns.Columns);
        }

        var suffix = Guid.NewGuid().ToString("N");
        var schema = new CodeGenerationPreviewRequest(
            "acme",
            "catalog",
            "product",
            "acme_catalog_product",
            "Acme.Modules.Catalog",
            "Product",
            "products",
            "products",
            "TenantRequired",
            true,
            [
                new("Id", "Id", "id", "Uuid", false, null, null, null),
                new("TenantId", "TenantId", "tenantId", "Uuid", false, null, null, null),
                new("Name", "Name", "displayName", "String", false, 200, null, null),
                new("IsActive", "IsActive", "isActive", "Boolean", false, null, null, null),
                new("Version", "Version", "version", "Int64", false, null, null, null),
            ]);
        using var createTemplateRequest = AuthorizedJson(
            HttpMethod.Post,
            "/api/v1/code-generation/templates",
            accessToken,
            new CreateCodeGenerationTemplateRequest(
                "Native AOT " + suffix,
                "Native AOT materializer verification",
                schema));
        using var createTemplateResponse = await client
            .SendAsync(createTemplateRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                createTemplateResponse,
                HttpStatusCode.Created,
                "Create Native AOT code generation template",
                cancellationToken)
            .ConfigureAwait(false);
        var template = await createTemplateResponse.Content
            .ReadFromJsonAsync<CodeGenerationTemplateResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(template);

        using var previewRequest = AuthorizedJson(
            HttpMethod.Post,
            "/api/v1/code-generation/runs/preview",
            accessToken,
            new CodeGenerationRunPreviewRequest(template.Id, template.Version, null));
        using var previewResponse = await client.SendAsync(previewRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                previewResponse,
                HttpStatusCode.OK,
                "Create Native AOT code generation preview run",
                cancellationToken)
            .ConfigureAwait(false);
        var run = await previewResponse.Content
            .ReadFromJsonAsync<CodeGenerationRunPreviewResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(run);

        await AssertPageContainsAsync<CodeGenerationTemplateResponse>(
                client,
                "/api/v1/code-generation/templates?page=1&pageSize=20",
                accessToken,
                item => item.Id == template.Id,
                cancellationToken)
            .ConfigureAwait(false);
        await AssertPageContainsAsync<CodeGenerationRunResponse>(
                client,
                "/api/v1/code-generation/runs?page=1&pageSize=20",
                accessToken,
                item => item.Id == run.RunId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task AssertPageContainsAsync<T>(
        HttpClient client,
        string path,
        string accessToken,
        Func<T, bool> predicate,
        CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Get, path, accessToken);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                response,
                HttpStatusCode.OK,
                "Read Native AOT page " + path,
                cancellationToken)
            .ConfigureAwait(false);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<T>>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(page);
        Assert.IsTrue(page.Items.Any(predicate), "Expected Native AOT page row was not materialized.");
    }

    private static async Task VerifySerialNumbersFlowAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        const string rulesPath = "/api/v1/serial-numbers/rules";
        var ruleKey = "native_aot_" + Guid.NewGuid().ToString("N");
        var create = new CreateSerialNumberRuleRequest(
            ruleKey,
            "Native AOT serial rule",
            null,
            SerialNumberRuleScope.Host,
            SerialNumberResetInterval.Never,
            "N-{sequence:4}",
            1,
            9999,
            1,
            true);

        using var createRequest = AuthorizedJson(
            HttpMethod.Post,
            rulesPath,
            accessToken,
            create);
        using var createResponse = await client.SendAsync(
                createRequest,
                cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                createResponse,
                HttpStatusCode.Created,
                "Create Native AOT serial number rule",
                cancellationToken)
            .ConfigureAwait(false);
        var created = await createResponse.Content
            .ReadFromJsonAsync<SerialNumberRuleResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(created);
        Assert.AreEqual(ruleKey, created.RuleKey);

        using var getRequest = Authorized(
            HttpMethod.Get,
            $"{rulesPath}/{created.Id:D}",
            accessToken);
        using var getResponse = await client.SendAsync(getRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                getResponse,
                HttpStatusCode.OK,
                "Read Native AOT serial number rule",
                cancellationToken)
            .ConfigureAwait(false);
        var found = await getResponse.Content
            .ReadFromJsonAsync<SerialNumberRuleResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(created.Id, found?.Id);

        using var listRequest = Authorized(
            HttpMethod.Get,
            rulesPath + "?page=1&pageSize=20&key=" + Uri.EscapeDataString(ruleKey),
            accessToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                listResponse,
                HttpStatusCode.OK,
                "List Native AOT serial number rules",
                cancellationToken)
            .ConfigureAwait(false);
        var page = await listResponse.Content
            .ReadFromJsonAsync<PagedResult<SerialNumberRuleResponse>>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(page);
        Assert.IsTrue(page.Items.Any(item => item.Id == created.Id));

        using var previewRequest = AuthorizedJson(
            HttpMethod.Post,
            rulesPath + "/preview",
            accessToken,
            new PreviewSerialNumberRequest(
                SerialNumberRuleScope.Host,
                "N-{sequence:4}",
                null,
                7,
                new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero),
                SerialNumberResetInterval.Never));
        using var previewResponse = await client.SendAsync(
                previewRequest,
                cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                previewResponse,
                HttpStatusCode.OK,
                "Preview Native AOT serial number",
                cancellationToken)
            .ConfigureAwait(false);
        var preview = await previewResponse.Content
            .ReadFromJsonAsync<SerialNumberPreviewResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual("N-0007", preview?.Value);
    }

    private static async Task VerifyDocumentFlowAsync(
        HttpClient client,
        string accessToken,
        string? nativeLogFilePath,
        CancellationToken cancellationToken)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var categoryRequest = new CreateHostDocumentCategoryRequest(
            "Native AOT category " + suffix,
            null,
            1);
        var category = await PostAndReadAsync<
                CreateHostDocumentCategoryRequest,
                HostDocumentCategoryResponse>(
                client,
                "/api/v1/document/host/categories/",
                accessToken,
                categoryRequest,
                HttpStatusCode.Created,
                cancellationToken)
            .ConfigureAwait(false);
        using (var duplicateCategoryRequest = AuthorizedJson(
                   HttpMethod.Post,
                   "/api/v1/document/host/categories/",
                   accessToken,
                   categoryRequest))
        using (var duplicateCategoryResponse = await client
                   .SendAsync(duplicateCategoryRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    duplicateCategoryResponse,
                    HttpStatusCode.Conflict,
                    "Reject duplicate Native AOT document category",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        var tag = await PostAndReadAsync<
                CreateHostDocumentTagRequest,
                HostDocumentTagResponse>(
                client,
                "/api/v1/document/host/tags/",
                accessToken,
                new CreateHostDocumentTagRequest("Native AOT tag " + suffix),
                HttpStatusCode.Created,
                cancellationToken)
            .ConfigureAwait(false);
        var item = await PostAndReadAsync<
                CreateHostDocumentItemRequest,
                HostDocumentItemResponse>(
                client,
                "/api/v1/document/host/items/",
                accessToken,
                new CreateHostDocumentItemRequest(
                    "Native AOT document " + suffix,
                    "Native process closure",
                    HostDocumentType.Document,
                    HostDocumentStatus.Draft,
                    1,
                    null,
                    category.Id,
                    [tag.Id]),
                HttpStatusCode.Created,
                cancellationToken,
                nativeLogFilePath)
            .ConfigureAwait(false);

        using (var getRequest = Authorized(
                   HttpMethod.Get,
                   $"/api/v1/document/host/items/{item.Id:D}",
                   accessToken))
        using (var getResponse = await client.SendAsync(getRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    getResponse,
                    HttpStatusCode.OK,
                    "Read Native AOT document item",
                    cancellationToken)
                .ConfigureAwait(false);
            var found = await getResponse.Content
                .ReadFromJsonAsync<HostDocumentItemResponse>(cancellationToken)
                .ConfigureAwait(false);
            Assert.AreEqual(item.Id, found?.Id);
        }

        using (var listRequest = Authorized(
                   HttpMethod.Get,
                   "/api/v1/document/host/items/?page=1&pageSize=20",
                   accessToken))
        using (var listResponse = await client.SendAsync(listRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    listResponse,
                    HttpStatusCode.OK,
                    "List Native AOT document items",
                    cancellationToken)
                .ConfigureAwait(false);
            var page = await listResponse.Content
                .ReadFromJsonAsync<PagedResult<HostDocumentItemResponse>>(cancellationToken)
                .ConfigureAwait(false);
            Assert.IsNotNull(page);
            Assert.IsTrue(page.Items.Any(candidate => candidate.Id == item.Id));
        }

        var share = await PostAndReadAsync<
                CreateHostDocumentShareRequest,
                HostDocumentShareResponse>(
                client,
                "/api/v1/document/host/shares/",
                accessToken,
                new CreateHostDocumentShareRequest(item.Id, 1),
                HttpStatusCode.Created,
                cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(item.Id, share.DocumentId);

        var permissionUserId = Guid.NewGuid();
        var permissions = await PostAndReadAsync<
                SetHostDocumentPermissionsRequest,
                IReadOnlyList<HostDocumentPermissionResponse>>(
                client,
                "/api/v1/document/host/permissions/",
                accessToken,
                new SetHostDocumentPermissionsRequest(
                    item.Id,
                    [new HostDocumentPermissionEntry(permissionUserId, "read")]),
                HttpStatusCode.OK,
                cancellationToken)
            .ConfigureAwait(false);
        Assert.HasCount(1, permissions);
        Assert.AreEqual(item.Id, permissions[0].DocumentId);
        Assert.AreEqual(permissionUserId, permissions[0].UserId);
        Assert.AreEqual("read", permissions[0].PermissionLevel);

        using (var statisticsRequest = Authorized(
                   HttpMethod.Get,
                   "/api/v1/document/host/statistics/",
                   accessToken))
        using (var statisticsResponse = await client
                   .SendAsync(statisticsRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    statisticsResponse,
                    HttpStatusCode.OK,
                    "Read Native AOT document statistics",
                    cancellationToken)
                .ConfigureAwait(false);
            var statistics = await statisticsResponse.Content
                .ReadFromJsonAsync<HostDocumentStatisticsResponse>(cancellationToken)
                .ConfigureAwait(false);
            Assert.IsNotNull(statistics);
            Assert.IsTrue(statistics.Summary.TotalItems >= 1);
            Assert.IsTrue(statistics.ByCategory.Any(entry => entry.Count >= 1));
            Assert.IsTrue(statistics.ShareCount >= 1);
        }

        foreach (var path in new[]
                 {
                     "/api/v1/document/host/categories/",
                     "/api/v1/document/host/tags/",
                     "/api/v1/document/host/shares/?page=1&pageSize=20",
                     "/api/v1/document/host/recycle-bin/?page=1&pageSize=20",
                 })
        {
            using var request = Authorized(HttpMethod.Get, path, accessToken);
            using var response = await client.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            await AssertStatusAsync(
                    response,
                    HttpStatusCode.OK,
                    "Read Native AOT Document endpoint " + path,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task VerifyAuditingFlowAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var outboundProviderKey = "native-aot-" + Guid.NewGuid().ToString("N");
        using (var outboundProbeRequest = AuthorizedJson(
                   HttpMethod.Post,
                   "/api/v1/auditing/outbound-call-probes",
                   accessToken,
                   new OutboundCallAuditProbeRequest(
                       new OutboundCallAuditRequest(
                           outboundProviderKey,
                           "record",
                           "test",
                           204,
                           true,
                           1,
                           0))))
        using (var outboundProbeResponse = await client
                   .SendAsync(outboundProbeRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    outboundProbeResponse,
                    HttpStatusCode.NoContent,
                    "Write Native AOT outbound audit probe",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        using (var exceptionProbeRequest = Authorized(
                   HttpMethod.Post,
                   "/api/v1/auditing/exception-probes",
                   accessToken))
        using (var exceptionProbeResponse = await client
                   .SendAsync(exceptionProbeRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    exceptionProbeResponse,
                    HttpStatusCode.InternalServerError,
                    "Write Native AOT exception audit probe",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        using (var accessRequest = Authorized(
                   HttpMethod.Get,
                   "/api/v1/auditing/access-logs?page=1&pageSize=100",
                   accessToken))
        using (var accessResponse = await client.SendAsync(accessRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    accessResponse,
                    HttpStatusCode.OK,
                    "Read Native AOT access audit page",
                    cancellationToken)
                .ConfigureAwait(false);
            var page = await accessResponse.Content
                .ReadFromJsonAsync<PagedResult<AccessLogResponse>>(cancellationToken)
                .ConfigureAwait(false);
            Assert.IsNotNull(page);
        }
        _ = await WaitForAuditPageItemAsync<OperationLogResponse>(
                client,
                "/api/v1/auditing/operation-logs?page=1&pageSize=100",
                accessToken,
                static item => item.RequestPath.Contains("/api/v1/", StringComparison.Ordinal),
                cancellationToken)
            .ConfigureAwait(false);
        _ = await WaitForAuditPageItemAsync<ExceptionLogResponse>(
                client,
                "/api/v1/auditing/exception-logs?page=1&pageSize=100",
                accessToken,
                static item => item.RequestPath == "/api/v1/auditing/exception-probes",
                cancellationToken)
            .ConfigureAwait(false);
        _ = await WaitForAuditPageItemAsync<OutboundCallLogResponse>(
                client,
                "/api/v1/auditing/outbound-call-logs?page=1&pageSize=100",
                accessToken,
                item => item.ProviderKey == outboundProviderKey,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<T> WaitForAuditPageItemAsync<T>(
        HttpClient client,
        string path,
        string accessToken,
        Func<T, bool> predicate,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            using var request = Authorized(HttpMethod.Get, path, accessToken);
            using var response = await client.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            await AssertStatusAsync(
                    response,
                    HttpStatusCode.OK,
                    "Read Native AOT audit page " + path,
                    cancellationToken)
                .ConfigureAwait(false);
            var page = await response.Content.ReadFromJsonAsync<PagedResult<T>>(cancellationToken)
                .ConfigureAwait(false);
            Assert.IsNotNull(page);
            var match = page.Items.FirstOrDefault(predicate);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                .ConfigureAwait(false);
        }

        Assert.Fail("Timed out waiting for Native AOT audit row from " + path);
        throw new InvalidOperationException("Unreachable after Assert.Fail.");
    }

    private static async Task<TResponse> PostAndReadAsync<TRequest, TResponse>(
        HttpClient client,
        string path,
        string accessToken,
        TRequest body,
        HttpStatusCode expectedStatusCode,
        CancellationToken cancellationToken,
        string? nativeLogFilePath = null)
    {
        using var request = AuthorizedJson(HttpMethod.Post, path, accessToken, body);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                response,
                expectedStatusCode,
                "POST " + path,
                cancellationToken,
                nativeLogFilePath)
            .ConfigureAwait(false);
        var value = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(value);
        return value;
    }

    private static async Task VerifyReadinessAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/health/ready", cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task VerifyAiModuleNativeClosureAsync(
        HttpClient client,
        string accessToken,
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken)
    {
        using (var request = Authorized(HttpMethod.Get, "/api/v1/ai/agent-tools", accessToken))
        using (var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    response,
                    HttpStatusCode.OK,
                    "Read Native AOT AI agent tool catalog",
                    cancellationToken)
                .ConfigureAwait(false);
            var tools = await response.Content
                .ReadFromJsonAsync<IReadOnlyList<AiAgentToolCatalogItem>>(cancellationToken)
                .ConfigureAwait(false);
            Assert.IsNotNull(tools);
            Assert.IsTrue(tools.Any(tool => tool.ToolName == "ai.tools.ping"));
        }

        using var metadataResponse = await client
            .GetAsync("/.well-known/oauth-protected-resource/ai/mcp", cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                metadataResponse,
                HttpStatusCode.OK,
                "Read Native AOT MCP protected resource metadata",
                cancellationToken)
            .ConfigureAwait(false);
        using var metadata = JsonDocument.Parse(await metadataResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        Assert.AreEqual("fullnet://ai/mcp", metadata.RootElement.GetProperty("resource").GetString());

        // 原生产物实际执行目录写入、可空审批列物化及分页序列化，不以路由存在替代闭包验证。
        using var create = AuthorizedJson(HttpMethod.Post, "/api/v1/ai/knowledge-bases", accessToken, new CreateAiKnowledgeBaseRequest("原生目录", null));
        using var createdResponse = await client.SendAsync(create, cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(createdResponse, HttpStatusCode.Created, "Create native knowledge catalog", cancellationToken).ConfigureAwait(false);
        var created = (await createdResponse.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>(cancellationToken).ConfigureAwait(false))!;
        Assert.IsNull(created.EmbeddingModelConfigId);
        Assert.IsNull(created.GenerationModelVersion);
        using var modelCreate = AuthorizedJson(HttpMethod.Post, "/api/v1/ai/model-configs", accessToken,
            new CreateAiModelConfigRequest(null, "原生审批模型", "ollama", "https://provider.test", "model", null, null, false, true));
        using var modelResponse = await client.SendAsync(modelCreate, cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(modelResponse, HttpStatusCode.Created, "Create native approval model", cancellationToken).ConfigureAwait(false);
        var model = (await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>(cancellationToken).ConfigureAwait(false))!;
        using var update = AuthorizedJson(HttpMethod.Put, $"/api/v1/ai/knowledge-bases/{created.Id}/policy", accessToken,
            new UpdateAiKnowledgePolicyRequest("restricted", model.Id, model.Version, model.Id, model.Version, created.Version));
        using var updatedResponse = await client.SendAsync(update, cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(updatedResponse, HttpStatusCode.OK, "Update native knowledge policy", cancellationToken).ConfigureAwait(false);
        var updated = (await updatedResponse.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>(cancellationToken).ConfigureAwait(false))!;
        Assert.AreEqual("restricted", updated.DataClassification);
        Assert.AreEqual(created.Version + 1, updated.Version);
        Assert.AreEqual(model.Id, updated.EmbeddingModelConfigId);
        Assert.AreEqual(model.Version, updated.EmbeddingModelVersion);
        Assert.AreEqual(model.Id, updated.GenerationModelConfigId);
        Assert.AreEqual(model.Version, updated.GenerationModelVersion);
        await AssertPageContainsAsync<AiKnowledgeBaseResponse>(client, "/api/v1/ai/knowledge-bases?page=1&pageSize=20", accessToken,
            item => item.Id == created.Id, cancellationToken).ConfigureAwait(false);
        await VerifyKnowledgeMemberNativeAsync(client, accessToken, updated, provider, connectionString, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>原生实例真实授予只读权限和成员资格，撤权后复用同一令牌立即得到 404。</summary>
    private static async Task VerifyKnowledgeMemberNativeAsync(HttpClient client, string ownerToken,
        AiKnowledgeBaseResponse knowledgeBase, DatabaseProvider provider, string connectionString, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = await PostAndReadAsync<CreateHostUserRequest, HostUserResponse>(client, "/api/v1/identity/users", ownerToken,
            new CreateHostUserRequest($"naot-kb-{suffix}", "原生只读成员", AdminPassword), HttpStatusCode.Created, token).ConfigureAwait(false);
        var role = await PostAndReadAsync<CreateHostRoleRequest, HostRoleResponse>(client, "/api/v1/identity/roles", ownerToken,
            new CreateHostRoleRequest($"naot-kb-{suffix}", "原生知识库只读"), HttpStatusCode.Created, token).ConfigureAwait(false);
        using var permissions = AuthorizedJson(HttpMethod.Put, $"/api/v1/identity/roles/{role.Id}/permissions", ownerToken,
            new ReplaceHostRolePermissionsRequest([AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
                AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.VersionsRead,
                AiKnowledgePermissions.RoleCandidatesRead, AiKnowledgeDocumentPermissions.RoleCandidatesRead], role.Version));
        using var assignedPermissions = await client.SendAsync(permissions, token).ConfigureAwait(false);
        await AssertStatusAsync(assignedPermissions, HttpStatusCode.OK, "Assign native knowledge read permission", token).ConfigureAwait(false);
        using var rolesGet = Authorized(HttpMethod.Get, $"/api/v1/identity/users/{user.Id}/roles", ownerToken);
        using var rolesResponse = await client.SendAsync(rolesGet, token).ConfigureAwait(false);
        await AssertStatusAsync(rolesResponse, HttpStatusCode.OK, "Read native member roles", token).ConfigureAwait(false);
        var roles = (await rolesResponse.Content.ReadFromJsonAsync<HostUserRolesResponse>(token).ConfigureAwait(false))!;
        using var rolesSet = AuthorizedJson(HttpMethod.Put, $"/api/v1/identity/users/{user.Id}/roles", ownerToken,
            new ReplaceHostUserRolesRequest([role.Id], roles.Version));
        using var rolesAssigned = await client.SendAsync(rolesSet, token).ConfigureAwait(false);
        await AssertStatusAsync(rolesAssigned, HttpStatusCode.OK, "Assign native member role", token).ConfigureAwait(false);
        var path = $"/api/v1/ai/knowledge-bases/{knowledgeBase.Id}";
        await AssertPageContainsAsync<AiKnowledgeRoleCandidateResponse>(client, path + "/role-candidates", ownerToken,
            item => item.Id == role.Id, token).ConfigureAwait(false);
        using var grant = AuthorizedJson(HttpMethod.Put, path + "/members", ownerToken,
            new SetAiKnowledgeMembersRequest([user.Id], knowledgeBase.Version));
        using var granted = await client.SendAsync(grant, token).ConfigureAwait(false);
        await AssertStatusAsync(granted, HttpStatusCode.OK, "Grant native host knowledge member", token).ConfigureAwait(false);
        var members = (await granted.Content.ReadFromJsonAsync<AiKnowledgeMembersResponse>(token).ConfigureAwait(false))!;
        CollectionAssert.AreEqual(new[] { user.Id }, members.UserIds.ToArray());
        // API 创建的账户必须先完成首次改密；复用含 Cookie/CSRF 校验的真实登录流程。
        var memberToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client, user.Username, AdminPassword, token).ConfigureAwait(false);
        await VerifyKnowledgeRoleNativeAsync(client, ownerToken, memberToken, user.Id, provider, connectionString, token).ConfigureAwait(false);
        using var read = Authorized(HttpMethod.Get, path, memberToken);
        using var readable = await client.SendAsync(read, token).ConfigureAwait(false);
        await AssertStatusAsync(readable, HttpStatusCode.OK, "Read native granted knowledge catalog", token).ConfigureAwait(false);
        var document = await VerifyKnowledgeDocumentNativeAsync(client, ownerToken, memberToken, user.Id, path, token).ConfigureAwait(false);
        await AssertPageContainsAsync<AiKnowledgeRoleCandidateResponse>(client, path + $"/documents/{document.Id}/role-candidates", ownerToken,
            item => item.Id == role.Id, token).ConfigureAwait(false);
        // 原生实例中旧令牌也必须在权威权限撤销后拒绝候选查询，不能仅靠 JWT 策略。
        using var candidateBefore = Authorized(HttpMethod.Get, path + "/role-candidates", memberToken);
        using var candidateBeforeResponse = await client.SendAsync(candidateBefore, token).ConfigureAwait(false);
        await AssertStatusAsync(candidateBeforeResponse, HttpStatusCode.NotFound, "Member cannot enumerate native role candidates", token).ConfigureAwait(false);
        // 正式权限 API 会撤销整个会话；此夹具只改权限，确保 403 来自权威权限复核而不是会话 401。
        await NativeAiRoleCandidateSeed.RevokeCandidatePermissionsAsync(provider, connectionString, role.Id, token).ConfigureAwait(false);
        foreach (var candidatePath in new[] { path + "/role-candidates", path + $"/documents/{document.Id}/role-candidates" })
        {
            using var candidateAfter = Authorized(HttpMethod.Get, candidatePath, memberToken);
            using var candidateAfterResponse = await client.SendAsync(candidateAfter, token).ConfigureAwait(false);
            await AssertStatusAsync(candidateAfterResponse, HttpStatusCode.Forbidden, "Old native token cannot retain candidate permissions", token).ConfigureAwait(false);
        }
        using var revoke = AuthorizedJson(HttpMethod.Put, path + "/members", ownerToken,
            new SetAiKnowledgeMembersRequest([], members.Version));
        using var revoked = await client.SendAsync(revoke, token).ConfigureAwait(false);
        await AssertStatusAsync(revoked, HttpStatusCode.OK, "Revoke native host knowledge member", token).ConfigureAwait(false);
        using var denied = Authorized(HttpMethod.Get, path, memberToken);
        using var unreadable = await client.SendAsync(denied, token).ConfigureAwait(false);
        await AssertStatusAsync(unreadable, HttpStatusCode.NotFound, "Immediately deny revoked native member", token).ConfigureAwait(false);
        using var documentDenied = Authorized(HttpMethod.Get, path + $"/documents/{document.Id}", memberToken);
        using var documentUnreadable = await client.SendAsync(documentDenied, token).ConfigureAwait(false);
        await AssertStatusAsync(documentUnreadable, HttpStatusCode.NotFound, "Immediately deny native document after knowledge revoke", token).ConfigureAwait(false);
        using var documentDelete = AuthorizedJson(HttpMethod.Delete, path + $"/documents/{document.Id}", ownerToken,
            new DeleteAiKnowledgeDocumentRequest(document.Version));
        using var documentDeleted = await client.SendAsync(documentDelete, token).ConfigureAwait(false);
        await AssertStatusAsync(documentDeleted, HttpStatusCode.NoContent, "Delete native document authority", token).ConfigureAwait(false);
        using var deletedRead = Authorized(HttpMethod.Get, path + $"/documents/{document.Id}", ownerToken);
        using var deletedResponse = await client.SendAsync(deletedRead, token).ConfigureAwait(false);
        await AssertStatusAsync(deletedResponse, HttpStatusCode.NotFound, "Deny native deleted document metadata", token).ConfigureAwait(false);
    }

    // 原生探针执行文件版本、双层授权、分页、版本更新和独立撤权。
    private static async Task VerifyKnowledgeRoleNativeAsync(HttpClient client, string ownerToken, string memberToken, Guid userId,
        DatabaseProvider provider, string connectionString, CancellationToken token)
    {
        var knowledge = await PostAndReadAsync<CreateAiKnowledgeBaseRequest, AiKnowledgeBaseResponse>(client, "/api/v1/ai/knowledge-bases", ownerToken,
            new CreateAiKnowledgeBaseRequest("原生角色授予", null), HttpStatusCode.Created, token).ConfigureAwait(false);
        var path = $"/api/v1/ai/knowledge-bases/{knowledge.Id}";
        var document = await PostAndReadAsync<CreateAiKnowledgeDocumentRequest, AiKnowledgeDocumentResponse>(client, path + "/documents", ownerToken,
            new CreateAiKnowledgeDocumentRequest("原生角色文档", null), HttpStatusCode.Created, token).ConfigureAwait(false);
        var docPath = path + $"/documents/{document.Id}";
        var role = await NativeAiRoleCandidateSeed.InsertAsync(provider, connectionString, null, token).ConfigureAwait(false);
        await NativeAiRoleCandidateSeed.AssignAsync(provider, connectionString, userId, role, token).ConfigureAwait(false);
        foreach (var resource in new[] { path, docPath })
        {
            using var update = AuthorizedJson(HttpMethod.Put, resource + "/roles", ownerToken, new SetAiKnowledgeRoleGrantsRequest([role], 1));
            using var response = await client.SendAsync(update, token).ConfigureAwait(false);
            await AssertStatusAsync(response, HttpStatusCode.OK, "Grant native role", token).ConfigureAwait(false);
            var grants = (await response.Content.ReadFromJsonAsync<AiKnowledgeRoleGrantsResponse>(token).ConfigureAwait(false))!;
            CollectionAssert.AreEqual(new[] { role }, grants.RoleIds.ToArray());
            Assert.AreEqual(2, grants.Version);
            using var read = Authorized(HttpMethod.Get, resource + "/roles", ownerToken);
            using var readResponse = await client.SendAsync(read, token).ConfigureAwait(false);
            await AssertStatusAsync(readResponse, HttpStatusCode.OK, "Read native role grants", token).ConfigureAwait(false);
        }
        await AssertPageContainsAsync<AiKnowledgeDocumentResponse>(client, path + "/documents", memberToken,
            item => item.Id == document.Id, token).ConfigureAwait(false);
        var upload = await VerifyKnowledgeFileNativeAsync(client, ownerToken, docPath, 2, token).ConfigureAwait(false);
        var source = docPath + $"/versions/{upload.VersionId}/source";
        using var key = AuthorizedJson(HttpMethod.Post, "/api/v1/identity/api-keys", ownerToken,
            new CreateHostApiKeyRequest(userId, "原生知识库读取", [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead], null));
        using var keyResponse = await client.SendAsync(key, token).ConfigureAwait(false);
        await AssertStatusAsync(keyResponse, HttpStatusCode.Created, "Create native knowledge API key", token).ConfigureAwait(false);
        var credential = (await keyResponse.Content.ReadFromJsonAsync<CreateHostApiKeyResponse>(token).ConfigureAwait(false))!;
        foreach (var credentialToken in new[] { memberToken, credential.Secret })
        {
            using var read = Authorized(HttpMethod.Get, source, credentialToken);
            if (credentialToken == credential.Secret) read.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", credential.Secret);
            using var response = await client.SendAsync(read, token).ConfigureAwait(false);
            await AssertStatusAsync(response, HttpStatusCode.OK, "Read native source through authoritative role and credential", token).ConfigureAwait(false);
        }
        await NativeAiRoleCandidateSeed.DisableAsync(provider, connectionString, role, token).ConfigureAwait(false);
        using var denied = Authorized(HttpMethod.Get, source, memberToken);
        using var deniedResponse = await client.SendAsync(denied, token).ConfigureAwait(false);
        await AssertStatusAsync(deniedResponse, HttpStatusCode.NotFound, "Old native token cannot retain disabled role", token).ConfigureAwait(false);
    }

    // 原生探针执行文件版本、双层授权、分页、版本更新和独立撤权。
    private static async Task<AiKnowledgeDocumentResponse> VerifyKnowledgeDocumentNativeAsync(HttpClient client,
        string ownerToken, string memberToken, Guid userId, string basePath, CancellationToken token)
    {
        var path = basePath + "/documents";
        var document = await PostAndReadAsync<CreateAiKnowledgeDocumentRequest, AiKnowledgeDocumentResponse>(client, path, ownerToken,
            new CreateAiKnowledgeDocumentRequest("原生文档草稿", "受保护的元数据"), HttpStatusCode.Created, token).ConfigureAwait(false);
        Assert.AreEqual("draft", document.Status);
        var docPath = path + $"/{document.Id}";
        using var denied = Authorized(HttpMethod.Get, docPath, memberToken);
        using var deniedResponse = await client.SendAsync(denied, token).ConfigureAwait(false);
        await AssertStatusAsync(deniedResponse, HttpStatusCode.NotFound, "Knowledge membership does not grant native document metadata", token).ConfigureAwait(false);
        using var grant = AuthorizedJson(HttpMethod.Put, docPath + "/members", ownerToken,
            new SetAiKnowledgeDocumentMembersRequest([userId], document.Version));
        using var granted = await client.SendAsync(grant, token).ConfigureAwait(false);
        await AssertStatusAsync(granted, HttpStatusCode.OK, "Grant native document access", token).ConfigureAwait(false);
        var members = (await granted.Content.ReadFromJsonAsync<AiKnowledgeDocumentMembersResponse>(token).ConfigureAwait(false))!;
        CollectionAssert.AreEqual(new[] { userId }, members.UserIds.ToArray());
        var imported = await VerifyKnowledgeFileNativeAsync(client, ownerToken, docPath, members.Version, token).ConfigureAwait(false);
        using var memberSource = Authorized(HttpMethod.Get, docPath + $"/versions/{imported.VersionId}/source", memberToken);
        using var memberSourceResponse = await client.SendAsync(memberSource, token).ConfigureAwait(false);
        await AssertStatusAsync(memberSourceResponse, HttpStatusCode.OK, "Read native explicitly authorized source", token).ConfigureAwait(false);
        using var update = AuthorizedJson(HttpMethod.Put, docPath, ownerToken,
            new UpdateAiKnowledgeDocumentRequest("原生更新标题", null, members.Version + 1));
        using var updated = await client.SendAsync(update, token).ConfigureAwait(false);
        await AssertStatusAsync(updated, HttpStatusCode.OK, "Update native document metadata", token).ConfigureAwait(false);
        document = (await updated.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>(token).ConfigureAwait(false))!;
        await AssertPageContainsAsync<AiKnowledgeDocumentResponse>(client, path, memberToken,
            item => item.Id == document.Id && item.Title == "原生更新标题", token).ConfigureAwait(false);
        using var membersRead = Authorized(HttpMethod.Get, docPath + "/members", ownerToken);
        using var membersResponse = await client.SendAsync(membersRead, token).ConfigureAwait(false);
        await AssertStatusAsync(membersResponse, HttpStatusCode.OK, "Read native document members", token).ConfigureAwait(false);
        using var revoke = AuthorizedJson(HttpMethod.Put, docPath + "/members", ownerToken,
            new SetAiKnowledgeDocumentMembersRequest([], document.Version));
        using var revoked = await client.SendAsync(revoke, token).ConfigureAwait(false);
        await AssertStatusAsync(revoked, HttpStatusCode.OK, "Revoke native document access", token).ConfigureAwait(false);
        members = (await revoked.Content.ReadFromJsonAsync<AiKnowledgeDocumentMembersResponse>(token).ConfigureAwait(false))!;
        using var unreadable = Authorized(HttpMethod.Get, docPath, memberToken);
        using var unreadableResponse = await client.SendAsync(unreadable, token).ConfigureAwait(false);
        await AssertStatusAsync(unreadableResponse, HttpStatusCode.NotFound, "Immediately deny native revoked document", token).ConfigureAwait(false);
        using var revokedSource = Authorized(HttpMethod.Get, docPath + $"/versions/{imported.VersionId}/source", memberToken);
        using var revokedSourceResponse = await client.SendAsync(revokedSource, token).ConfigureAwait(false);
        await AssertStatusAsync(revokedSourceResponse, HttpStatusCode.NotFound, "Immediately deny native revoked source", token).ConfigureAwait(false);
        using var regrant = AuthorizedJson(HttpMethod.Put, docPath + "/members", ownerToken,
            new SetAiKnowledgeDocumentMembersRequest([userId], members.Version));
        using var regranted = await client.SendAsync(regrant, token).ConfigureAwait(false);
        await AssertStatusAsync(regranted, HttpStatusCode.OK, "Restore explicit native document access", token).ConfigureAwait(false);
        using var current = Authorized(HttpMethod.Get, docPath, ownerToken);
        using var currentResponse = await client.SendAsync(current, token).ConfigureAwait(false);
        await AssertStatusAsync(currentResponse, HttpStatusCode.OK, "Read native document authority", token).ConfigureAwait(false);
        return (await currentResponse.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>(token).ConfigureAwait(false))!;
    }

    private static async Task<AiKnowledgeImportResponse> VerifyKnowledgeFileNativeAsync(HttpClient client, string accessToken,
        string docPath, int documentVersion, CancellationToken token)
    {
        var requestKey = Guid.NewGuid();
        using var upload = Authorized(HttpMethod.Post, docPath + $"/versions?version={documentVersion}&requestKey={requestKey}&fileName=native.txt", accessToken);
        upload.Content = new StringContent("原生来源正文", Encoding.UTF8, "text/plain");
        using var uploaded = await client.SendAsync(upload, token).ConfigureAwait(false);
        await AssertStatusAsync(uploaded, HttpStatusCode.Accepted, "Upload native knowledge source", token).ConfigureAwait(false);
        var operation = (await uploaded.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>(token).ConfigureAwait(false))!;
        Assert.AreEqual("completed", operation.State);
        await AssertPageContainsAsync<AiKnowledgeVersionResponse>(client, docPath + "/versions", accessToken,
            item => item.Id == operation.VersionId && item.ProcessingStatus == "pending_processing", token).ConfigureAwait(false);
        using var retry = Authorized(HttpMethod.Post, docPath + $"/imports/{operation.Id}/retry", accessToken);
        using var retried = await client.SendAsync(retry, token).ConfigureAwait(false);
        await AssertStatusAsync(retried, HttpStatusCode.OK, "Read native durable import recovery", token).ConfigureAwait(false);
        Assert.AreEqual(operation.Id, (await retried.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>(token).ConfigureAwait(false))!.Id);
        using var source = Authorized(HttpMethod.Get, docPath + $"/versions/{operation.VersionId}/source", accessToken);
        using var downloaded = await client.SendAsync(source, token).ConfigureAwait(false);
        await AssertStatusAsync(downloaded, HttpStatusCode.OK, "Download native knowledge source", token).ConfigureAwait(false);
        Assert.AreEqual("原生来源正文", await downloaded.Content.ReadAsStringAsync(token).ConfigureAwait(false));
        Assert.IsTrue(downloaded.Headers.CacheControl!.NoStore);
        return operation;
    }

    private static async Task VerifyMessagingDeadLetterFlowAsync(
        DatabaseProvider provider,
        string connectionString,
        HttpClient client,
        string accessToken,
        string? nativeLogFilePath,
        CancellationToken cancellationToken)
    {
        const string consumerName = "fullnet.native_aot.unregistered.consumer";
        const string messageType = "fullnet.native_aot.messaging.materializer_probe";
        var messageId = Guid.CreateVersion7();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { Probe = messageId });
        await SeedFailedDeadLetterAsync(
                provider,
                connectionString,
                consumerName,
                messageId,
                messageType,
                payload,
                cancellationToken)
            .ConfigureAwait(false);

        await AssertPageContainsAsync<DeadLetterResponse>(
                client,
                "/api/v1/messaging/dead-letters?page=1&pageSize=20&consumerName="
                    + Uri.EscapeDataString(consumerName),
                accessToken,
                item => item.ConsumerName == consumerName && item.MessageId == messageId,
                cancellationToken)
            .ConfigureAwait(false);

        using var replayRequest = AuthorizedJson(
            HttpMethod.Post,
            "/api/v1/messaging/dead-letters/replay",
            accessToken,
            new ReplayDeadLetterRequest(consumerName, messageId));
        using var replayResponse = await client.SendAsync(replayRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                replayResponse,
                HttpStatusCode.UnprocessableEntity,
                "Replay Native AOT dead letter through the Outbox envelope materializer",
                cancellationToken,
                nativeLogFilePath)
            .ConfigureAwait(false);
        using var problem = JsonDocument.Parse(
            await replayResponse.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false));
        Assert.AreEqual(
            MessagingErrorCodes.SubscriptionRouteNotFound,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyMessagingDeliveryStatusAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = Authorized(
            HttpMethod.Get,
            "/api/v1/messaging/delivery-status",
            accessToken);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                response,
                HttpStatusCode.OK,
                "GET /api/v1/messaging/delivery-status",
                cancellationToken)
            .ConfigureAwait(false);

        var status = await response.Content
            .ReadFromJsonAsync<DeliveryStatusResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(status);
        Assert.IsNotNull(status.Backlog);
        Assert.IsGreaterThanOrEqualTo(0, status.Backlog.PendingCount);
        Assert.IsGreaterThanOrEqualTo(0, status.Backlog.DueRetryCount);
        Assert.IsGreaterThanOrEqualTo(0, status.Backlog.ActiveLeaseCount);
        Assert.IsGreaterThanOrEqualTo(0, status.Backlog.DeadLetterCount);
        Assert.IsFalse(
            status.Streams is null || status.Streams.Count == 0,
            "Native AOT delivery-status 必须返回已注册事件流，不能用空列表冒充积压物化。");
        Assert.IsFalse(
            string.IsNullOrWhiteSpace(status.Streams[0].EventType),
            "Native AOT delivery-status 事件流必须包含 EventType。");
    }

    private static async Task SeedFailedDeadLetterAsync(
        DatabaseProvider provider,
        string connectionString,
        string consumerName,
        Guid messageId,
        string messageType,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var parameters = new
        {
            ConsumerName = consumerName,
            MessageId = messageId,
            MessageType = messageType,
            SchemaVersion = 1,
            ContentType = "application/json",
            PartitionKey = $"native-aot-{messageId:N}",
            Producer = "fullnet.native_aot.tests",
            Payload = payload,
            PayloadHash = SHA256.HashData(payload),
            LastErrorCode = "messaging.native_aot.probe",
            LastError = "Injected dead letter for Native AOT materializer verification.",
        };
        var command = provider switch
        {
            DatabaseProvider.SqlServer => new CommandDefinition(
                """
                INSERT INTO dbo.fn_messaging_outbox_event
                    (Id, MessageType, SchemaVersion, ContentType, TenantId, PartitionKey,
                     CorrelationId, CausationId, TraceParent, Producer, Payload, OccurredAtUtc)
                VALUES
                    (@MessageId, @MessageType, @SchemaVersion, @ContentType, NULL, @PartitionKey,
                     NULL, NULL, NULL, @Producer, @Payload, SYSDATETIMEOFFSET());

                INSERT INTO dbo.fn_messaging_inbox_message
                    (ConsumerName, MessageId, MessageType, SchemaVersion, TenantId, PayloadHash,
                     Status, Attempts, ReceivedAtUtc, ProcessedAtUtc, LastErrorCode, LastError)
                VALUES
                    (@ConsumerName, @MessageId, @MessageType, @SchemaVersion, NULL, @PayloadHash,
                     'failed', 1, SYSDATETIMEOFFSET(), NULL, @LastErrorCode, @LastError);
                """,
                parameters,
                cancellationToken: cancellationToken),
            DatabaseProvider.MySql => new CommandDefinition(
                """
                INSERT INTO fn_messaging_outbox_event
                    (Id, MessageType, SchemaVersion, ContentType, TenantId, PartitionKey,
                     CorrelationId, CausationId, TraceParent, Producer, Payload, OccurredAtUtc)
                VALUES
                    (@MessageId, @MessageType, @SchemaVersion, @ContentType, NULL, @PartitionKey,
                     NULL, NULL, NULL, @Producer, @Payload, UTC_TIMESTAMP(6));

                INSERT INTO fn_messaging_inbox_message
                    (ConsumerName, MessageId, MessageType, SchemaVersion, TenantId, PayloadHash,
                     Status, Attempts, ReceivedAtUtc, ProcessedAtUtc, LastErrorCode, LastError)
                VALUES
                    (@ConsumerName, @MessageId, @MessageType, @SchemaVersion, NULL, @PayloadHash,
                     'failed', 1, UTC_TIMESTAMP(6), NULL, @LastErrorCode, @LastError);
                """,
                parameters,
                cancellationToken: cancellationToken),
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{provider}'."),
        };

        if (provider == DatabaseProvider.SqlServer)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.ExecuteAsync(command).ConfigureAwait(false);
            return;
        }

        await using var mySqlConnection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await mySqlConnection.ExecuteAsync(command).ConfigureAwait(false);
    }

    private static string ReadNativeLogTail(string? logFilePath, int maxChars = 4_000)
    {
        if (string.IsNullOrEmpty(logFilePath) || !File.Exists(logFilePath))
        {
            return string.Empty;
        }

        var content = File.ReadAllText(logFilePath);
        if (content.Length <= maxChars)
        {
            return content;
        }

        return content[^maxChars..];
    }

    private static HttpRequestMessage Authorized(
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

    private static HttpRequestMessage AuthorizedJson<T>(
        HttpMethod method,
        string path,
        string accessToken,
        T body)
    {
        var request = Authorized(method, path, accessToken);
        request.Content = JsonContent.Create(body);
        return request;
    }

    internal static async Task AssertStatusAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatusCode,
        string operation,
        CancellationToken cancellationToken = default,
        string? nativeLogFilePath = null)
    {
        if (response.StatusCode == expectedStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        var logTail = ReadNativeLogTail(nativeLogFilePath);
        Assert.Fail(
            $"{operation} failed. Expected {expectedStatusCode}, actual {response.StatusCode}. "
            + $"Response body: {body}"
            + (string.IsNullOrEmpty(logTail)
                ? string.Empty
                : $"\nNative log tail:\n{logTail}"));
    }
}
