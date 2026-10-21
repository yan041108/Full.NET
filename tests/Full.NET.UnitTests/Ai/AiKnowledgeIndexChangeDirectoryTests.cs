using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>目录只读展示最新来源；授权等待、范围变化及有界分页不能扩大数据访问。</summary>
[TestClass]
public sealed class AiKnowledgeIndexChangeDirectoryTests
{
    [TestMethod]
    [DataRow(false, DatabaseProvider.SqlServer)]
    [DataRow(true, DatabaseProvider.SqlServer)]
    [DataRow(false, DatabaseProvider.MySql)]
    [DataRow(true, DatabaseProvider.MySql)]
    public async Task Directory_returns_one_bounded_page_and_a_document_cursor(bool scoped, DatabaseProvider provider)
    {
        var f = new Fixture(scoped, provider);
        var cursor = Guid.CreateVersion7();
        var result = await f.Service.ListAsync(f.KnowledgeId, f.Actor, cursor, 2, CancellationToken.None);
        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(f.Query.Rows.Take(2).ToArray(), result.Value!.Items.ToArray());
        Assert.AreEqual(f.Query.Rows[1].DocumentId, result.Value.NextDocumentId);
        Assert.AreEqual(4, f.Query.Calls);
        var parameters = (Dictionary<string, object?>)f.Query.Parameters!;
        Assert.AreEqual(3, parameters["Take"]);
        Assert.AreEqual(cursor, parameters["AfterDocumentId"]);
        Assert.AreEqual(1, parameters["HasCursor"]);
        Assert.IsFalse(parameters.ContainsKey("TenantId"));
        Assert.AreEqual(scoped ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, f.Query.PageStatement!.Scope);
        Assert.IsTrue(f.Query.PageStatement.Name.Contains(provider == DatabaseProvider.MySql ? "mysql" : "sqlserver", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(false, "empty")]
    [DataRow(true, "empty")]
    [DataRow(false, "deny")]
    [DataRow(true, "deny")]
    [DataRow(false, "revoke")]
    [DataRow(true, "revoke")]
    [DataRow(false, "scope")]
    [DataRow(true, "scope")]
    [DataRow(false, "missing")]
    [DataRow(true, "missing")]
    [DataRow(false, "changed")]
    [DataRow(true, "changed")]
    [DataRow(false, "source_changed")]
    [DataRow(true, "source_changed")]
    [DataRow(false, "late_owner_scope")]
    [DataRow(true, "late_owner_scope")]
    [DataRow(false, "late_page_scope")]
    [DataRow(true, "late_page_scope")]
    [DataRow(false, "late_missing")]
    [DataRow(true, "late_missing")]
    public async Task Directory_rechecks_authority_and_does_not_return_stale_rows(bool scoped, string scenario)
    {
        var f = new Fixture(scoped, DatabaseProvider.SqlServer, scenario);
        var result = await f.Service.ListAsync(f.KnowledgeId, f.Actor, null, 2, CancellationToken.None);
        if (scenario == "empty")
        {
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(0, result.Value!.Items.Count);
            Assert.IsNull(result.Value.NextDocumentId);
        }
        else
        {
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(scenario switch { "missing" or "late_missing" => AiKnowledgeErrorCodes.NotFound,
                "changed" or "source_changed" => AiKnowledgeErrorCodes.VersionConflict, _ => "authorization.permission_denied" }, result.Error!.Code);
        }
        if (scenario == "deny") Assert.AreEqual(0, f.Query.Calls);
    }

    [TestMethod]
    [DataRow("too_many")]
    [DataRow("duplicate")]
    [DataRow("bad_id")]
    [DataRow("bad_version")]
    [DataRow("bad_title")]
    [DataRow("null_row")]
    public async Task Directory_rejects_invalid_database_projection(string scenario)
    {
        var f = new Fixture(false, DatabaseProvider.SqlServer);
        f.Query.Rows = scenario switch
        {
            "too_many" => [..f.Query.Rows, f.Query.Rows[0]],
            "duplicate" => [f.Query.Rows[0], f.Query.Rows[0]],
            "bad_id" => [f.Query.Rows[0] with { DocumentId = Guid.Empty }],
            "bad_version" => [f.Query.Rows[0] with { VersionNumber = 0 }],
            "null_row" => [null!],
            _ => [f.Query.Rows[0] with { Title = "" }]
        };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Service.ListAsync(f.KnowledgeId, f.Actor, null, 2, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(51)]
    public async Task Directory_rejects_invalid_page_size_before_database_work(int size)
    {
        var f = new Fixture(false, DatabaseProvider.MySql);
        Assert.IsFalse((await f.Service.ListAsync(f.KnowledgeId, f.Actor, null, size, CancellationToken.None)).IsSuccess);
        Assert.AreEqual(0, f.Query.Calls);
    }

    [TestMethod]
    public async Task Directory_honors_cancellation_and_rejects_ambient_transactions_and_empty_cursors()
    {
        var f = new Fixture(false, DatabaseProvider.MySql);
        Assert.IsFalse((await f.Service.ListAsync(f.KnowledgeId, f.Actor, Guid.Empty, 2, CancellationToken.None)).IsSuccess);
        f.Transactions.HasTransaction.Returns(true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Service.ListAsync(f.KnowledgeId, f.Actor, null, 2, CancellationToken.None));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Service.ListAsync(f.KnowledgeId, f.Actor, null, 2, cancelled.Token));
        Assert.AreEqual(0, f.Query.Calls);
    }

    [TestMethod]
    [DataRow("owner")]
    [DataRow("page")]
    public async Task Directory_cancels_during_final_authoritative_reads(string stage)
    {
        using var cancellation = new CancellationTokenSource(); var f = new Fixture(false, DatabaseProvider.SqlServer);
        f.Query.AfterOwnerRead = () => { if (stage == "owner" && f.Query.PageRead) cancellation.Cancel(); };
        f.Query.AfterRead = () => { if (stage == "page" && f.Query.PageReads == 2) cancellation.Cancel(); };
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Service.ListAsync(f.KnowledgeId, f.Actor, null, 2, cancellation.Token));
    }

    private sealed class Fixture
    {
        internal Guid KnowledgeId { get; } = Guid.CreateVersion7();
        internal Guid Actor { get; } = Guid.CreateVersion7();
        internal DirectoryQuery Query { get; }
        internal IDataTransactionState Transactions { get; } = Substitute.For<IDataTransactionState>();
        internal AiKnowledgeIndexChangeService Service { get; }

        internal Fixture(bool scoped, DatabaseProvider provider, string scenario = "normal")
        {
            var tenant = new CurrentTenantAccessor();
            if (scoped) tenant.SetTenant(new(Guid.CreateVersion7(), "acme", "Acme")); else tenant.SetHost();
            Query = new DirectoryQuery(scenario);
            void ChangeScope() => tenant.SetTenant(new(Guid.CreateVersion7(), "other", "Other"));
            Query.AfterRead = () => { if (scenario == "scope" || scenario == "late_page_scope" && Query.PageReads == 2) ChangeScope(); };
            Query.AfterOwnerRead = () => { if (scenario == "late_owner_scope" && Query.PageRead) ChangeScope(); };
            var auth = Substitute.For<ICurrentSessionAuthorization>(); auth.UsePermissionSetChecks();
            auth.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => scenario == "deny" || scenario == "revoke" && Query.PageRead
                ? null : new AuthorizedSessionActor(Actor, tenant.Id, Guid.CreateVersion7()));
            var guard = new AiKnowledgePermissionGuard(auth, Substitute.For<IHttpContextAccessor>(), Substitute.For<ICurrentApiKeyAuthorization>());
            Service = new(Query, Transactions, tenant, guard, Options.Create(new DatabaseOptions { Provider = provider }));
        }
    }

