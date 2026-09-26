using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Files;

internal static class TenantResourceFileStorageQuotaAssertions
{
    public static async Task VerifyUploadBlockedWhenStorageQuotaExhaustedAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        var acmeTenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(
            tenantClient,
            cancellationToken);

        using var hostClient = factory.CreateClientForHost("localhost");
        var quotaToken = await factory.CreateHostAccessTokenAsync(
            [
                "tenancy.tenant_quota.read",
                "tenancy.tenant_quota.manage",
            ],
            cancellationToken);
        await UpsertStorageBytesLimitAsync(
            hostClient,
            quotaToken,
            acmeTenant.Id,
            limitBytes: 0,
            cancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetTenant(new TenantContext(
            acmeTenant.Id,
            acmeTenant.Identifier,
            acmeTenant.Name));
        try
        {
            var store = scope.ServiceProvider.GetRequiredService<ITenantResourceFileStore>();
            using var content = new MemoryStream(new byte[] { 1, 2, 3 });
            var upload = await store.UploadAsync(
                "import_export",
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                "probe.bin",
                "application/octet-stream",
                content,
                3,
                cancellationToken);
            Assert.IsFalse(upload.IsSuccess);
            Assert.AreEqual(FilesErrorCodes.StorageQuotaExceeded, upload.Error!.Code);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private static async Task UpsertStorageBytesLimitAsync(
        HttpClient hostClient,
        string accessToken,
        Guid tenantId,
        long limitBytes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/tenancy/tenants/{tenantId:D}/quota/metrics")
        {
            Content = JsonContent.Create(new UpsertTenantQuotaMetricRequest(
                TenantQuotaMetricCodes.FilesStorageBytes,
                TenantQuotaDefaults.PeriodKey,
                limitBytes)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await hostClient.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
