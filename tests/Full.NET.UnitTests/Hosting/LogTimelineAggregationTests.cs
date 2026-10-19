using System.Security.Claims;
using Full.NET.Abstractions.Observability;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modularity.Observability;
using Full.NET.Modules.Auditing.Features.QueryLogTimeline;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Hosting;

/// <summary>验证跨来源聚合不会将未授权、失败与空结果混为一谈。</summary>
[TestClass]
public sealed class LogTimelineAggregationTests
{
    private static LogTimelineQuery Query() => new(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow,
        new string('a', 32), null, null, null, 100);

    [TestMethod]
    public async Task Aggregation_sorts_event_time_and_reports_unauthorized_and_failed_sources()
    {
        var query = Query();
        var denied = new Reader("denied", _ => throw new AssertFailedException("未授权来源不能被调用。"));
        var failed = new Reader("failed", _ => throw new IOException("private server path"));
        var first = new Reader("first", _ => Task.FromResult(new LogTimelineSourceResult(
            [new("1", "first", query.FromUtc.AddSeconds(2), "safe", new())], "available")));
        var second = new Reader("second", _ => Task.FromResult(new LogTimelineSourceResult(
            [new("2", "second", query.FromUtc.AddSeconds(1), "safe", new())], "available")));
        var tenant = new CurrentTenantAccessor(); ((ICurrentTenantContextWriter)tenant).SetHost();
        var service = new LogTimelineQueryService([denied, first, failed, second], new Authorization(), tenant);
        var response = await service.QueryAsync(query, new ClaimsPrincipal(), CancellationToken.None);
        Assert.IsTrue(response.IsSuccess); Assert.IsNotNull(response.Value);
        CollectionAssert.AreEqual(new[] { "2", "1" }, response.Value.Entries.Select(entry => entry.Id).ToArray());
        Assert.IsTrue(response.Value.Partial);
        Assert.AreEqual("forbidden", response.Value.Sources[0].Status);
        Assert.AreEqual("unavailable", response.Value.Sources[2].Status);
    }

    [TestMethod]
    public async Task Aggregation_propagates_client_cancellation()
    {
        var tenant = new CurrentTenantAccessor(); ((ICurrentTenantContextWriter)tenant).SetHost();
        var service = new LogTimelineQueryService([new Reader("source", _ => throw new AssertFailedException())],
            new Authorization(), tenant);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await service.QueryAsync(Query(), new ClaimsPrincipal(), cancellation.Token));
    }

    [TestMethod]
    public async Task Database_reader_preserves_legacy_session_columns_when_snapshot_is_absent()
    {
        var session = Guid.NewGuid();
        var row = new LogTimelineRow { Id = Guid.NewGuid(), OccurredAtUtc = DateTimeOffset.UtcNow,
            SessionId = session, CenterSessionId = Guid.NewGuid(), ApplicationSessionId = Guid.NewGuid() };
        var statement = new SqlStatement("test.timeline", "SELECT 1", SqlDataScope.HostOnly);
        var reader = new SqlLogTimelineReader(new Queries(row), Options.Create(new DatabaseOptions
            { Provider = DatabaseProvider.SqlServer }), "identity", "permission", statement, statement);
        var result = await reader.ReadAsync(Query(), CancellationToken.None);
        Assert.AreEqual(session, result.Entries[0].Correlation.SessionId);
        Assert.AreEqual(row.CenterSessionId, result.Entries[0].Correlation.CenterSessionId);
        Assert.AreEqual(row.ApplicationSessionId, result.Entries[0].Correlation.ApplicationSessionId);
    }

    private sealed class Reader(string name, Func<CancellationToken, Task<LogTimelineSourceResult>> read) : ILogTimelineReader
    {
        public string Source => name;
        public string RequiredPermission => name;
        public Task<LogTimelineSourceResult> ReadAsync(LogTimelineQuery query, CancellationToken cancellationToken) => read(cancellationToken);
    }
    private sealed class Authorization : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            Task.FromResult(policyName.EndsWith("denied", StringComparison.Ordinal) ? AuthorizationResult.Failed() : AuthorizationResult.Success());
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            throw new NotSupportedException();
    }
    private sealed class Queries(LogTimelineRow row) : IQueryExecutor
    {
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>([(T)(object)row]);
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
