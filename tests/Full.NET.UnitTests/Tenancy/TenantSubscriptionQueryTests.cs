using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions;
using Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

/// <summary>状态筛选在读取前校验，并与总数、提供程序分页使用同一组参数。</summary>
[TestClass]
public sealed class TenantSubscriptionQueryTests
{
    [TestMethod]
    public async Task Get_binds_original_tenant_and_id_and_preserves_terminal_history()
    {
        var queries = Substitute.For<IQueryExecutor>(); var tenant = Guid.CreateVersion7(); var id = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        queries.QuerySingleOrDefaultAsync<TenantSubscriptionRecord>(TenantSubscriptionSql.FindById,
            Arg.Is<Dictionary<string, object?>>(p => (Guid)p!["TenantId"]! == tenant && (Guid)p["Id"]! == id), Arg.Any<CancellationToken>())
            .Returns(new TenantSubscriptionRecord(id, tenant, null, "Cancelled", null, now.AddDays(-30), now, now, 7));
        var result = await new TenantSubscriptionQueryService(queries).GetAsync(tenant, id);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(id, result.Value!.Id); Assert.AreEqual(tenant, result.Value.TenantId);
        Assert.AreEqual("Cancelled", result.Value.Status); Assert.AreEqual(7, result.Value.Version); Assert.AreEqual(now, result.Value.CancelledAtUtc);
    }

    [TestMethod]
    public async Task Get_returns_explicit_not_found_when_original_record_is_absent()
    {
        var result = await new TenantSubscriptionQueryService(Substitute.For<IQueryExecutor>()).GetAsync(Guid.CreateVersion7(), Guid.CreateVersion7());
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(TenancyErrorCodes.SubscriptionNotFound, result.Error!.Code);
    }

    [TestMethod]
    [DataRow("Trial")]
    [DataRow("Active")]
    [DataRow("PastDue")]
    [DataRow("Cancelled")]
    [DataRow("Expired")]
    [DataRow(" ")]
    public async Task List_passes_normalized_status_to_both_reads(string status)
    {
        var queries = Substitute.For<IQueryExecutor>();
        queries.QueryAsync<TenantSubscriptionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<TenantSubscriptionRecord>());
        var id = Guid.CreateVersion7(); var service = new TenantSubscriptionQueryService(queries);
        var result = await service.ListAsync(id, page: 2, pageSize: 20, status: " " + status + " ");
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(2, result.Value!.Page);
        var calls = queries.ReceivedCalls().ToArray(); Assert.AreEqual(2, calls.Length);
        foreach (var call in calls)
        {
            var parameters = (Dictionary<string, object?>)call.GetArguments()[1]!;
            Assert.AreEqual(id, parameters["TenantId"]); Assert.AreEqual(20L, parameters["Offset"]);
            Assert.AreEqual(string.IsNullOrWhiteSpace(status) ? null : status, parameters["Status"]);
        }
    }

    [TestMethod]
    [DataRow("active")]
    [DataRow("Deleted")]
    [DataRow("Active' OR 1=1--")]
    public async Task List_rejects_unknown_status_before_query(string status)
    {
        var queries = Substitute.For<IQueryExecutor>();
        var result = await new TenantSubscriptionQueryService(queries).ListAsync(Guid.CreateVersion7(), status: status);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(TenancyErrorCodes.SubscriptionStatusInvalid, result.Error!.Code);
        Assert.AreEqual(0, queries.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task List_keeps_provider_paging_and_safe_bounds(DatabaseProvider provider)
    {
        var queries = Substitute.For<IQueryExecutor>();
        queries.QueryAsync<TenantSubscriptionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<TenantSubscriptionRecord>());
        var result = await new TenantSubscriptionQueryService(queries, Options.Create(new DatabaseOptions { Provider = provider }))
            .ListAsync(Guid.CreateVersion7(), page: 0, pageSize: 1000);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(1, result.Value!.Page); Assert.AreEqual(100, result.Value.PageSize);
        await queries.Received(1).QueryAsync<TenantSubscriptionRecord>(provider == DatabaseProvider.MySql ? TenantSubscriptionSql.ListPageMySql : TenantSubscriptionSql.ListPageSqlServer,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }
}
