using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Organization;

/// <summary>
/// 租户职位管理纵向切片验收夹具。
/// </summary>
internal static class OrganizationPositionManagementAssertions
{
    public static async Task VerifyTenantPositionManagementContractAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionInTenantContextAsync(
            factory,
            client,
            cancellationToken);
        await VerifyCreateRejectsDuplicateCodeAsync(client, cancellationToken);
        await VerifyPositionLevelLifecycleAsync(client, cancellationToken);
        await VerifyPositionLevelBindingLifecycleAsync(client, cancellationToken);
        await VerifyPositionUnitBindingLifecycleAsync(client, cancellationToken);
        await VerifyCustomPositionLifecycleAsync(client, cancellationToken);
        await OpenApiOrganizationTenantPositionsContractAssertions.VerifyAsync(
            client,
            cancellationToken);
        await OpenApiOrganizationTenantPositionLevelsContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyListRequiresReadPermissionInTenantContextAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var tenantToken = await EnterAcmeTenantAsync(
            client,
            await factory.CreateHostAccessTokenAsync(
                [
                    "platform.dashboard.read",
                    "tenancy.tenants.read",
                    "tenancy.tenants.switch",
                ],
                cancellationToken),
            cancellationToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/organization/positions?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            tenantToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyCreateRejectsDuplicateCodeAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var code = $"pos-{Guid.NewGuid():N}"[..20].ToLowerInvariant();
        var body = new CreateOrganizationPositionRequest(code, "集成测试职位", 10);

        using var firstRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/positions",
            adminTenantToken,
            body);
        using var firstResponse = await client.SendAsync(firstRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);

