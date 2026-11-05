using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Features.Delivery;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.TenantResourceFiles;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

/// <summary>真实来源文件打开后，交付复核失败或取消必须释放流，成功时交给调用方。</summary>
[TestClass]
public sealed class EnterpriseRequestDownloadOwnershipTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Download_transfers_stream_only_after_successful_final_checks(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var fault = new ReadFault();
        using var factory = new FullNetApiFactory(provider, connection,
            new Dictionary<string, string?> { ["EnterpriseRequest:Delivery:Enabled"] = "false" },
            configureTestServices: services =>
            {
                services.AddScoped<TenantResourceFileStore>();
                services.Replace(ServiceDescriptor.Scoped<ITenantResourceFileStore>(sp => new ObservedStore(sp.GetRequiredService<TenantResourceFileStore>(), fault)));
                var original = services.Last(d => d.ServiceType == typeof(IBackgroundSessionAuthorization));
                services.Replace(ServiceDescriptor.Scoped<IBackgroundSessionAuthorization>(sp => new ReadAuthorization(
                    (IBackgroundSessionAuthorization)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!), fault)));
            }, includeEnterpriseRequest: true);
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var jwt = await EnterpriseRequestAssertions.LoginAndEnterAcmeTenantAsync(client, default);
        var organization = await EnterpriseRequestAssertions.CreateOrganizationUnitAsync(client, jwt, default);
        var user = await EnterpriseRequestAssertions.GetCurrentUserIdAsync(client, jwt, default);
        client.DefaultRequestHeaders.Authorization = new("Bearer", jwt);
        const string root = "/api/v1/enterprise_request/enterprise-requests";
        using var create = new HttpRequestMessage(HttpMethod.Post, root) { Content = JsonContent.Create(new
        { requestNumber = "STREAM-" + Guid.NewGuid().ToString("N")[..12], title = "Stream ownership", status = "Draft", totalAmount = "12.50", applicantUserId = user }) };
        create.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, organization.ToString());
        using var created = await client.SendAsync(create);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var request = (await created.Content.ReadFromJsonAsync<EnterpriseRequestResponse>())!;
        var route = $"{root}/{request.Id:D}";
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("stream ownership"u8.ToArray());
        fileContent.Headers.ContentType = new("text/plain");
        form.Add(fileContent, "file", "ownership.txt");
        using var upload = await client.PostAsync($"{route}/attachments?idempotencyKey={Guid.NewGuid():D}&requestVersion={request.Version}", form);
        Assert.AreEqual(HttpStatusCode.OK, upload.StatusCode, await upload.Content.ReadAsStringAsync());
        var attachment = (await upload.Content.ReadFromJsonAsync<EnterpriseRequestAttachmentResponse>())!;
        request = (await client.GetFromJsonAsync<EnterpriseRequestResponse>(route))!;
        using var reportCreated = await client.PostAsJsonAsync($"{route}/reports", new CreateEnterpriseRequestReportRequest(Guid.NewGuid(), request.Version));
        Assert.AreEqual(HttpStatusCode.OK, reportCreated.StatusCode, await reportCreated.Content.ReadAsStringAsync());
        var report = (await reportCreated.Content.ReadFromJsonAsync<EnterpriseRequestReportResponse>())!;
        using var worker = new EnterpriseRequestDeliveryWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new EnterpriseRequestDeliveryOptions { Enabled = true, IntervalSeconds = 1 }),
            new EnterpriseRequestDeliveryScanCursor(), NullLogger<EnterpriseRequestDeliveryWorker>.Instance);
        await worker.RunBatchAsync(default);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var tenantId = await db.QuerySingleAsync<Guid>("SELECT TenantId FROM demo_enterprise_request_delivery WHERE Id=@Id", new { attachment.Id });
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = await scope.ServiceProvider.GetRequiredService<IActiveTenantContextResolver>().ResolveActiveByIdAsync(tenantId, default);
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(tenant!);
        var processor = scope.ServiceProvider.GetRequiredService<EnterpriseRequestDeliveryProcessor>();
        var service = scope.ServiceProvider.GetRequiredService<EnterpriseRequestDeliveryService>();
        foreach (var (id, kind) in new[] { (attachment.Id, "Attachment"), (report.Id, "Report") })
        {
            var work = (await processor.FindAsync(id, default))!;
            Assert.AreEqual("Ready", work.Phase);
            foreach (var scenario in new[] { "success", "denied", "exception", "canceled" })
            {
                using var cancellation = new CancellationTokenSource();
                fault.OpenedStream = null;
                fault.AfterOpen = () => { if (scenario == "canceled") cancellation.Cancel(); else fault.Mode = scenario; };
                try
                {
                    if (scenario == "exception")
                        await Assert.ThrowsExactlyAsync<IOException>(() => service.DownloadAsync(request.Id, id, kind, work.Binding(), cancellation.Token));
                    else if (scenario == "canceled")
                        await Assert.ThrowsAsync<OperationCanceledException>(() => service.DownloadAsync(request.Id, id, kind, work.Binding(), cancellation.Token));
                    else
                    {
                        var result = await service.DownloadAsync(request.Id, id, kind, work.Binding(), cancellation.Token);
                        Assert.AreEqual(scenario == "success", result.IsSuccess, kind + " " + scenario);
                        if (result.IsSuccess)
                        {
                            Assert.IsTrue(result.Value!.Content.CanRead, "成功交付不能提前释放。");
                            await result.Value.Content.DisposeAsync();
                        }
                        else Assert.AreEqual("enterprise_request.delivery_forbidden", result.Error!.Code);
                    }
                    Assert.IsNotNull(fault.OpenedStream, kind + " " + scenario);
                    Assert.IsFalse(fault.OpenedStream.CanRead, "失败、异常和取消均须释放实际 Files 流。");
                }
                finally { fault.AfterOpen = null; fault.Mode = null; if (fault.OpenedStream is not null) await fault.OpenedStream.DisposeAsync(); }
            }
        }
    }

    private sealed class ReadFault { internal Action? AfterOpen; internal Stream? OpenedStream; internal string? Mode; }
    private sealed class ReadAuthorization(IBackgroundSessionAuthorization inner, ReadFault fault) : IBackgroundSessionAuthorization
    {
        public Task<AuthorizedSessionActor?> AuthorizeAsync(SessionBindingSnapshot binding, string permissionCode, CancellationToken cancellationToken = default) =>
            inner.AuthorizeAsync(binding, permissionCode, cancellationToken);
        public Task<AuthorizedSessionActor?> AuthorizeAllAsync(SessionBindingSnapshot binding, IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken = default)
        {
            if (fault.Mode == "exception") { fault.Mode = null; throw new IOException("Injected authority read failure after file open."); }
            if (fault.Mode == "denied") { fault.Mode = null; return Task.FromResult<AuthorizedSessionActor?>(null); }
            return inner.AuthorizeAllAsync(binding, permissionCodes, cancellationToken);
        }
    }
    private sealed class ObservedStore(ITenantResourceFileStore inner, ReadFault fault) : ITenantResourceFileStore
    {
        public Task<Result<TenantResourceFileReference>> UploadAsync(string ownerModuleKey, Guid resourceId, Guid actorUserId, string originalFileName, string contentType,
            Stream content, long length, CancellationToken cancellationToken = default) => inner.UploadAsync(ownerModuleKey, resourceId, actorUserId, originalFileName, contentType, content, length, cancellationToken);
        public async Task<Result<TenantResourceFileContent>> OpenReadyContentAsync(string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default)
        {
            var result = await inner.OpenReadyContentAsync(ownerModuleKey, resourceId, fileId, cancellationToken);
            if (result.IsSuccess) { fault.OpenedStream = result.Value!.Content; fault.AfterOpen?.Invoke(); }
            return result;
        }
        public Task<IReadOnlyList<TenantResourceFileReadyItem>> ListReadyAsync(string ownerModuleKey, Guid resourceId, CancellationToken cancellationToken = default) => inner.ListReadyAsync(ownerModuleKey, resourceId, cancellationToken);
        public Task ReleaseAsync(string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default) => inner.ReleaseAsync(ownerModuleKey, resourceId, fileId, cancellationToken);
    }
}
