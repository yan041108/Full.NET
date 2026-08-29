using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>
/// Host 用户管理纵向切片验收夹具；端点未实现时应保持失败（RED）。
/// </summary>
internal static class IdentityUserManagementAssertions
{
    public static async Task VerifyHostUserManagementContractAsync(
        Api.FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(
            factory,
            client,
            cancellationToken);
        await VerifyCreateRejectsDuplicateUsernameAsync(
            factory,
            client,
            cancellationToken);
        await VerifyAuthoritativeProfileValidationAndUniquenessAsync(
            client,
            cancellationToken);
        await VerifyDisabledUserCannotLoginAsync(
            factory,
            client,
            cancellationToken);
        await VerifyCannotDisableLastRemainingSuperAdministratorAsync(
            client,
            cancellationToken);
        await VerifyUpdateDisplayNameWithOptimisticVersionAsync(
            client,
            cancellationToken);
        await VerifyResetPasswordInvalidatesOldCredentialsAsync(
            client,
            cancellationToken);
        await VerifyEnableUserRestoresLoginAsync(
            client,
            cancellationToken);
        await VerifyExactActionPermissionBoundariesAsync(
            factory,
            client,
            cancellationToken);
        await OpenApiHostUsersContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyAuthoritativeProfileValidationAndUniquenessAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var rejectedUsername = $"profile-invalid-{Guid.NewGuid():N}";
        using (var invalidRequest = CreateBearerJsonRequest(
                   HttpMethod.Post,
                   "/api/v1/identity/users",
                   adminToken,
                   new CreateHostUserRequest(
                       rejectedUsername,
                       "资料格式无效",
                       Api.FullNetApiFactory.TestPassword,
                       Profile: CreateProfile(phoneNumber: "+138-0000-0000"))))
        using (var invalidResponse = await client.SendAsync(invalidRequest, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
            using var problem = JsonDocument.Parse(
                await invalidResponse.Content.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual(
                IdentityErrorCodes.UserProfileInvalid,
                problem.RootElement.GetProperty("code").GetString());
        }

        var phoneNumber = $"139{Random.Shared.NextInt64(10_000_000, 99_999_999)}";
        using var firstRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(
                $"profile-owner-{Guid.NewGuid():N}",
                "资料所有者",
                Api.FullNetApiFactory.TestPassword,
                Profile: CreateProfile(phoneNumber: phoneNumber)));
        using var firstResponse = await client.SendAsync(firstRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);

        using var duplicateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(
                rejectedUsername,
                "重复资料用户",
                Api.FullNetApiFactory.TestPassword,
                Profile: CreateProfile(phoneNumber: phoneNumber)));
        using var duplicateResponse = await client.SendAsync(
            duplicateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        using (var problem = JsonDocument.Parse(
                   await duplicateResponse.Content.ReadAsStringAsync(cancellationToken)))
        {
            Assert.AreEqual(
                IdentityErrorCodes.UserPhoneNumberExists,
                problem.RootElement.GetProperty("code").GetString());
        }

        using var recoveredRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(
                rejectedUsername,
                "失败事务已回滚",
                Api.FullNetApiFactory.TestPassword));
        using var recoveredResponse = await client.SendAsync(recoveredRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, recoveredResponse.StatusCode);

        var uniqueSuffix = Guid.NewGuid().ToString("N");
        var authoritativeOwner = await CreateHostUserWithProfileAsync(
            client,
            adminToken,
            $"authority-owner-{uniqueSuffix}",
            "权威资料字段所有者",
            CreateProfile(
                email: $"owner-{uniqueSuffix}@example.com",
                employeeNumber: $"EMP-{uniqueSuffix[..12]}",
                idCardType: "passport",
                idCardNumber: $"P-{uniqueSuffix[..12]}"),
            cancellationToken);
        await AssertProfileConflictAsync(
            client,
            adminToken,
            CreateProfile(email: authoritativeOwner.Profile!.Email),
            IdentityErrorCodes.UserEmailExists,
            cancellationToken);
        await AssertProfileConflictAsync(
            client,
            adminToken,
            CreateProfile(employeeNumber: authoritativeOwner.Profile.EmployeeNumber),
            IdentityErrorCodes.UserEmployeeNumberExists,
            cancellationToken);
        await AssertProfileConflictAsync(
            client,
            adminToken,
            CreateProfile(
                idCardType: authoritativeOwner.Profile.IdCardType,
                idCardNumber: authoritativeOwner.Profile.IdCardNumber),
            IdentityErrorCodes.UserIdCardExists,
            cancellationToken);

        var racedPhone = $"137{Random.Shared.NextInt64(10_000_000, 99_999_999)}";
        using var racedRequestA = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(
                $"profile-race-a-{Guid.NewGuid():N}",
                "并发资料 A",
                Api.FullNetApiFactory.TestPassword,
                Profile: CreateProfile(phoneNumber: racedPhone)));
        using var racedRequestB = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(
                $"profile-race-b-{Guid.NewGuid():N}",
                "并发资料 B",
                Api.FullNetApiFactory.TestPassword,
                Profile: CreateProfile(phoneNumber: racedPhone)));
        var racedResponses = await Task.WhenAll(
            client.SendAsync(racedRequestA, cancellationToken),
            client.SendAsync(racedRequestB, cancellationToken));
        using (racedResponses[0])
        using (racedResponses[1])
        {
            CollectionAssert.AreEquivalent(
                new[] { HttpStatusCode.Created, HttpStatusCode.Conflict },
                racedResponses.Select(response => response.StatusCode).ToArray());
            var conflictResponse = racedResponses.Single(
                response => response.StatusCode == HttpStatusCode.Conflict);
            using var problem = JsonDocument.Parse(
                await conflictResponse.Content.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual(
                IdentityErrorCodes.UserPhoneNumberExists,
                problem.RootElement.GetProperty("code").GetString());
        }

        var updateOwnerA = await CreateHostUserWithProfileAsync(
            client,
            adminToken,
            $"profile-update-a-{Guid.NewGuid():N}",
            "更新竞态原值 A",
            CreateProfile(email: $"update-a-{Guid.NewGuid():N}@example.com"),
            cancellationToken);
        var updateOwnerB = await CreateHostUserWithProfileAsync(
            client,
            adminToken,
            $"profile-update-b-{Guid.NewGuid():N}",
            "更新竞态原值 B",
            CreateProfile(email: $"update-b-{Guid.NewGuid():N}@example.com"),
            cancellationToken);
        var updateOwners = new[] { updateOwnerA, updateOwnerB };
        var racedEmail = $"update-race-{Guid.NewGuid():N}@example.com";
        using var updateRequestA = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/users/{updateOwnerA.Id:D}",
            adminToken,
            new UpdateHostUserRequest(
                "更新竞态新值 A",
                updateOwnerA.Version,
                Profile: CreateProfile(email: racedEmail) with
                {
                    Version = updateOwnerA.Profile!.Version,
                }));
        using var updateRequestB = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/users/{updateOwnerB.Id:D}",
            adminToken,
            new UpdateHostUserRequest(
                "更新竞态新值 B",
                updateOwnerB.Version,
                Profile: CreateProfile(email: racedEmail) with
                {
                    Version = updateOwnerB.Profile!.Version,
                }));
        var updateResponses = await Task.WhenAll(
            client.SendAsync(updateRequestA, cancellationToken),
            client.SendAsync(updateRequestB, cancellationToken));
        var losingIndex = Array.FindIndex(
            updateResponses,
            response => response.StatusCode == HttpStatusCode.Conflict);
        using (updateResponses[0])
        using (updateResponses[1])
        {
            CollectionAssert.AreEquivalent(
                new[] { HttpStatusCode.OK, HttpStatusCode.Conflict },
                updateResponses.Select(response => response.StatusCode).ToArray());
            Assert.IsTrue(losingIndex >= 0);
        }

        var loser = updateOwners[losingIndex];
        using var loserRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/users/{loser.Id:D}");
        loserRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var loserResponse = await client.SendAsync(loserRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loserResponse.StatusCode);
        var loserAfterRace = await loserResponse.Content
            .ReadFromJsonAsync<HostUserResponse>(cancellationToken);
        Assert.IsNotNull(loserAfterRace);
        Assert.AreEqual(loser.DisplayName, loserAfterRace.DisplayName);
        Assert.AreEqual(loser.Version, loserAfterRace.Version);
        Assert.AreEqual(loser.Profile!.Email, loserAfterRace.Profile!.Email);
        Assert.AreEqual(loser.Profile.Version, loserAfterRace.Profile.Version);
    }

    private static HostUserProfileWriteRequest CreateProfile(
        string? phoneNumber = null,
        string? email = null,
        string? employeeNumber = null,
        string? idCardType = null,
        string? idCardNumber = null) =>
        new(
            FieldKeys: new[]
            {
                phoneNumber is null ? null : "phone_number",
                email is null ? null : "email",
                employeeNumber is null ? null : "employee_number",
                idCardType is null ? null : "id_card_type",
                idCardNumber is null ? null : "id_card_number",
            }.Where(fieldKey => fieldKey is not null).Cast<string>().ToArray(),
            Nickname: null,
            PhoneNumber: phoneNumber,
            Email: email,
            EmployeeNumber: employeeNumber,
            Gender: null,
            JoinDateUtc: null,
            SortOrder: null,
            IdCardType: idCardType,
            IdCardNumber: idCardNumber,
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

    private static async Task<HostUserResponse> CreateHostUserWithProfileAsync(
        HttpClient client,
        string accessToken,
        string username,
        string displayName,
        HostUserProfileWriteRequest profile,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            accessToken,
            new CreateHostUserRequest(
                username,
                displayName,
                Api.FullNetApiFactory.TestPassword,
                Profile: profile));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<HostUserResponse>(cancellationToken))!;
    }

    private static async Task AssertProfileConflictAsync(
        HttpClient client,
        string accessToken,
        HostUserProfileWriteRequest profile,
        string expectedCode,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            accessToken,
            new CreateHostUserRequest(
                $"profile-conflict-{Guid.NewGuid():N}",
                "资料冲突",
                Api.FullNetApiFactory.TestPassword,
                Profile: profile));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(expectedCode, problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyListRequiresReadPermissionAsync(
        Api.FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await factory.CreateHostAccessTokenAsync(
                ["platform.dashboard.read"],
                cancellationToken));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyCreateRejectsDuplicateUsernameAsync(
        Api.FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var username = $"host-user-{Guid.NewGuid():N}";
        var body = new CreateHostUserRequest(
            username,
            "集成测试用户",
            Api.FullNetApiFactory.TestPassword);

        using var firstRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            body);
        using var firstResponse = await client.SendAsync(firstRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);
        var created = await firstResponse.Content.ReadFromJsonAsync<HostUserResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(username, created.Username);

        using var duplicateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            body);
        using var duplicateResponse = await client.SendAsync(
            duplicateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await duplicateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            IdentityErrorCodes.UsernameExists,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyDisabledUserCannotLoginAsync(
        Api.FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var username = $"disabled-{Guid.NewGuid():N}";
        var password = Api.FullNetApiFactory.TestPassword;

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(username, "待禁用用户", password));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HostUserResponse>(
            cancellationToken);
        Assert.IsNotNull(created);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/users/{created.Id:D}/disable",
            adminToken,
            new { });
        using var disableResponse = await client.SendAsync(
            disableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);

        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, password)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await loginResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            IdentityErrorCodes.InvalidCredentials,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyCannotDisableLastRemainingSuperAdministratorAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=50");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<HostUserResponse>>(
            cancellationToken);
        Assert.IsNotNull(page);
        var admin = page.Items.Single(user => user.Username == "admin");

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/users/{admin.Id:D}/disable",
            adminToken,
            new { });
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, disableResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await disableResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            IdentityErrorCodes.SuperAdministratorLastRemaining,
            problem.RootElement.GetProperty("code").GetString());

        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", Api.FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    private static async Task VerifyUpdateDisplayNameWithOptimisticVersionAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var username = $"update-{Guid.NewGuid():N}";

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(
                username,
                "更新前名称",
                Api.FullNetApiFactory.TestPassword));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HostUserResponse>(
            cancellationToken);
        Assert.IsNotNull(created);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/users/{created.Id:D}",
            adminToken,
            new UpdateHostUserRequest("更新后名称", created.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<HostUserResponse>(
            cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual("更新后名称", updated.DisplayName);
        Assert.AreEqual(created.Version + 1, updated.Version);

        using var staleRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/users/{created.Id:D}",
            adminToken,
            new UpdateHostUserRequest("冲突名称", created.Version));
        using var staleResponse = await client.SendAsync(staleRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, staleResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await staleResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            IdentityErrorCodes.ProfileVersionConflict,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyResetPasswordInvalidatesOldCredentialsAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var username = $"reset-{Guid.NewGuid():N}";
        const string originalPassword = "FullNet!2026Secure";
        const string newPassword = "FullNet!2026Rotate";

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(username, "重置密码测试", originalPassword));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HostUserResponse>(
            cancellationToken);
        Assert.IsNotNull(created);

        using var loginBeforeRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, originalPassword)),
        };
        loginBeforeRequest.Headers.Add("Origin", "http://localhost");
        using var loginBeforeResponse = await client.SendAsync(loginBeforeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginBeforeResponse.StatusCode);

        using var resetRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/users/{created.Id:D}/reset-password",
            adminToken,
            new ResetHostUserPasswordRequest(newPassword));
        using var resetResponse = await client.SendAsync(resetRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, resetResponse.StatusCode);

        using var oldLoginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, originalPassword)),
        };
        oldLoginRequest.Headers.Add("Origin", "http://localhost");
        using var oldLoginResponse = await client.SendAsync(oldLoginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, oldLoginResponse.StatusCode);

        using var newLoginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, newPassword)),
        };
        newLoginRequest.Headers.Add("Origin", "http://localhost");
        using var newLoginResponse = await client.SendAsync(newLoginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, newLoginResponse.StatusCode);
    }

    private static async Task VerifyEnableUserRestoresLoginAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var username = $"enabled-{Guid.NewGuid():N}";
        var password = Api.FullNetApiFactory.TestPassword;

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(username, "启用测试", password));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HostUserResponse>(
            cancellationToken);
        Assert.IsNotNull(created);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/users/{created.Id:D}/disable",
            adminToken,
            new { });
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);

        using var loginWhileDisabledRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, password)),
        };
        loginWhileDisabledRequest.Headers.Add("Origin", "http://localhost");
        using var loginWhileDisabledResponse = await client.SendAsync(
            loginWhileDisabledRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, loginWhileDisabledResponse.StatusCode);

        using var enableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/users/{created.Id:D}/enable",
            adminToken,
            new { });
        using var enableResponse = await client.SendAsync(enableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enableResponse.StatusCode);
        var enabled = await enableResponse.Content.ReadFromJsonAsync<HostUserResponse>(
            cancellationToken);
        Assert.IsNotNull(enabled);
        Assert.IsTrue(enabled.IsActive);
        Assert.AreEqual(created.Version + 2, enabled.Version);

        using var loginAfterEnableRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, password)),
        };
        loginAfterEnableRequest.Headers.Add("Origin", "http://localhost");
        using var loginAfterEnableResponse = await client.SendAsync(
            loginAfterEnableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginAfterEnableResponse.StatusCode);
    }

    private static async Task VerifyExactActionPermissionBoundariesAsync(
        Api.FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var activeUsername = $"action-active-{Guid.NewGuid():N}";
        var disabledUsername = $"action-disabled-{Guid.NewGuid():N}";
        var password = Api.FullNetApiFactory.TestPassword;

        var activeUser = await CreateHostUserAsync(
            client,
            adminToken,
            activeUsername,
            "动作边界活跃用户",
            password,
            cancellationToken);
        var disabledUser = await CreateHostUserAsync(
            client,
            adminToken,
            disabledUsername,
            "动作边界禁用用户",
            password,
            cancellationToken);
        await PostWithoutBodyAsync(
            client,
            adminToken,
            $"/api/v1/identity/users/{disabledUser.Id:D}/disable",
            HttpStatusCode.OK,
            cancellationToken);

        var readOnlyToken = await factory.CreateHostAccessTokenAsync(
            [IdentityUserManagementPermissions.Read],
            cancellationToken);
        await AssertUsersListAllowedAsync(client, readOnlyToken, cancellationToken);
        await AssertPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            "/api/v1/identity/users",
            new CreateHostUserRequest(
                $"denied-{Guid.NewGuid():N}",
                "拒绝创建",
                password),
            cancellationToken);
        await AssertPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Put,
            $"/api/v1/identity/users/{activeUser.Id:D}",
            new UpdateHostUserRequest("拒绝更新", activeUser.Version),
            cancellationToken);
        await AssertPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            $"/api/v1/identity/users/{activeUser.Id:D}/disable",
            new { },
            cancellationToken);
        await AssertPermissionDeniedAsync<object?>(
            client,
            readOnlyToken,
            HttpMethod.Get,
            "/api/v1/identity/users/export",
            null,
            cancellationToken);

        var createToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityUserManagementPermissions.Read,
                IdentityUserManagementPermissions.Create,
            ],
            cancellationToken);
        var createdByLimited = await CreateHostUserAsync(
            client,
            createToken,
            $"limited-create-{Guid.NewGuid():N}",
            "受限创建用户",
            password,
            cancellationToken);
        await AssertPermissionDeniedAsync(
            client,
            createToken,
            HttpMethod.Post,
            $"/api/v1/identity/users/{createdByLimited.Id:D}/disable",
            new { },
            cancellationToken);

        var updateToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityUserManagementPermissions.Read,
                IdentityUserManagementPermissions.Update,
            ],
            cancellationToken);
        await AssertOkAsync(
            client,
            updateToken,
            HttpMethod.Put,
            $"/api/v1/identity/users/{activeUser.Id:D}",
            new UpdateHostUserRequest("受限更新名称", activeUser.Version),
            cancellationToken);
        await AssertPermissionDeniedAsync(
            client,
            updateToken,
            HttpMethod.Post,
            "/api/v1/identity/users",
            new CreateHostUserRequest(
                $"denied-update-{Guid.NewGuid():N}",
                "拒绝创建",
                password),
            cancellationToken);

        var disableToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityUserManagementPermissions.Read,
                IdentityUserManagementPermissions.Disable,
            ],
            cancellationToken);
        var disableTarget = await CreateHostUserAsync(
            client,
            adminToken,
            $"disable-target-{Guid.NewGuid():N}",
            "禁用目标",
            password,
            cancellationToken);
        await PostWithoutBodyAsync(
            client,
            disableToken,
            $"/api/v1/identity/users/{disableTarget.Id:D}/disable",
            HttpStatusCode.OK,
            cancellationToken);
        await AssertPermissionDeniedAsync(
            client,
            disableToken,
            HttpMethod.Post,
            $"/api/v1/identity/users/{disabledUser.Id:D}/enable",
            new { },
            cancellationToken);

        var enableToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityUserManagementPermissions.Read,
                IdentityUserManagementPermissions.Enable,
            ],
            cancellationToken);
        await PostWithoutBodyAsync(
            client,
            enableToken,
            $"/api/v1/identity/users/{disabledUser.Id:D}/enable",
            HttpStatusCode.OK,
            cancellationToken);
        await AssertPermissionDeniedAsync(
            client,
            enableToken,
            HttpMethod.Post,
            $"/api/v1/identity/users/{activeUser.Id:D}/disable",
            new { },
            cancellationToken);

        var resetToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityUserManagementPermissions.Read,
                IdentityUserManagementPermissions.ResetPassword,
            ],
            cancellationToken);
        var resetTarget = await CreateHostUserAsync(
            client,
            adminToken,
            $"reset-target-{Guid.NewGuid():N}",
            "重置密码目标",
            password,
            cancellationToken);
        await PostWithoutBodyAsync(
            client,
            resetToken,
            $"/api/v1/identity/users/{resetTarget.Id:D}/reset-password",
            HttpStatusCode.OK,
            cancellationToken,
            new ResetHostUserPasswordRequest("FullNet!2026Rotate"));
        await AssertPermissionDeniedAsync(
            client,
            resetToken,
            HttpMethod.Put,
            $"/api/v1/identity/users/{resetTarget.Id:D}",
            new UpdateHostUserRequest("拒绝更新", resetTarget.Version),
            cancellationToken);

        var exportToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityUserManagementPermissions.Read,
                IdentityUserManagementPermissions.Export,
            ],
            cancellationToken);
        await AssertOkAsync<object?>(
            client,
            exportToken,
            HttpMethod.Get,
            "/api/v1/identity/users/export",
            null,
            cancellationToken);
        using (var workbookExportRequest = new HttpRequestMessage(
                   HttpMethod.Get,
                   "/api/v1/identity/users/export-file"))
        {
            workbookExportRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", exportToken);
            using var workbookExportResponse = await client.SendAsync(
                workbookExportRequest,
                cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, workbookExportResponse.StatusCode);
            Assert.AreEqual(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                workbookExportResponse.Content.Headers.ContentType?.MediaType);
            var bytes = await workbookExportResponse.Content.ReadAsByteArrayAsync(
                cancellationToken);
            CollectionAssert.AreEqual(new byte[] { 0x50, 0x4B }, bytes.Take(2).ToArray());
        }
        await AssertPermissionDeniedAsync(
            client,
            exportToken,
            HttpMethod.Post,
            "/api/v1/identity/users",
            new CreateHostUserRequest(
                $"denied-export-{Guid.NewGuid():N}",
                "拒绝创建",
                password),
            cancellationToken);

        var importToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityUserManagementPermissions.Read,
                IdentityUserManagementPermissions.Import,
            ],
            cancellationToken);
        byte[] importTemplate;
        using (var templateRequest = new HttpRequestMessage(
                   HttpMethod.Get,
                   "/api/v1/identity/users/import-template"))
        {
            templateRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", importToken);
            using var templateResponse = await client.SendAsync(
                templateRequest,
                cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, templateResponse.StatusCode);
            importTemplate = await templateResponse.Content.ReadAsByteArrayAsync(
                cancellationToken);
        }

        using (var multipart = new MultipartFormDataContent())
        {
            var workbook = new ByteArrayContent(importTemplate);
            workbook.Headers.ContentType = new MediaTypeHeaderValue(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            multipart.Add(workbook, "file", "host-users-template.xlsx");
            using var importFileRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/identity/users/import-file")
            {
                Content = multipart,
            };
            importFileRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", importToken);
            using var importFileResponse = await client.SendAsync(
                importFileRequest,
                cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, importFileResponse.StatusCode);
            var fileResult = await importFileResponse.Content
                .ReadFromJsonAsync<ImportHostUsersResponse>(cancellationToken);
            Assert.IsNotNull(fileResult);
            Assert.AreEqual(0, fileResult.SucceededCount);
        }
        await AssertOkAsync(
            client,
            importToken,
            HttpMethod.Post,
            "/api/v1/identity/users/import",
            new ImportHostUsersRequest(
            [
                new CreateHostUserRequest(
                    $"imported-{Guid.NewGuid():N}",
                    "导入用户",
                    password),
            ]),
            cancellationToken);
        using (var profileImportRequest = CreateBearerJsonRequest(
                   HttpMethod.Post,
                   "/api/v1/identity/users/import",
                   importToken,
                   new ImportHostUsersRequest(
                   [
                       new CreateHostUserRequest(
                           $"profile-import-denied-{Guid.NewGuid():N}",
                           "拒绝越权资料导入",
                           password,
                           Profile: CreateProfile(phoneNumber: "+8613800000000")),
                   ])))
        using (var profileImportResponse = await client.SendAsync(
                   profileImportRequest,
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, profileImportResponse.StatusCode);
            var profileImport = await profileImportResponse.Content
                .ReadFromJsonAsync<ImportHostUsersResponse>(cancellationToken);
            Assert.IsNotNull(profileImport);
            Assert.AreEqual(0, profileImport.SucceededCount);
            Assert.AreEqual(
                CommonErrorCodes.PermissionDenied,
                profileImport.Results.Single().ErrorCode);
        }
        using var rejectedImport = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users/import",
            importToken,
            new ImportHostUsersRequest(
            [
                new CreateHostUserRequest(
                    $"sa-import-{Guid.NewGuid():N}",
                    "拒绝超管导入",
                    password,
                    IdentityAccountTypes.SuperAdmin),
            ]));
        using var rejectedResponse = await client.SendAsync(
            rejectedImport,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, rejectedResponse.StatusCode);
        var rejected = await rejectedResponse.Content
            .ReadFromJsonAsync<ImportHostUsersResponse>(cancellationToken);
        Assert.IsNotNull(rejected);
        Assert.AreEqual(0, rejected.SucceededCount);
        Assert.AreEqual(
            IdentityErrorCodes.SuperAdministratorImportRejected,
            rejected.Results.Single().ErrorCode);
        await AssertPermissionDeniedAsync(
            client,
            exportToken,
            HttpMethod.Post,
            "/api/v1/identity/users/import",
            new ImportHostUsersRequest(
            [
                new CreateHostUserRequest(
                    $"denied-import-{Guid.NewGuid():N}",
                    "拒绝导入",
                    password),
            ]),
            cancellationToken);

        var batchDisableToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityUserManagementPermissions.Read,
                IdentityUserManagementPermissions.Disable,
            ],
            cancellationToken);
        var batchTarget = await CreateHostUserAsync(
            client,
            adminToken,
            $"batch-disable-{Guid.NewGuid():N}",
            "批量停用目标",
            password,
            cancellationToken);
        await AssertOkAsync(
            client,
            batchDisableToken,
            HttpMethod.Post,
            "/api/v1/identity/users/batch-disable",
            new BatchHostUserIdsRequest([batchTarget.Id]),
            cancellationToken);
    }

    private static async Task<HostUserResponse> CreateHostUserAsync(
        HttpClient client,
        string accessToken,
        string username,
        string displayName,
        string password,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            accessToken,
            new CreateHostUserRequest(username, displayName, password));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<HostUserResponse>(cancellationToken);
        Assert.IsNotNull(created);
        return created;
    }

    private static async Task AssertUsersListAllowedAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertPermissionDeniedAsync<TRequest>(
        HttpClient client,
        string accessToken,
        HttpMethod method,
        string path,
        TRequest? body,
        CancellationToken cancellationToken)
    {
        using var request = body is null
            ? new HttpRequestMessage(method, path)
            : CreateBearerJsonRequest(method, path, accessToken, body);
        if (body is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task AssertOkAsync<TRequest>(
        HttpClient client,
        string accessToken,
        HttpMethod method,
        string path,
        TRequest? body,
        CancellationToken cancellationToken)
    {
        using var request = body is null
            ? new HttpRequestMessage(method, path)
            : CreateBearerJsonRequest(method, path, accessToken, body);
        if (body is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task PostWithoutBodyAsync(
        HttpClient client,
        string accessToken,
        string path,
        HttpStatusCode expectedStatus,
        CancellationToken cancellationToken,
        object? body = null)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            path,
            accessToken,
            body ?? new { });
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(expectedStatus, response.StatusCode);
    }

    private static async Task<string> LoginAsHostAdminAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", Api.FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
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
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }
}