    // 具体替身直接返回生产 DTO，避免为内部服务扩大动态代理可见性。
    private sealed class DirectoryQuery(string scenario) : IQueryExecutor
    {
        internal AiKnowledgeIndexChangeItem[] Rows { get; set; } = Enumerable.Range(1, 3).Select(n => new AiKnowledgeIndexChangeItem(
            Guid.CreateVersion7(), "Document " + n, Guid.CreateVersion7(), n, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)).ToArray();
        internal bool PageRead { get; private set; }
        internal int PageReads { get; private set; }
        internal int Calls { get; private set; }
        internal object? Parameters { get; private set; }
        internal SqlStatement? PageStatement { get; private set; }
        internal Action? AfterRead { get; set; }
        internal Action? AfterOwnerRead { get; set; }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Calls++; PageReads++; var previousRead = PageRead; PageRead = true; Parameters = parameters; PageStatement = statement; AfterRead?.Invoke();
            AiKnowledgeIndexChangeItem[] result = scenario == "empty" ? [] : scenario == "source_changed" && previousRead ? new[] { Rows[0] with { VersionId = Guid.CreateVersion7() } } : Rows;
            return Task.FromResult((IReadOnlyList<T>)(object)result);
        }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        { Calls++; AfterOwnerRead?.Invoke(); return Task.FromResult((T?)(object)(scenario == "missing" || scenario == "late_missing" && PageRead ? 0 : scenario == "changed" && PageRead ? 6 : 5)); }
    }
}
