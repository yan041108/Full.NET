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
