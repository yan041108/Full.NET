using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Api;

internal static class TenantLifecycleAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var token = await factory.CreateHostAccessTokenAsync(
            [
                "tenancy.host_tenants.read",
                "tenancy.tenant_lifecycle.suspend",
                "tenancy.tenant_lifecycle.reactivate",
            ],
            cancellationToken);
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/tenants?page=1&pageSize=20");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<TenantSummary>>(
            cancellationToken);
        Assert.IsNotNull(page);
        var tenant = page!.Items.FirstOrDefault(item =>
            item.Identifier == "acme" && item.IsActive);
        Assert.IsNotNull(tenant);

        var hostSwitchToken = await factory.CreateHostAccessTokenAsync(
            [
                "tenancy.tenants.switch",
            ],
            cancellationToken);
        var tenantSessionToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(
            client,
            hostSwitchToken,
            tenant!.Id,
            cancellationToken);

        using var suspendRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant!.Id:D}/suspend")
        {
            Content = JsonContent.Create(new SuspendTenantRequest(tenant.Version)),
        };
        suspendRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var suspendResponse = await client.SendAsync(suspendRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, suspendResponse.StatusCode);
        var suspended = await suspendResponse.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken);
        Assert.IsNotNull(suspended);
        Assert.IsFalse(suspended!.IsActive);
        Assert.AreEqual(TenantLifecycleStatuses.Suspended, suspended.LifecycleStatus);

        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        using var blockedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenancy/current");
        using var blockedResponse = await tenantClient.SendAsync(blockedRequest, cancellationToken);
        Assert.IsFalse(
            blockedResponse.IsSuccessStatusCode,
            $"Suspended tenant API should be blocked but returned {blockedResponse.StatusCode}.");

        using var blockedInviteRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/identity/tenant-members/invitations")
        {
            Content = JsonContent.Create(new CreateTenantInvitationRequest(
                $"blocked-{Guid.NewGuid():N}@example.com",
                TenantMemberRoles.Member)),
        };
        blockedInviteRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            tenantSessionToken);
        using var blockedTenantClient = factory.CreateClientForHost("acme.localhost");
        using var blockedInviteResponse = await blockedTenantClient.SendAsync(
            blockedInviteRequest,
            cancellationToken);
        Assert.IsFalse(
            blockedInviteResponse.IsSuccessStatusCode,
            "Suspended tenant should block protected tenant-member writes.");
        using var inviteProblem = JsonDocument.Parse(
            await blockedInviteResponse.Content.ReadAsStringAsync(cancellationToken));
        var inviteErrorCode = inviteProblem.RootElement.GetProperty("code").GetString();
        Assert.IsTrue(
            string.Equals(
                inviteErrorCode,
                IdentityErrorCodes.TenantMembershipTenantInactive,
                StringComparison.Ordinal)
            || string.Equals(
                inviteErrorCode,
                TenancyErrorCodes.ContextMismatch,
                StringComparison.Ordinal),
            inviteErrorCode);

        await VerifySuspendedTenantBlocksResourceFileUploadAsync(
            factory,
            tenant!.Id,
            tenant.Identifier,
            tenant.Name,
            cancellationToken);

        await VerifySuspendedTenantBlocksRealtimeConnectionAsync(
            factory,
            tenantSessionToken,
            cancellationToken);

        await VerifyHostScopedConsumersRemainAvailableWhenTenantSuspendedAsync(
            client,
            token,
            cancellationToken);

        using var reactivateRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/reactivate")
        {
            Content = JsonContent.Create(new ReactivateTenantRequest(suspended.Version)),
        };
        reactivateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var reactivateResponse = await client.SendAsync(reactivateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, reactivateResponse.StatusCode);
        var reactivated = await reactivateResponse.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken);
        Assert.IsNotNull(reactivated);
        Assert.IsTrue(reactivated!.IsActive);
        Assert.AreEqual(TenantLifecycleStatuses.Active, reactivated.LifecycleStatus);
    }

    private static async Task VerifySuspendedTenantBlocksResourceFileUploadAsync(
        FullNetApiFactory factory,
        Guid tenantId,
        string tenantIdentifier,
        string tenantName,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetTenant(new TenantContext(tenantId, tenantIdentifier, tenantName));
        try
        {
            var store = scope.ServiceProvider.GetRequiredService<ITenantResourceFileStore>();
            using var content = new MemoryStream(new byte[] { 9 });
            var upload = await store.UploadAsync(
                "import_export",
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                "suspended-probe.bin",
                "application/octet-stream",
                content,
                1,
                cancellationToken);
            Assert.IsFalse(upload.IsSuccess);
            Assert.AreEqual(FilesErrorCodes.TenantInactive, upload.Error!.Code);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private static async Task VerifySuspendedTenantBlocksRealtimeConnectionAsync(
        FullNetApiFactory factory,
        string tenantAccessToken,
        CancellationToken cancellationToken)
    {
        await using var connection = new HubConnectionBuilder()
            .WithUrl(
                "http://localhost/hubs/notifications",
                options =>
                {
                    options.AccessTokenProvider = () =>
                        Task.FromResult<string?>(tenantAccessToken);
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ =>
                        factory.Server.CreateHandler();
                })
            .Build();

        try
        {
            await connection.StartAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HubException or HttpRequestException or InvalidOperationException)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        Assert.AreNotEqual(
            HubConnectionState.Connected,
            connection.State,
            "Suspended tenant Realtime connection should not remain connected.");
    }

    private static async Task VerifyHostScopedConsumersRemainAvailableWhenTenantSuspendedAsync(
        HttpClient hostClient,
        string hostLifecycleToken,
        CancellationToken cancellationToken)
    {
        var hostAdminToken = await LoginAsHostAdminAsync(
            hostClient,
            cancellationToken);

        using var jobsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/jobs/host-definitions?page=1&pageSize=5");
        jobsRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            hostAdminToken);
        using var jobsResponse = await hostClient.SendAsync(jobsRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, jobsResponse.StatusCode);

        using var openAccessRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/open-access-clients?page=1&pageSize=5");
        openAccessRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            hostAdminToken);
        using var openAccessResponse = await hostClient.SendAsync(
            openAccessRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, openAccessResponse.StatusCode);

        using var lifecycleProbe = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/tenants?page=1&pageSize=5");
        lifecycleProbe.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            hostLifecycleToken);
        using var lifecycleResponse = await hostClient.SendAsync(lifecycleProbe, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, lifecycleResponse.StatusCode);
    }

    private static async Task<string> LoginAsHostAdminAsync(
        HttpClient hostClient,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await hostClient.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginToken = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(loginToken);
        return loginToken!.AccessToken;
    }
}