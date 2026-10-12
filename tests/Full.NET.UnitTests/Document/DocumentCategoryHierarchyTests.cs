using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features.ManageHostDocumentCategories;
using Full.NET.Modules.Document.Persistence;
using NSubstitute;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Document;

[TestClass]
public sealed class DocumentCategoryHierarchyTests
{
    [TestMethod]
    [DataRow(2)]
    [DataRow(5)]
    public async Task Moving_category_under_its_descendant_is_rejected_without_writing(int depth)
    {
        var rows = Enumerable.Range(0, depth).Select(_ => new DocumentCategoryRecord
        {
            Id = Guid.CreateVersion7(), Name = "category", Version = 1,
        }).ToArray();
        for (var i = 1; i < rows.Length; i++)
            rows[i] = new DocumentCategoryRecord { Id = rows[i].Id, ParentId = rows[i - 1].Id, Name = "child", Version = 1 };
        var queries = new TestQueries(rows);
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);

        var result = await CreateService(queries, commands).UpdateAsync(rows[0].Id,
            new("renamed", rows[^1].Id, 0, 1));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(Full.NET.Modules.Document.Contracts.DocumentErrorCodes.CategoryInvalidParent, result.Error!.Code);
        await commands.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Invalid_existing_parent_chain_is_rejected_without_writing(bool cycle)
    {
        var target = Guid.CreateVersion7();
        var parent = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var rows = new List<DocumentCategoryRecord>
        {
            new() { Id = target, Name = "target", Version = 1 },
            new() { Id = parent, ParentId = other, Name = "parent", Version = 1 },
        };
        if (cycle) rows.Add(new() { Id = other, ParentId = parent, Name = "cycle", Version = 1 });
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);

        var result = await CreateService(new TestQueries(rows), commands).UpdateAsync(target, new("target", parent, 0, 1));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(Full.NET.Modules.Document.Contracts.DocumentErrorCodes.CategoryInvalidParent, result.Error!.Code);
        await commands.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    public async Task Moving_category_to_root_remains_allowed()
    {
        var id = Guid.CreateVersion7();
        var queries = new TestQueries([new() { Id = id, ParentId = Guid.CreateVersion7(), Name = "target", Version = 1 }]);
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var result = await CreateService(queries, commands).UpdateAsync(id, new("target", null, 0, 1));
        Assert.IsTrue(result.IsSuccess);
        await commands.Received(1).ExecuteAsync(DocumentCategorySql.Update, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "create")]
    [DataRow(DatabaseProvider.SqlServer, "update")]
    [DataRow(DatabaseProvider.SqlServer, "delete")]
    [DataRow(DatabaseProvider.MySql, "create")]
    [DataRow(DatabaseProvider.MySql, "update")]
    [DataRow(DatabaseProvider.MySql, "delete")]
    public async Task Every_category_mutation_locks_host_hierarchy_before_reading_or_writing(DatabaseProvider provider, string operation)
    {
        var id = Guid.CreateVersion7();
        var queries = new TestQueries([new() { Id = id, Name = "target", Version = 1 }]);
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var service = CreateService(queries, commands, provider);
        if (operation == "create") await service.CreateAsync(new("new", null, 0));
        else if (operation == "update") await service.UpdateAsync(id, new("updated", null, 0, 1));
        else await service.DeleteAsync(id, Guid.CreateVersion7(), new(1));

        var statement = queries.FirstStatement;
        Assert.IsNotNull(statement);
        Assert.AreEqual(SqlDataScope.HostOnly, statement.Scope);
        StringAssert.Contains(statement.Text, "TenantId IS NULL AND IsDeleted = 0");
        StringAssert.Contains(statement.Text, provider == DatabaseProvider.SqlServer ? "UPDLOCK, HOLDLOCK" : "FOR UPDATE");
        StringAssert.Contains(statement.Text, "ORDER BY Id");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Creating_under_invalid_existing_parent_chain_is_rejected(bool cycle)
    {
        var parent = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var rows = new List<DocumentCategoryRecord> { new() { Id = parent, ParentId = other, Name = "parent", Version = 1 } };
        if (cycle) rows.Add(new() { Id = other, ParentId = parent, Name = "cycle", Version = 1 });
        var commands = Substitute.For<ICommandExecutor>();
        var result = await CreateService(new(rows), commands).CreateAsync(new("new", parent, 0));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(Full.NET.Modules.Document.Contracts.DocumentErrorCodes.CategoryInvalidParent, result.Error!.Code);
        await commands.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    private static HostDocumentCategoryManagementService CreateService(TestQueries queries, ICommandExecutor commands,
        DatabaseProvider provider = DatabaseProvider.SqlServer) =>
        new(queries, commands, new InlineTransaction(), new(queries), Substitute.For<IClock>(), Substitute.For<IIdGenerator>(),
            Options.Create(new DatabaseOptions { Provider = provider }));

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Delete_rechecks_children_after_waiting_hierarchy_scan(DatabaseProvider provider)
    {
        var id = Guid.CreateVersion7();
        var queries = new TestQueries([new() { Id = id, Name = "parent", Version = 1 }]) { ChildCount = 1 };
        var commands = Substitute.For<ICommandExecutor>();
        var result = await CreateService(queries, commands, provider).DeleteAsync(id, Guid.CreateVersion7(), new(1));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(Full.NET.Modules.Document.Contracts.DocumentErrorCodes.CategoryHasChildren, result.Error!.Code);
        await commands.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    private sealed class InlineTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) => action(cancellationToken);
    }

    private sealed class TestQueries(IEnumerable<DocumentCategoryRecord> rows) : IQueryExecutor
    {
        private readonly Dictionary<Guid, DocumentCategoryRecord> categories = rows.ToDictionary(row => row.Id);
        public SqlStatement? FirstStatement { get; private set; }
        public long ChildCount { get; init; }

        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            FirstStatement ??= statement;
            if (statement == DocumentCategorySql.CountActiveChildren)
                return Task.FromResult((T?)(object)ChildCount);
            if (typeof(T) == typeof(DocumentCategoryRecord))
            {
                var id = (Guid)((Dictionary<string, object?>)parameters!)["Id"]!;
                return Task.FromResult(categories.TryGetValue(id, out var row) ? (T?)(object)row : default);
            }
            return Task.FromResult<T?>(default);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            FirstStatement ??= statement;
            return Task.FromResult<IReadOnlyList<T>>(categories.Values.Cast<T>().ToArray());
        }
    }
}