        using var duplicateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/positions",
            adminTenantToken,
            body);
        using var duplicateResponse = await client.SendAsync(
            duplicateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await duplicateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            OrganizationErrorCodes.PositionCodeExists,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyPositionLevelLifecycleAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var code = $"level-{Guid.NewGuid():N}"[..24].ToLowerInvariant();
        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/position-levels",
            adminTenantToken,
            new
            {
                code,
                name = "高级职级",
                displayOrder = 10,
            });
        using var createResponse = await client.SendAsync(
            createRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        using var created = JsonDocument.Parse(
            await createResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(code, created.RootElement.GetProperty("code").GetString());
        Assert.AreEqual("高级职级", created.RootElement.GetProperty("name").GetString());
        Assert.IsTrue(created.RootElement.GetProperty("isActive").GetBoolean());
        var levelId = created.RootElement.GetProperty("id").GetGuid();
        var version = created.RootElement.GetProperty("version").GetInt32();

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/organization/position-levels/{levelId:D}",
            adminTenantToken,
            new
            {
                name = "专家职级",
                displayOrder = 20,
                version,
            });
        using var updateResponse = await client.SendAsync(
            updateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        using var updated = JsonDocument.Parse(
            await updateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual("专家职级", updated.RootElement.GetProperty("name").GetString());
        Assert.AreEqual(version + 1, updated.RootElement.GetProperty("version").GetInt32());

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/organization/position-levels/{levelId:D}/disable",
            adminTenantToken,
            new { });
        using var disableResponse = await client.SendAsync(
            disableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        using var disabled = JsonDocument.Parse(
            await disableResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.IsFalse(disabled.RootElement.GetProperty("isActive").GetBoolean());
    }

    private static async Task VerifyPositionLevelBindingLifecycleAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        using var createLevelRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/position-levels",
            adminTenantToken,
            new
            {
                code = $"level-{Guid.NewGuid():N}"[..24].ToLowerInvariant(),
                name = "职位绑定职级",
                displayOrder = 10,
            });
        using var createLevelResponse = await client.SendAsync(
            createLevelRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createLevelResponse.StatusCode);
        using var level = JsonDocument.Parse(
            await createLevelResponse.Content.ReadAsStringAsync(cancellationToken));
        var positionLevelId = level.RootElement.GetProperty("id").GetGuid();
        var positionLevelCode = level.RootElement.GetProperty("code").GetString();

        using var createPositionRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/positions",
            adminTenantToken,
            new CreateOrganizationPositionRequest(
                $"pos-{Guid.NewGuid():N}"[..20].ToLowerInvariant(),
                "职级绑定职位",
                10));
        using var createPositionResponse = await client.SendAsync(
            createPositionRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createPositionResponse.StatusCode);
        using var position = JsonDocument.Parse(
            await createPositionResponse.Content.ReadAsStringAsync(cancellationToken));
        var positionId = position.RootElement.GetProperty("id").GetGuid();
        var positionVersion = position.RootElement.GetProperty("version").GetInt32();

        using var bindRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/organization/positions/{positionId:D}/position-level",
            adminTenantToken,
            new { positionLevelId, version = positionVersion });
        using var bindResponse = await client.SendAsync(bindRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, bindResponse.StatusCode);
        using var bound = JsonDocument.Parse(
            await bindResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            positionLevelId,
            bound.RootElement.GetProperty("positionLevelId").GetGuid());
        Assert.AreEqual(
            positionLevelCode,
            bound.RootElement.GetProperty("positionLevelCode").GetString());
        Assert.AreEqual(
            "职位绑定职级",
            bound.RootElement.GetProperty("positionLevelName").GetString());
        var boundVersion = bound.RootElement.GetProperty("version").GetInt32();
        Assert.AreEqual(positionVersion + 1, boundVersion);

        using var missingLevelRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/organization/positions/{positionId:D}/position-level",
            adminTenantToken,
            new { positionLevelId = Guid.NewGuid(), version = boundVersion });
        using var missingLevelResponse = await client.SendAsync(
            missingLevelRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, missingLevelResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await missingLevelResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            OrganizationErrorCodes.PositionLevelNotFound,
            problem.RootElement.GetProperty("code").GetString());

        using var unbindRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/organization/positions/{positionId:D}/position-level",
            adminTenantToken,
            new { positionLevelId = (Guid?)null, version = boundVersion });
        using var unbindResponse = await client.SendAsync(unbindRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, unbindResponse.StatusCode);
        using var unbound = JsonDocument.Parse(
            await unbindResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            JsonValueKind.Null,
            unbound.RootElement.GetProperty("positionLevelId").ValueKind);
        Assert.AreEqual(
            JsonValueKind.Null,
            unbound.RootElement.GetProperty("positionLevelName").ValueKind);
    }

    private static async Task VerifyPositionUnitBindingLifecycleAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var unitCode = $"unit-{Guid.NewGuid():N}".ToLowerInvariant();
        using var createUnitRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/units",
            adminTenantToken,
            new CreateOrganizationUnitRequest(null, unitCode, "职位所属机构", 10));
        using var createUnitResponse = await client.SendAsync(
            createUnitRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createUnitResponse.StatusCode);
        var unit = await createUnitResponse.Content.ReadFromJsonAsync<OrganizationUnitResponse>(
            cancellationToken);
        Assert.IsNotNull(unit);

        var positionCode = $"pos-{Guid.NewGuid():N}"[..20].ToLowerInvariant();
        using var createPositionRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/positions",
            adminTenantToken,
            new CreateOrganizationPositionRequest(positionCode, "机构绑定职位", 10));
        using var createPositionResponse = await client.SendAsync(
            createPositionRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createPositionResponse.StatusCode);
        var position = await createPositionResponse.Content
            .ReadFromJsonAsync<OrganizationPositionResponse>(cancellationToken);
        Assert.IsNotNull(position);

        using var bindRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/organization/positions/{position.Id:D}/unit",
            adminTenantToken,
            new { unitId = unit.Id, position.Version });
        using var bindResponse = await client.SendAsync(bindRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, bindResponse.StatusCode);
        using var bound = JsonDocument.Parse(
            await bindResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            unit.Id,
            bound.RootElement.GetProperty("unitId").GetGuid());
        Assert.AreEqual(
            unit.Code,
            bound.RootElement.GetProperty("unitCode").GetString());
        Assert.AreEqual(
            unit.Name,
            bound.RootElement.GetProperty("unitName").GetString());
        var boundVersion = bound.RootElement.GetProperty("version").GetInt32();
        Assert.AreEqual(position.Version + 1, boundVersion);

        using var missingUnitRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/organization/positions/{position.Id:D}/unit",
            adminTenantToken,
            new { unitId = Guid.NewGuid(), version = boundVersion });
        using var missingUnitResponse = await client.SendAsync(
            missingUnitRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, missingUnitResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await missingUnitResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            OrganizationErrorCodes.UnitNotFound,
            problem.RootElement.GetProperty("code").GetString());

        using var unbindRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/organization/positions/{position.Id:D}/unit",
            adminTenantToken,
            new { unitId = (Guid?)null, version = boundVersion });
        using var unbindResponse = await client.SendAsync(unbindRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, unbindResponse.StatusCode);
        using var unbound = JsonDocument.Parse(
            await unbindResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            JsonValueKind.Null,
            unbound.RootElement.GetProperty("unitId").ValueKind);
        Assert.AreEqual(
            JsonValueKind.Null,
            unbound.RootElement.GetProperty("unitName").ValueKind);
    }

    private static async Task VerifyCustomPositionLifecycleAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var code = $"pos-{Guid.NewGuid():N}"[..20].ToLowerInvariant();

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/positions",
            adminTenantToken,
            new CreateOrganizationPositionRequest(code, "生命周期职位", 10));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content
            .ReadFromJsonAsync<OrganizationPositionResponse>(cancellationToken);
        Assert.IsNotNull(created);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/organization/positions/{created.Id:D}",
            adminTenantToken,
            new UpdateOrganizationPositionRequest(
                "已更新职位",
                created.DisplayOrder,
                created.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content
            .ReadFromJsonAsync<OrganizationPositionResponse>(cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual("已更新职位", updated.Name);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/organization/positions/{created.Id:D}/disable",
            adminTenantToken,
            new { });
        using var disableResponse = await client.SendAsync(
            disableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabled = await disableResponse.Content
            .ReadFromJsonAsync<OrganizationPositionResponse>(cancellationToken);
        Assert.IsNotNull(disabled);
        Assert.IsFalse(disabled.IsActive);
    }

    private static async Task<string> LoginAndEnterAcmeTenantAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginToken = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(loginToken);
        return await EnterAcmeTenantAsync(client, loginToken.AccessToken, cancellationToken);
    }

    private static async Task<string> EnterAcmeTenantAsync(
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
        using var availableResponse = await client.SendAsync(availableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, availableResponse.StatusCode);
        var available = await availableResponse.Content
            .ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken);
        Assert.IsNotNull(available);
        var acme = available.Single(tenant => tenant.Identifier == "acme");

        using var enterRequest = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/tenancy/context")
        {
            Content = JsonContent.Create(new ChangeTenantContextRequest(acme.Id)),
        };
        enterRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            hostAccessToken);
        using var enterResponse = await client.SendAsync(enterRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enterResponse.StatusCode);
        var entered = await enterResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(entered);
        return entered.AccessToken;
    }

    private static HttpRequestMessage CreateBearerJsonRequest<TRequest>(
        HttpMethod method,
        string path,
        string accessToken,
        TRequest body)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null && method != HttpMethod.Get)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }
}
