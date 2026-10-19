using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Observability;
using Full.NET.Abstractions.Tenancy;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Auditing.Features.QueryLogTimeline;
using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Auditing;

/// <summary>双库真实执行全部来源 SQL，同时验证权限、关联筛选、发生时间和安全摘要。</summary>
internal static class AuditingLogTimelineAssertions
{
    public static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var readers = scope.ServiceProvider.GetServices<ILogTimelineReader>().ToArray();
        var trace = Guid.NewGuid().ToString("N");
        var legacyTrace = Guid.NewGuid().ToString("N");
        var session = Guid.NewGuid(); var user = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow.AddMinutes(-2);
        var correlation = new LogCorrelationSnapshot { TraceId = trace, SessionId = session, UserId = user,
            ClientIpFingerprint = new string('c', 64) };
        var writer = scope.ServiceProvider.GetRequiredService<AuditWriteBatchWriter>();
        var first = AuditWriteEnvelope.ForOperation(new OperationLogWriteModel("timeline.first", "POST", "/probe", 200,
            1, true, user, null, legacyTrace, new string('c', 64), null)
            { CapturedAtUtc = start, CorrelationJson = correlation.ToJson() });
        var second = AuditWriteEnvelope.ForOperation(new OperationLogWriteModel("timeline.second", "POST", "/probe", 200,
            1, true, user, null, trace, new string('c', 64), null)
            { CapturedAtUtc = start.AddSeconds(1), CorrelationJson = correlation.ToJson() });
        await writer.WriteMicroBatchAsync([second, first], CancellationToken.None);
        Assert.IsTrue((await first.Completion.Task).Succeeded);
        Assert.IsTrue((await second.Completion.Task).Succeeded);
        var query = new LogTimelineQuery(start.AddMinutes(-1), start.AddMinutes(2), trace, session, user, new string('c', 64), 100);
        // 直接执行每个固定查询，避免聚合层的 unavailable 掩盖 SQL 或迁移缺陷。
        foreach (var reader in readers.Where(reader => !reader.Source.StartsWith("runtime.", StringComparison.Ordinal)))
        {
            var source = await reader.ReadAsync(query, CancellationToken.None);
            Assert.AreEqual("available", source.Status, reader.Source);
        }
        var url = "/api/v1/auditing/timeline?traceId=" + trace
            + "&fromUtc=" + Uri.EscapeDataString(query.FromUtc.ToString("O"))
            + "&toUtc=" + Uri.EscapeDataString(query.ToUtc.ToString("O"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await factory.CreateHostAccessTokenAsync(["platform.dashboard.read"]));
        using (var denied = await client.GetAsync(url)) Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await factory.CreateHostAccessTokenAsync([LogTimelinePermissions.Read, "auditing.operations.read"]));
        var response = await client.GetFromJsonAsync<LogTimelineResponse>(url + "&sessionId=" + session + "&userId=" + user
            + "&clientIpFingerprint=" + new string('c', 64));
        Assert.IsNotNull(response);
        Assert.AreEqual(2, response.Entries.Count);
        CollectionAssert.AreEqual(new[] { "timeline.first", "timeline.second" }, response.Entries.Select(entry => entry.Summary).ToArray());
        Assert.AreEqual(session, response.Entries[0].Correlation.SessionId);
        Assert.AreEqual(start.ToUnixTimeMilliseconds(), response.Entries[0].OccurredAtUtc.ToUnixTimeMilliseconds());
        Assert.IsTrue(response.Sources.Any(source => source.Status == "forbidden"));
        Assert.IsTrue(response.Partial);
        var oldTraceMismatch = await client.GetFromJsonAsync<LogTimelineResponse>(url.Replace(trace, legacyTrace, StringComparison.Ordinal));
        Assert.IsNotNull(oldTraceMismatch);
        Assert.AreEqual(0, oldTraceMismatch.Entries.Count);
        var mismatch = await client.GetFromJsonAsync<LogTimelineResponse>(url + "&sessionId=" + Guid.NewGuid());
        Assert.IsNotNull(mismatch);
        Assert.AreEqual(0, mismatch.Entries.Count);
        using var invalid = await client.GetAsync("/api/v1/auditing/timeline?userId=" + user);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
