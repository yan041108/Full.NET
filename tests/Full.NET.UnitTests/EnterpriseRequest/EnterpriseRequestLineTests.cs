using System.Globalization;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Features.ManageLines;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.EnterpriseRequest;

/// <summary>明细金额、组织隔离与主表版本共同保护聚合。</summary>
[TestClass]
public sealed class EnterpriseRequestLineTests
{
    [TestMethod]
    public void Calculates_each_line_before_summing_and_accepts_empty_or_free_lines()
    {
        var result = EnterpriseRequestLineService.Calculate([new("First", 1.0001m, 50m), new("Second", 2.5m, 3.21m), new("Free", 1m, 0m)]);
        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(new[] {50.01m, 8.03m, 0m}, result.Value!.Select(row => row.Amount).ToArray());
        Assert.IsTrue(EnterpriseRequestLineService.Calculate([]).IsSuccess);
        Assert.IsTrue(EnterpriseRequestLineService.Calculate(Enumerable.Repeat(new EnterpriseRequestLineInput("Valid", 1m, 0m), 200).ToArray()).IsSuccess);
    }

    [TestMethod]
    [DataRow("0", "1")]
    [DataRow("-1", "1")]
    [DataRow("0.00001", "1")]
    [DataRow("100000000000000", "1")]
    [DataRow("1", "-1")]
    [DataRow("1", "1.001")]
    [DataRow("1", "10000000000000000")]
    [DataRow("2", "9999999999999999.99")]
    [DataRow("99999999999999.9999", "9999999999999999.99")]
    public void Invalid_precision_range_or_product_fails_without_decimal_overflow(string quantity, string price)
    {
        var result = EnterpriseRequestLineService.Calculate([new("Item", decimal.Parse(quantity, CultureInfo.InvariantCulture), decimal.Parse(price, CultureInfo.InvariantCulture))]);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(ErrorType.Validation, result.Error!.Type);
    }

    [TestMethod]
    public void Rejects_null_blank_long_description_count_and_total_overflow()
    {
        IReadOnlyList<EnterpriseRequestLineInput>?[] invalid = [null, [null!], [new(" ", 1m, 1m)],
            [new(new string('x', 201), 1m, 1m)], Enumerable.Repeat(new EnterpriseRequestLineInput("Item", 1m, 0m), 201).ToArray(),
            [new("Maximum", 1m, 9999999999999999.99m), new("Additional", 1m, 0.01m)]];
        foreach (var items in invalid) Assert.IsFalse(EnterpriseRequestLineService.Calculate(items).IsSuccess);
    }

