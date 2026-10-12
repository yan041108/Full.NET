using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features.ManageHostDocumentPermissions;
using Full.NET.Modules.Document.Persistence;
using NSubstitute;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Document;

[TestClass]
public sealed class DocumentPermissionConcurrencyTests
{
    [TestMethod]
    public void Permission_request_preserves_constructor_and_exposes_revision()
    {
        var request = new SetHostDocumentPermissionsRequest(Guid.CreateVersion7(), []);
        var (id, permissions) = request;
        Assert.AreEqual(request.DocumentId, id);
        Assert.AreEqual(0, permissions.Count);
        Assert.IsNotNull(request.GetType().GetProperty("Version"));
    }

    [TestMethod]
    public async Task Unversioned_permission_replace_fails_without_mutating_rows()
    {
        var queries = new TestQueries();
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var transaction = new InlineTransaction();
        var service = new HostDocumentPermissionManagementService(queries, commands, transaction,
            CreateQueryService(queries, transaction), Substitute.For<IClock>(), Substitute.For<IIdGenerator>());

        var result = await service.SetPermissionsAsync(new SetHostDocumentPermissionsRequest(Guid.CreateVersion7(), []), Guid.CreateVersion7());

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("document.permission.invalid", result.Error!.Code);
        await commands.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    [DataRow(long.MaxValue)]
    public async Task Invalid_revision_never_enters_transaction(long revision)
    {
        var transaction = new InlineTransaction();
        var service = new HostDocumentPermissionManagementService(null!, null!, transaction, null!, null!, null!);
        var result = await service.SetPermissionsAsync(new(Guid.CreateVersion7(), []) { Version = revision }, Guid.CreateVersion7());
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, transaction.Calls);
    }

    [TestMethod]
    public async Task Stale_revision_does_not_delete_existing_permissions()
    {
        var queries = new TestQueries();
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        var transaction = new InlineTransaction();
        var service = new HostDocumentPermissionManagementService(queries, commands, transaction,
            CreateQueryService(queries, transaction), Substitute.For<IClock>(), Substitute.For<IIdGenerator>());

        var result = await service.SetPermissionsAsync(new(Guid.CreateVersion7(), []) { Version = 6 }, Guid.CreateVersion7());

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("document.host_permission.version_conflict", result.Error!.Code);
        await commands.Received(1).ExecuteAsync(DocumentItemSql.TouchActiveItem, Arg.Any<object?>(), CancellationToken.None);
        await commands.DidNotReceive().ExecuteAsync(DocumentPermissionSql.DeleteByDocument, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, queries.ListCalls);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Empty_snapshot_has_revision_and_locks_active_host_parent_first(DatabaseProvider provider)
    {
        var queries = new TestQueries();
        var transaction = new InlineTransaction();
        var id = Guid.CreateVersion7();
        var result = await CreateQueryService(queries, transaction, provider).GetSnapshotAsync(id);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(id, result.Value!.DocumentId);
        Assert.AreEqual(7L, result.Value.Version);
        Assert.AreEqual(0, result.Value.Permissions.Count);
        Assert.AreEqual(1, transaction.Calls);
        Assert.AreEqual(SqlDataScope.HostOnly, queries.ParentStatement!.Scope);
        StringAssert.Contains(queries.ParentStatement.Text, "TenantId IS NULL AND IsDeleted = 0");
        StringAssert.Contains(queries.ParentStatement.Text, provider == DatabaseProvider.SqlServer ? "UPDLOCK, HOLDLOCK" : "FOR UPDATE");
        Assert.AreEqual(1, queries.ListCalls);
    }

    [TestMethod]
    public async Task Missing_document_snapshot_is_not_an_editable_empty_list()
    {
        var queries = new TestQueries { Missing = true };
        var result = await CreateQueryService(queries, new InlineTransaction()).GetSnapshotAsync(Guid.CreateVersion7());
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("document.host_permission.document_not_found", result.Error!.Code);
        Assert.AreEqual(0, queries.ListCalls);
    }

    private static HostDocumentPermissionQueryService CreateQueryService(IQueryExecutor queries, ICommandTransaction transaction,
        DatabaseProvider provider = DatabaseProvider.SqlServer) => new(queries, transaction, Options.Create(new DatabaseOptions { Provider = provider }));

    private sealed class InlineTransaction : ICommandTransaction
    {
        public int Calls { get; private set; }
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            Calls++;
            return action(cancellationToken);
        }
    }

    private sealed class TestQueries : IQueryExecutor
    {
        public bool Missing { get; init; }
        public int ListCalls { get; private set; }
        public SqlStatement? ParentStatement { get; private set; }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            ParentStatement = statement;
            if (statement == DocumentPermissionSql.LockDocumentSqlServer || statement == DocumentPermissionSql.LockDocumentMySql)
            {
                Assert.AreEqual(typeof(DocumentPermissionRevisionRecord), typeof(T));
                return Task.FromResult(Missing ? default : (T?)(object)new DocumentPermissionRevisionRecord { Version = 7 });
            }
            return Task.FromResult(Missing ? default : (T?)(object)new DocumentItemRecord { Version = 7 });
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Assert.IsNotNull(ParentStatement);
            ListCalls++;
            return Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());
        }
    }
}
