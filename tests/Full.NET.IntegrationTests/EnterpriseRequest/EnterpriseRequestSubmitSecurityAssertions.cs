using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

internal static partial class EnterpriseRequestAssertions
{
    public static async Task VerifySubmitRejectsUnprivilegedOrUnassignedActorAsync(
        FullNetApiFactory factory, bool submitGranted, CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var admin = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var unit = await CreateOrganizationUnitAsync(client, admin, cancellationToken);
        await EnterpriseRequestWorkflowBootstrap.PublishDemoApprovalDefinitionInTenantAsync(client, admin, cancellationToken);
        using var create = new HttpRequestMessage(HttpMethod.Post, BasePath)
        {
            Content = JsonContent.Create(new
            {
                requestNumber = $"SEC-{Guid.NewGuid():N}"[..16], title = "Submit authorization probe",
                status = EnterpriseRequestStatusKeys.Draft, totalAmount = 1m, applicantUserId = Guid.NewGuid()
            })
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        create.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, unit.ToString("D"));
        using var createdResponse = await client.SendAsync(create, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<EnterpriseRequestResponse>(cancellationToken);
        Assert.IsNotNull(created);

        var permissions = new List<string> { "tenancy.tenants.read", "tenancy.tenants.switch", EnterpriseRequestPermissions.Read, EnterpriseRequestPermissions.Update };
        if (submitGranted) permissions.Add(EnterpriseRequestWorkflowPermissions.Submit);
        var identity = await factory.CreateHostIdentityAsync($"submit-{Guid.NewGuid():N}", permissions, cancellationToken);
        using var availableRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenancy/available");
        availableRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken);
        using var availableResponse = await client.SendAsync(availableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, availableResponse.StatusCode);
        var available = await availableResponse.Content.ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken);
        Assert.IsNotNull(available);
        var acme = available.Single(value => value.Identifier == "acme");
        using var enter = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tenancy/context")
        {
            Content = JsonContent.Create(new ChangeTenantContextRequest(acme.Id))
        };
        enter.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken);
        using var enteredResponse = await client.SendAsync(enter, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enteredResponse.StatusCode);
        var entered = await enteredResponse.Content.ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(entered);

        using var submit = new HttpRequestMessage(HttpMethod.Post, $"{BasePath}/{created.Id:D}/submit-for-approval");
        submit.Headers.Authorization = new AuthenticationHeaderValue("Bearer", entered.AccessToken);
        // 请求头提供机构不能替代对记录原机构的写授权。
        submit.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, unit.ToString("D"));
        using var denied = await client.SendAsync(submit, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode, await denied.Content.ReadAsStringAsync(cancellationToken));
        using var problem = JsonDocument.Parse(await denied.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(submitGranted ? OrganizationErrorCodes.WriteAccessDenied : "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
        using var writeLines = new HttpRequestMessage(HttpMethod.Put, $"{BasePath}/{created.Id:D}/lines")
        { Content = JsonContent.Create(new { version = created.Version.ToString(), items = Array.Empty<object>() }) };
        writeLines.Headers.Authorization = new AuthenticationHeaderValue("Bearer", entered.AccessToken);
        writeLines.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, unit.ToString("D"));
        using var linesDenied = await client.SendAsync(writeLines, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, linesDenied.StatusCode);
        using var read = new HttpRequestMessage(HttpMethod.Get, $"{BasePath}/{created.Id:D}");
        read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        using var readResponse = await client.SendAsync(read, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, readResponse.StatusCode);
        var unchanged = await readResponse.Content.ReadFromJsonAsync<EnterpriseRequestResponse>(cancellationToken);
        Assert.IsNotNull(unchanged);
        Assert.AreEqual(created.Status, unchanged.Status);
        Assert.AreEqual(created.Version, unchanged.Version);
        Assert.AreEqual(created.UpdatedAtUtc, unchanged.UpdatedAtUtc);
    }
}