    [TestMethod]
    public async Task Read_respects_organization_filter_and_does_not_query_lines_of_hidden_parent()
    {
        var f = new Fixture(); f.Row = null;
        var result = await f.Read();
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(EnterpriseRequestErrorCodes.NotFound, result.Error!.Code);
        Assert.AreEqual(0, f.LineReads);
        Assert.IsTrue(f.Queries.ReceivedCalls().Any(call => call.GetArguments()[0] is SqlStatement sql && sql.Text.Contains("OrganizationUnitId = @LineUnit", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("version")]
    [DataRow("tenant")]
    [DataRow("request")]
    public async Task Read_rejects_changed_parent_or_wrong_line_identity(string mismatch)
    {
        var f = new Fixture();
        f.ReadLines = () => {
            if (mismatch == "version") f.Row = f.Row! with { Version = 2 };
            return new[] { f.Line with { TenantId = mismatch == "tenant" ? Guid.NewGuid() : f.TenantId,
                RequestId = mismatch == "request" ? Guid.NewGuid() : f.Id } };
        };
        var result = await f.Read(); Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(EnterpriseRequestErrorCodes.VersionConflict, result.Error!.Code);
    }

    [TestMethod]
    [DataRow("host")]
    [DataRow("actor")]
    [DataRow("request")]
    public async Task Invalid_context_rejects_before_read_or_write(string kind)
    {
        var f = new Fixture(); if (kind == "host") f.Tenant.IsHost.Returns(true);
        var id = kind == "request" ? Guid.Empty : f.Id; var actor = kind == "actor" ? Guid.Empty : f.Actor;
        Assert.IsFalse((await f.Service.GetAsync(id, actor, true)).IsSuccess);
        Assert.IsFalse((await f.Service.ReplaceAsync(id, f.Input, actor)).IsSuccess);
        Assert.AreEqual(0, f.Queries.ReceivedCalls().Count()); Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("Submitted")]
    [DataRow("Approved")]
    [DataRow("Rejected")]
    [DataRow("Cancelled")]
    public async Task Non_draft_can_be_read_but_not_changed(string status)
    {
        var f = new Fixture(); f.Row = f.Row! with { Status = status };
        Assert.IsTrue((await f.Read()).IsSuccess);
        var result = await f.Write(); Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(EnterpriseRequestWorkflowErrorCodes.InvalidStatus, result.Error!.Code);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(2L)]
    public async Task Stale_or_predicted_future_version_does_not_write(long version)
    {
        var f = new Fixture(); var result = await f.Service.ReplaceAsync(f.Id, f.Input with { Version = version }, f.Actor);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(EnterpriseRequestErrorCodes.VersionConflict, result.Error!.Code);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Organization_write_denial_rejects_before_transaction()
    {
        var f = new Fixture(); f.Authorizer.EnsureCanWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(false));
        var result = await f.Write(); Assert.IsFalse(result.IsSuccess); Assert.AreEqual(ErrorType.Forbidden, result.Error!.Type);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count()); Assert.AreEqual(0, f.Coordinator.CommitCount);
    }

    [TestMethod]
    public async Task Parent_cas_conflict_never_deletes_or_inserts_lines()
    {
        var f = new Fixture(); f.Commands.ExecuteAsync(EnterpriseRequestLineSql.UpdateParent, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        Assert.IsFalse((await f.Write()).IsSuccess);
        Assert.AreEqual(1, f.Commands.ReceivedCalls().Count()); Assert.AreEqual(1, f.Coordinator.RollbackCount);
    }

    [TestMethod]
    public async Task Insert_failure_rolls_back_parent_update_and_line_deletion()
    {
        var f = new Fixture(); f.Commands.ExecuteAsync(EnterpriseRequestLineSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<int>(new InvalidOperationException("probe")));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Write());
        Assert.AreEqual(3, f.Commands.ReceivedCalls().Count()); Assert.AreEqual(1, f.Coordinator.RollbackCount); Assert.AreEqual(0, f.Coordinator.CommitCount);
    }

    [TestMethod]
    public async Task Replacement_updates_total_version_and_numbers_in_single_transaction()
    {
        var f = new Fixture(); var result = await f.Write();
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(2L, result.Value!.RequestVersion); Assert.AreEqual(50.01m, result.Value.TotalAmount);
        Assert.AreEqual(1, result.Value.Items[0].LineNumber); Assert.AreEqual("Item", result.Value.Items[0].ItemDescription);
        Assert.AreEqual(3, f.Commands.ReceivedCalls().Count()); Assert.AreEqual(1, f.Coordinator.CommitCount);
    }

    [TestMethod]
    public async Task Cancelled_request_does_not_access_database()
    {
        var f = new Fixture(); using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Service.GetAsync(f.Id, f.Actor, false, source.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Service.ReplaceAsync(f.Id, f.Input, f.Actor, source.Token));
        Assert.AreEqual(0, f.Queries.ReceivedCalls().Count());
    }

    private sealed class Fixture
    {
        internal readonly Guid Id = Guid.NewGuid(), Actor = Guid.NewGuid(), Unit = Guid.NewGuid(), TenantId = Guid.NewGuid();
        internal readonly IQueryExecutor Queries = Substitute.For<IQueryExecutor>();
        internal readonly ICommandExecutor Commands = Substitute.For<ICommandExecutor>();
        internal readonly ICurrentTenant Tenant = Substitute.For<ICurrentTenant>();
        internal readonly IOrganizationOwnedEntityWriteAuthorizer Authorizer = Substitute.For<IOrganizationOwnedEntityWriteAuthorizer>();
        internal readonly RecordingDbTransactionCoordinator Coordinator = new();
        internal EnterpriseRequestRecord? Row;
        internal readonly EnterpriseRequestLineRow Line;
        internal readonly EnterpriseRequestLineService Service;
        internal Func<IReadOnlyList<EnterpriseRequestLineRow>> ReadLines;
        internal int LineReads;
        internal readonly ReplaceEnterpriseRequestLinesRequest Input = new(1, [new(" Item ", 1.0001m, 50m)]);
        internal Fixture()
        {
            Tenant.IsAvailable.Returns(true); Tenant.Id.Returns(TenantId);
            Row = new(Id, TenantId, Unit, "REQ", "Request", "Draft", 1m, Actor, 1, DateTimeOffset.UtcNow, Actor, null, null, false, null, null);
            Line = new(Guid.NewGuid(), TenantId, Id, 1, "Item", 1m, 1m, 1m);
            Queries.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Row);
            ReadLines = () => [Line];
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Authorizer.EnsureCanWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
            var scopes = Substitute.For<IUserDataScopeResolver>(); scopes.ResolveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new EffectiveUserDataScope(false, []));
            var filters = Substitute.For<IDataScopeSqlFilterBuilder>(); filters.BuildOrganizationUnitFilter(Arg.Any<EffectiveUserDataScope>(), "OrganizationUnitId", Actor)
                .Returns(new DataScopeSqlFilter("OrganizationUnitId = @LineUnit", new Dictionary<string, object?> { ["LineUnit"] = Unit }));
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.NewGuid());
            Service = new(new EnterpriseRequestQueryService(Queries, Options.Create(new DatabaseOptions()), scopes, filters), new LineQueries(Queries, this), Commands,
                new DapperCommandTransaction(Coordinator), Tenant, Authorizer, ids, Substitute.For<IClock>());
        }
        internal Task<Result<EnterpriseRequestLinesResponse>> Read() => Service.GetAsync(Id, Actor, false);
        internal Task<Result<EnterpriseRequestLinesResponse>> Write() => Service.ReplaceAsync(Id, Input, Actor);
    }

    // 避免代理库为内部行投影的 IReadOnlyList 自动生成不可访问的代理；不扩大生产类型可见性。
    private sealed class LineQueries(IQueryExecutor inner, Fixture fixture) : IQueryExecutor
    {
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (typeof(T) != typeof(EnterpriseRequestLineRow)) return inner.QueryAsync<T>(statement, parameters, cancellationToken);
            Assert.AreEqual(EnterpriseRequestLineSql.Read, statement);
            fixture.LineReads++;
            return Task.FromResult((IReadOnlyList<T>)(object)fixture.ReadLines());
        }
    }
}
