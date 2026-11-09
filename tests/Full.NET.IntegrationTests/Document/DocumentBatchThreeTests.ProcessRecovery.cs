using System.Net;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.EnterpriseRequest;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.PreviewTasks;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Document;

public sealed partial class DocumentBatchThreeTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Office_process_crash_exhausts_lease_releases_output_and_allows_explicit_retry(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var evidence = Directory.CreateTempSubdirectory("fullnet-office-process-");
        try
        {
            using var api = new FullNetApiFactory(provider, connection, new Dictionary<string, string?>
            {
                ["Files:Local:RootPath"] = Path.Combine(evidence.FullName, "files"),
                ["DataProtection:KeyRingPath"] = Path.Combine(evidence.FullName, "keys"),
                ["Document:OfficePreviewConversion:Enabled"] = "true",
                ["Document:OfficePreviewConversion:ProviderKey"] = "external_http",
                ["Document:OfficePreviewConversion:ExternalHttp:BaseUrl"] = "https://example.test/convert",
                ["Document:OfficePreviewConversion:MaxAttempts"] = "1",
                ["Document:OfficePreviewConversion:TimeoutSeconds"] = "15"
            });
            await api.InitializeAsync();
            using var client = api.CreateClientForHost("localhost");
            var actor = await api.CreateHostIdentityAsync($"office-process-{Guid.NewGuid():N}", Permissions);
            var item = await CreateOfficeAsync(client, actor.AccessToken);
            var created = await JsonAsync<HostDocumentPreviewTaskResponse>(client, actor.AccessToken, HttpMethod.Post, Tasks,
                new CreateHostDocumentPreviewTaskRequest(item.Id, item.CurrentVersion!.Id), HttpStatusCode.Created);
            var originalPath = Tasks + $"/{created.Id:D}";
            var converted = Path.Combine(evidence.FullName, "conversion.gate");
            var replacementCalls = Path.Combine(evidence.FullName, "replacement.calls");
            await using var scope = api.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var lifecycle = scope.ServiceProvider.GetRequiredService<DocumentPreviewLifecycle>();
            var queries = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();

            int originalPid;
            DateTimeOffset leaseExpires;
            await using (var first = await EnterpriseRequestWorkerProcess.StartAsync(api, evidence.FullName, "office-conversion", converted, 1))
            {
                originalPid = first.Id;
                await UntilAsync(() => Task.FromResult(File.Exists(converted)), 35);
                var pending = (await lifecycle.FindAsync(created.Id, default))!;
                Assert.AreEqual("processing", pending.StatusKey);
                Assert.IsNull(pending.OutputFileId);
                Assert.AreEqual(1, pending.AttemptCount);
                Assert.IsNotNull(pending.LeaseExpiresAtUtc);
                leaseExpires = pending.LeaseExpiresAtUtc.Value;
                await first.CrashAsync();
            }

            await using var replacement = await EnterpriseRequestWorkerProcess.StartAsync(api, evidence.FullName, "office-recovery", replacementCalls, 2);
            Assert.AreNotEqual(originalPid, replacement.Id);
            var untilLeaseExpiry = Math.Max(0, (int)Math.Ceiling((leaseExpires - DateTimeOffset.UtcNow).TotalSeconds));
            await UntilAsync(async () => (await lifecycle.FindAsync(created.Id, default))!.StatusKey == "failed", untilLeaseExpiry + 20);
            var failed = await JsonAsync<HostDocumentPreviewTaskResponse>(client, actor.AccessToken, HttpMethod.Get, originalPath);
            Assert.AreEqual("document.preview.attempts_exhausted", failed.ErrorCode);
            Assert.IsTrue(failed.CompletedAtUtc >= leaseExpires);
            Assert.IsNull(failed.OutputFileId);
            Assert.IsFalse(File.Exists(replacementCalls));
            Assert.AreEqual(1L, await CountAsync(queries, "fn_document_preview_attempt",
                "TaskId = @Id AND ErrorCode = @ErrorCode AND CompletedAtUtc IS NOT NULL",
                ("Id", created.Id), ("ErrorCode", "document.preview.lease_expired")));

            var retry = await JsonAsync<HostDocumentPreviewTaskResponse>(client, actor.AccessToken, HttpMethod.Post, originalPath + "/retry");
            Assert.AreNotEqual(created.Id, retry.Id);
            Assert.AreEqual(created.VersionId, retry.VersionId);
            Assert.AreEqual(created.SourceFileId, retry.SourceFileId);
            await UntilAsync(async () => (await lifecycle.FindAsync(retry.Id, default))!.StatusKey == "succeeded", 40);
            using var content = await SendAsync(client, actor.AccessToken, HttpMethod.Get, Tasks + $"/{retry.Id:D}/content");
            Assert.AreEqual(HttpStatusCode.OK, content.StatusCode);
            CollectionAssert.AreEqual(Pdf, await content.Content.ReadAsByteArrayAsync());
            Assert.HasCount(1, await File.ReadAllLinesAsync(replacementCalls));

            var failedRecord = (await lifecycle.FindAsync(created.Id, default))!;
            var untilOutputRelease = Math.Max(0, (int)Math.Ceiling(((failedRecord.LeaseExpiresAtUtc ?? DateTimeOffset.UtcNow)
                - DateTimeOffset.UtcNow).TotalSeconds));
            await UntilAsync(async () => (await lifecycle.ReferencesAsync(created.Id, default))
                .Any(reference => reference.Purpose == "output" && reference.StateKey == "released"), untilOutputRelease + 20);
            var references = await lifecycle.ReferencesAsync(created.Id, default);
            Assert.AreEqual("claimed", references.Single(reference => reference.Purpose == "source").StateKey);
            Assert.AreEqual("failed", (await lifecycle.FindAsync(created.Id, default))!.StatusKey);
        }
        finally { evidence.Delete(recursive: true); }

        static async Task UntilAsync(Func<Task<bool>> condition, int seconds)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            while (!await condition()) await Task.Delay(100, timeout.Token);
        }
    }
}
