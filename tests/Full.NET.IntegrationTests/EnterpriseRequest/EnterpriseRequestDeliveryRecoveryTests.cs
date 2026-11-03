using System.Data.Common;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using DbUp;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.EnterpriseRequest;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Features.Delivery;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.TenantResourceFiles;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

/// <summary>正式双库、Files 和后台服务验证附件响应丢失、导出恢复及版本失效。</summary>
[TestClass]
public sealed partial class EnterpriseRequestDeliveryRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Files_and_reports_recover_without_reupload_and_cancel_or_stale_source_denies_download(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var fault = new UploadFault();
        using var factory = new FullNetApiFactory(provider, cs,
            new Dictionary<string, string?> { ["EnterpriseRequest:Delivery:Enabled"] = "false" },
            configureTestServices: services =>
            {
                services.AddSingleton(fault);
                services.AddScoped<TenantResourceFileStore>();
                services.Replace(ServiceDescriptor.Scoped<ITenantResourceFileStore>(sp =>
                    new LostUploadStore(sp.GetRequiredService<TenantResourceFileStore>(), fault)));
                var authorization = services.Last(d => d.ServiceType == typeof(IBackgroundSessionAuthorization));
                services.Replace(ServiceDescriptor.Scoped<IBackgroundSessionAuthorization>(sp => new FieldAuthorization(
                    (IBackgroundSessionAuthorization)ActivatorUtilities.CreateInstance(sp, authorization.ImplementationType!), fault)));
            }, includeEnterpriseRequest: true);
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var jwt = await EnterpriseRequestAssertions.LoginAndEnterAcmeTenantAsync(client, default);
        var organization = await EnterpriseRequestAssertions.CreateOrganizationUnitAsync(client, jwt, default);
        var user = await EnterpriseRequestAssertions.GetCurrentUserIdAsync(client, jwt, default);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        const string root = "/api/v1/enterprise_request/enterprise-requests";
        using var create = new HttpRequestMessage(HttpMethod.Post, root) { Content = JsonContent.Create(new
        { requestNumber = "DEL-" + Guid.NewGuid().ToString("N")[..12], title = "=delivery <probe>", status = "Draft", totalAmount = "12.50", applicantUserId = user }) };
        create.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, organization.ToString());
        using var created = await client.SendAsync(create);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var request = (await created.Content.ReadFromJsonAsync<EnterpriseRequestResponse>())!;
        var path = $"{root}/{request.Id:D}";
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(cs)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(cs));
        await db.OpenAsync();

        var key = Guid.NewGuid();
        fault.LoseNext = true;
        using (var upload = await Upload()) Assert.AreEqual(HttpStatusCode.InternalServerError, upload.StatusCode);
        Assert.AreEqual(1, fault.Uploads);
        var intent = await db.QuerySingleAsync<Guid>("SELECT Id FROM demo_enterprise_request_delivery WHERE RequestId=@Id", new { request.Id });
        await Due();
        using var worker = new EnterpriseRequestDeliveryWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new EnterpriseRequestDeliveryOptions { Enabled = true, IntervalSeconds = 1 }),
            new EnterpriseRequestDeliveryScanCursor(), NullLogger<EnterpriseRequestDeliveryWorker>.Instance);
        await worker.StartAsync(default);
        try { await WaitPhase(intent, "Ready"); } finally { await worker.StopAsync(default); }
        Assert.AreEqual(1, fault.Uploads, "Worker 应绑定 Files 已提交对象，不能重新上传。");
        using (var replay = await Upload()) Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode, await replay.Content.ReadAsStringAsync());
        Assert.AreEqual(1, fault.Uploads);
        using (var download = await client.GetAsync($"{path}/attachments/{intent:D}/download"))
        {
            Assert.AreEqual(HttpStatusCode.OK, download.StatusCode, await download.Content.ReadAsStringAsync());
            Assert.AreEqual("safe attachment", await download.Content.ReadAsStringAsync());
            Assert.AreEqual("application/octet-stream", download.Content.Headers.ContentType?.MediaType);
        }
        var version = (await client.GetFromJsonAsync<EnterpriseRequestResponse>(path))!.Version;
        var concurrentKey = Guid.NewGuid();
        var uploadsBeforeRace = fault.Uploads;
        var attempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Upload(concurrentKey, version)));
        foreach (var response in attempts)
        {
            using (response) Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        Assert.AreEqual(uploadsBeforeRace + 1, fault.Uploads, "并发同身份请求只能上传一个逻辑附件。");
        version = (await client.GetFromJsonAsync<EnterpriseRequestResponse>(path))!.Version;
        using var reportCreated = await client.PostAsJsonAsync($"{path}/reports", new CreateEnterpriseRequestReportRequest(Guid.NewGuid(), version));
        Assert.AreEqual(HttpStatusCode.OK, reportCreated.StatusCode, await reportCreated.Content.ReadAsStringAsync());
        var report = (await reportCreated.Content.ReadFromJsonAsync<EnterpriseRequestReportResponse>())!;
        fault.LoseNext = true;
        await Due(); await worker.RunBatchAsync(default); await WaitPhase(report.Id, "Running");
        var afterReportUpload = fault.Uploads;
        await db.ExecuteAsync("UPDATE demo_enterprise_request_delivery SET LeaseId=@Lease,LeaseUntilUtc=@Until WHERE Id=@Id",
            new { Id = report.Id, Lease = Guid.CreateVersion7(), Until = DateTimeOffset.UtcNow.AddMinutes(1) });
        await Due(); await worker.RunBatchAsync(default); Assert.AreEqual(afterReportUpload, fault.Uploads);
        await db.ExecuteAsync("UPDATE demo_enterprise_request_delivery SET LeaseUntilUtc=@Until WHERE Id=@Id", new { Id = report.Id, Until = DateTimeOffset.UtcNow.AddMinutes(-1) });
        await Due(); await worker.RunBatchAsync(default); await WaitPhase(report.Id, "Ready");
        Assert.AreEqual(afterReportUpload, fault.Uploads, "过期租约恢复必须找回原工作簿，不能重复渲染上传。");
        using (var export = await client.GetAsync($"{path}/reports/{report.Id:D}/download"))
        {
            Assert.AreEqual(HttpStatusCode.OK, export.StatusCode, await export.Content.ReadAsStringAsync());
            using var zip = new ZipArchive(new MemoryStream(await export.Content.ReadAsByteArrayAsync()));
            using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
            StringAssert.Contains(await reader.ReadToEndAsync(), "'=delivery");
        }
        using (var print = await client.PostAsync($"{path}/print", null))
        {
            Assert.AreEqual(HttpStatusCode.OK, print.StatusCode, await print.Content.ReadAsStringAsync());
            var preview = (await print.Content.ReadFromJsonAsync<EnterpriseRequestPrintResponse>())!;
            StringAssert.Contains(preview.Html, "&lt;probe&gt;");
        }

        // 未记账重跑应用迁移必须保留现有文件绑定。
        var migrationName = typeof(EnterpriseRequestModule).Assembly.GetManifestResourceNames().Single(n => n.Contains(
            $".Migrations.{(provider == DatabaseProvider.SqlServer ? "SqlServer" : "MySql")}.002_EnterpriseRequestDelivery.sql", StringComparison.Ordinal));
        if (provider == DatabaseProvider.SqlServer)
            await db.ExecuteAsync("DROP INDEX IX_demo_enterprise_request_delivery_Phase_DueAtUtc_TenantId ON demo_enterprise_request_delivery");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM SchemaVersions WHERE ScriptName=@Name"
            : "DELETE FROM schemaversions WHERE scriptname=@Name", new { Name = migrationName });
        var upgrader = provider == DatabaseProvider.SqlServer ? DeployChanges.To.SqlDatabase(cs) : DeployChanges.To.MySqlDatabase(cs);
        var result = upgrader.WithScriptsEmbeddedInAssembly(typeof(EnterpriseRequestModule).Assembly, n => n == migrationName).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.Message);
        Assert.AreEqual(3, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM demo_enterprise_request_delivery WHERE FileId IS NOT NULL AND RequestId=@Id", new { request.Id }));

        // 同一文件身份在其他租户作用域不可见；不能只检查随机文件是否存在。
        await using (var otherScope = factory.Services.CreateAsyncScope())
        {
            var writer = otherScope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            writer.SetTenant(new TenantContext(Guid.CreateVersion7(), "other-test", "Other test tenant"));
            try { Assert.IsNull(await otherScope.ServiceProvider.GetRequiredService<EnterpriseRequestDeliveryProcessor>().FindAsync(intent, default)); }
            finally { writer.Clear(); }
        }

        // 字段授权由真实会话授权器之外的受控撤权点收紧；无金额任务不会在后续放宽时补入金额。
        fault.DenyAmounts = true;
        using var maskedCreated = await client.PostAsJsonAsync($"{path}/reports", new CreateEnterpriseRequestReportRequest(Guid.NewGuid(), version));
        Assert.AreEqual(HttpStatusCode.OK, maskedCreated.StatusCode, await maskedCreated.Content.ReadAsStringAsync());
        var masked = (await maskedCreated.Content.ReadFromJsonAsync<EnterpriseRequestReportResponse>())!;
        Assert.IsFalse(masked.IncludesAmounts);
        await Due(); await worker.RunBatchAsync(default); await WaitPhase(masked.Id, "Ready");
        fault.DenyAmounts = false;
        using (var maskedDownload = await client.GetAsync($"{path}/reports/{masked.Id:D}/download"))
        {
            Assert.AreEqual(HttpStatusCode.OK, maskedDownload.StatusCode, await maskedDownload.Content.ReadAsStringAsync());
            using var zip = new ZipArchive(new MemoryStream(await maskedDownload.Content.ReadAsByteArrayAsync()));
            using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
            Assert.IsFalse((await reader.ReadToEndAsync()).Contains("12.50", StringComparison.Ordinal));
        }
        fault.Opened = () => { fault.DenyAmounts = true; return Task.CompletedTask; };
        using (var deniedAfterOpen = await client.GetAsync($"{path}/reports/{report.Id:D}/download"))
            Assert.AreEqual(HttpStatusCode.Forbidden, deniedAfterOpen.StatusCode);
        fault.DenyAmounts = false;

        // 当前源版本变化后，历史工作簿不可再交付；附件仍按当前对象权限读取。
        await db.ExecuteAsync("UPDATE demo_enterprise_request_enterprise_request SET Version=Version+1 WHERE Id=@Id", new { request.Id });
        using (var stale = await client.GetAsync($"{path}/reports/{report.Id:D}/download")) Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        var rowVersion = await db.ExecuteScalarAsync<long>("SELECT Version FROM demo_enterprise_request_delivery WHERE Id=@Id", new { report.Id });
        using (var cancelled = await client.PostAsJsonAsync($"{path}/reports/{report.Id:D}/cancel", new CancelEnterpriseRequestReportRequest(rowVersion)))
            Assert.AreEqual(HttpStatusCode.OK, cancelled.StatusCode, await cancelled.Content.ReadAsStringAsync());
        using (var unavailable = await client.GetAsync($"{path}/reports/{report.Id:D}/download")) Assert.AreEqual(HttpStatusCode.NotFound, unavailable.StatusCode);
        await Due(); await worker.RunBatchAsync(default); await WaitPhase(report.Id, "Cancelled");
        Assert.IsNull(await db.ExecuteScalarAsync<Guid?>("SELECT FileId FROM demo_enterprise_request_delivery WHERE Id=@Id", new { report.Id }));

        // 文件 I/O 期间的删除必须阻止最终内容交付。
        fault.Opened = () => db.ExecuteAsync("UPDATE demo_enterprise_request_delivery SET Phase='Deleting',DueAtUtc=@Now,Version=Version+1 WHERE Id=@Id",
            new { Id = intent, Now = DateTimeOffset.UtcNow });
        using (var deletedDuringOpen = await client.GetAsync($"{path}/attachments/{intent:D}/download"))
            Assert.AreEqual(HttpStatusCode.NotFound, deletedDuringOpen.StatusCode);
        await Due(); await worker.RunBatchAsync(default); await WaitPhase(intent, "Deleted");

        // 持久任务使用原会话；换一套登录令牌不能让旧会话任务继续派发。
        version = (await client.GetFromJsonAsync<EnterpriseRequestResponse>(path))!.Version;
        using var pendingCreated = await client.PostAsJsonAsync($"{path}/reports", new CreateEnterpriseRequestReportRequest(Guid.NewGuid(), version));
        Assert.AreEqual(HttpStatusCode.OK, pendingCreated.StatusCode, await pendingCreated.Content.ReadAsStringAsync());
        var pending = (await pendingCreated.Content.ReadFromJsonAsync<EnterpriseRequestReportResponse>())!;
        var uploadsBeforeRevocation = fault.Uploads;
        await db.ExecuteAsync("UPDATE fn_identity_refresh_session SET RevokedAtUtc=@Now,Version=Version+1 WHERE UserId=@UserId AND RevokedAtUtc IS NULL", new { UserId = user, Now = DateTimeOffset.UtcNow });
        await Due(); await worker.RunBatchAsync(default); await WaitPhase(pending.Id, "FailedCleanup");
        await Due(); await worker.RunBatchAsync(default); await WaitPhase(pending.Id, "Failed");
        Assert.AreEqual(uploadsBeforeRevocation, fault.Uploads);
        client.DefaultRequestHeaders.Authorization = new("Bearer", await EnterpriseRequestAssertions.LoginAndEnterAcmeTenantAsync(client, default));
        using (var revoked = await client.GetAsync($"{path}/reports/{pending.Id:D}/download")) Assert.AreEqual(HttpStatusCode.NotFound, revoked.StatusCode);
        using var childWrite = await client.PostAsJsonAsync("/api/v1/enterprise_request/enterprise-request-lines", new { });
        Assert.AreEqual(HttpStatusCode.NotFound, childWrite.StatusCode, "独立明细写入口不得绕开主单版本围栏。");

        async Task<HttpResponseMessage> Upload(Guid? retryKey = null, long? retryVersion = null)
        {
            using var content = new MultipartFormDataContent();
            var file = new ByteArrayContent("safe attachment"u8.ToArray()); file.Headers.ContentType = new("text/plain");
            content.Add(file, "file", "evidence.txt");
            return await client.PostAsync($"{path}/attachments?idempotencyKey={retryKey ?? key:D}&requestVersion={retryVersion ?? request.Version}", content);
        }
        Task<int> Due() => db.ExecuteAsync("UPDATE demo_enterprise_request_delivery SET DueAtUtc=@Now WHERE RequestId=@Id", new { request.Id, Now = DateTimeOffset.UtcNow.AddMinutes(-1) });
        async Task WaitPhase(Guid id, string phase)
        {
            for (var i = 0; i < 50; i++)
            {
                if (await db.ExecuteScalarAsync<string>("SELECT Phase FROM demo_enterprise_request_delivery WHERE Id=@Id", new { Id = id }) == phase) return;
                await Task.Delay(200);
            }
            Assert.Fail($"Delivery {id} did not reach {phase}: " + await db.ExecuteScalarAsync<string>("SELECT Phase FROM demo_enterprise_request_delivery WHERE Id=@Id", new { Id = id }));
        }
    }

    private sealed class UploadFault { public bool LoseNext; public bool DenyAmounts; public int Uploads; public Func<Task>? Opened; }
    private sealed class FieldAuthorization(IBackgroundSessionAuthorization inner, UploadFault fault) : IBackgroundSessionAuthorization
    {
        public Task<AuthorizedSessionActor?> AuthorizeAsync(SessionBindingSnapshot binding, string permissionCode, CancellationToken cancellationToken = default) =>
            fault.DenyAmounts && permissionCode == EnterpriseRequestDeliveryPermissions.AmountsRead ? Task.FromResult<AuthorizedSessionActor?>(null)
                : inner.AuthorizeAsync(binding, permissionCode, cancellationToken);
        public Task<AuthorizedSessionActor?> AuthorizeAllAsync(SessionBindingSnapshot binding, IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken = default) =>
            fault.DenyAmounts && permissionCodes.Contains(EnterpriseRequestDeliveryPermissions.AmountsRead) ? Task.FromResult<AuthorizedSessionActor?>(null)
                : inner.AuthorizeAllAsync(binding, permissionCodes, cancellationToken);
    }
    private sealed class LostUploadStore(ITenantResourceFileStore inner, UploadFault fault) : ITenantResourceFileStore
    {
        public async Task<Result<TenantResourceFileReference>> UploadAsync(string ownerModuleKey, Guid resourceId, Guid actorUserId, string originalFileName,
            string contentType, Stream content, long length, CancellationToken cancellationToken = default)
        {
            fault.Uploads++;
            var result = await inner.UploadAsync(ownerModuleKey, resourceId, actorUserId, originalFileName, contentType, content, length, cancellationToken);
            if (result.IsSuccess && fault.LoseNext) { fault.LoseNext = false; throw new IOException("Committed file response lost."); }
            return result;
        }
        public async Task<Result<TenantResourceFileContent>> OpenReadyContentAsync(string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default)
        {
            var result = await inner.OpenReadyContentAsync(ownerModuleKey, resourceId, fileId, cancellationToken);
            if (result.IsSuccess && fault.Opened is { } callback) { fault.Opened = null; await callback(); }
            return result;
        }
        public Task<IReadOnlyList<TenantResourceFileReadyItem>> ListReadyAsync(string ownerModuleKey, Guid resourceId, CancellationToken cancellationToken = default) => inner.ListReadyAsync(ownerModuleKey, resourceId, cancellationToken);
        public Task ReleaseAsync(string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default) => inner.ReleaseAsync(ownerModuleKey, resourceId, fileId, cancellationToken);
    }
}
