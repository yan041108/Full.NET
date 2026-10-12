using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features.ManageHostDocumentItems;
using Full.NET.Modules.Document.Features.QueryHostRecycleBin;
using Full.NET.Modules.Document.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;
using Errors = Full.NET.Modules.Document.Contracts.DocumentErrorCodes;

namespace Full.NET.UnitTests.Document;

[TestClass]
public sealed class DocumentCategoryReferenceTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Unclassified_guard_uses_provider_lock_and_host_scope(DatabaseProvider provider)
    {
        var fixture = new Fixture();
        var service = new DocumentItemTitleService(fixture, Options.Create(new DatabaseOptions { Provider = provider }));
        Assert.IsFalse(await service.HasConflictAsync("item", null, null, CancellationToken.None));
        StringAssert.Contains(fixture.Queries[0].Text, "Id = @Id AND TenantId IS NULL");
        StringAssert.Contains(fixture.Queries[0].Text, provider == DatabaseProvider.SqlServer ? "UPDLOCK, HOLDLOCK" : "FOR UPDATE");
    }

    [TestMethod]
    public async Task Missing_unclassified_guard_fails_closed_before_title_or_write()
    {
        var fixture = new Fixture { GuardMissing = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ExecuteAsync("create"));
        Assert.AreEqual(1, fixture.Queries.Count);
        Assert.AreEqual(0, fixture.Writes.Count);
    }

    [TestMethod]
    public async Task Classified_write_keeps_category_gate_without_unclassified_guard()
    {
        var fixture = new Fixture { CategoryId = Guid.CreateVersion7(), CategoryActive = true };
        Assert.IsTrue((await fixture.Items().CreateAsync(Guid.CreateVersion7(), new("item", null) { CategoryId = fixture.CategoryId })).IsSuccess);
        Assert.IsFalse(fixture.Queries.Any(statement => statement.Text.Contains("fn_document_item_write_guard", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("create")]
    [DataRow("update")]
    [DataRow("restore")]
    [DataRow("recycle")]
    public async Task Unclassified_mutations_lock_scope_before_checking_title(string operation)
    {
        var fixture = new Fixture();
        Assert.IsTrue((await fixture.ExecuteAsync(operation)).IsSuccess);
        var guard = fixture.Queries.FindIndex(statement => statement.Text.Contains("fn_document_item_write_guard", StringComparison.Ordinal));
        var title = fixture.Queries.FindIndex(statement => statement.Text.Contains("Title = @Title", StringComparison.Ordinal));
        Assert.IsTrue(guard >= 0 && guard < title, "Unclassified writes must hold their transaction guard before title admission.");
    }

    [TestMethod]
    [DataRow("create")]
    [DataRow("update")]
    [DataRow("restore")]
    [DataRow("recycle")]
    public async Task Historical_title_duplicates_return_conflict_without_writing(string operation)
    {
        var fixture = new Fixture { HistoricalDuplicates = true };
        var result = await fixture.ExecuteAsync(operation);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(Errors.TitleConflict, result.Error!.Code);
        Assert.AreEqual(0, fixture.Writes.Count);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Creating_with_invalid_category_is_rejected_before_writing(bool empty)
    {
        var fixture = new Fixture { CategoryId = empty ? Guid.Empty : Guid.CreateVersion7() };
        var result = await fixture.Items().CreateAsync(Guid.CreateVersion7(), new("item", null) { CategoryId = fixture.CategoryId });
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(Errors.Invalid, result.Error!.Code);
        Assert.AreEqual(0, fixture.Writes.Count);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Updating_with_invalid_category_is_rejected_before_writing(bool empty)
    {
        var fixture = new Fixture { CategoryId = empty ? Guid.Empty : Guid.CreateVersion7() };
        var result = await fixture.Items().UpdateAsync(fixture.Id, Guid.CreateVersion7(), new("item", null, 1) { CategoryId = fixture.CategoryId });
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(Errors.Invalid, result.Error!.Code);
        Assert.AreEqual(0, fixture.Writes.Count);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Both_restore_routes_detach_invalid_category(bool recycle)
    {
        var fixture = new Fixture { CategoryId = Guid.CreateVersion7() };
        var result = recycle
            ? await fixture.Recycle().RestoreAsync(fixture.Id, Guid.CreateVersion7(), new(1))
            : await fixture.Items().RestoreAsync(fixture.Id, Guid.CreateVersion7(), new(1));
        Assert.IsTrue(result.IsSuccess);
        var write = fixture.Writes.Single();
        Assert.IsTrue(write.ContainsKey("CategoryId"));
        Assert.IsNull(write["CategoryId"]);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Both_restore_routes_reject_active_title_collision_without_writing(bool recycle)
    {
        var fixture = new Fixture { TitleCollision = true };
        var result = recycle
            ? await fixture.Recycle().RestoreAsync(fixture.Id, Guid.CreateVersion7(), new(1))
            : await fixture.Items().RestoreAsync(fixture.Id, Guid.CreateVersion7(), new(1));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(Errors.TitleConflict, result.Error!.Code);
        Assert.AreEqual(0, fixture.Writes.Count);
    }

    [TestMethod]
    public void Statistics_never_project_category_identity_outside_active_host_scope()
    {
        var sql = DocumentItemSql.StatisticsByCategory.Text;
        StringAssert.Contains(sql, "c.TenantId IS NULL");
        StringAssert.Contains(sql, "c.IsDeleted = 0");
        StringAssert.Contains(sql, "c.Id AS CategoryId");
        StringAssert.Contains(sql, "GROUP BY c.Id, c.Name");
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Category_lock_uses_active_host_predicate_and_provider_lock(DatabaseProvider provider)
    {
        var fixture = new Fixture { CategoryId = Guid.CreateVersion7(), CategoryActive = true };
        var service = new DocumentItemCategoryReferenceService(fixture, Options.Create(new DatabaseOptions { Provider = provider }));
        Assert.IsTrue(await service.LockActiveAsync(fixture.CategoryId, CancellationToken.None));
        Assert.IsNotNull(fixture.CategoryStatement);
        StringAssert.Contains(fixture.CategoryStatement.Text, "Id = @Id AND TenantId IS NULL AND IsDeleted = 0");
        StringAssert.Contains(fixture.CategoryStatement.Text, provider == DatabaseProvider.SqlServer ? "UPDLOCK, HOLDLOCK" : "FOR UPDATE");
    }

    [TestMethod]
    public async Task Unclassified_create_remains_valid_without_category_query()
    {
        var fixture = new Fixture();
        Assert.IsTrue((await fixture.Items().CreateAsync(Guid.CreateVersion7(), new("item", null))).IsSuccess);
        Assert.IsNull(fixture.CategoryStatement);
    }

    [TestMethod]
    public async Task Null_update_category_preserves_existing_active_category()
    {
        var fixture = new Fixture { CategoryId = Guid.CreateVersion7(), CategoryActive = true };
        Assert.IsTrue((await fixture.Items().UpdateAsync(fixture.Id, Guid.CreateVersion7(), new("item", null, 1))).IsSuccess);
        Assert.AreEqual(fixture.CategoryId, fixture.Writes.Single()["CategoryId"]);
    }

    [TestMethod]
    [DataRow(true, "document.recycle.not_found")]
    [DataRow(false, "document.host_document.version_conflict")]
    public async Task Active_item_restore_preserves_route_failure_semantics(bool recycle, string code)
    {
        var fixture = new Fixture { Deleted = false };
        var result = recycle ? await fixture.Recycle().RestoreAsync(fixture.Id, Guid.CreateVersion7(), new(1))
            : await fixture.Items().RestoreAsync(fixture.Id, Guid.CreateVersion7(), new(1));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(code, result.Error!.Code);
        Assert.AreEqual(0, fixture.Writes.Count);
    }

    private sealed class InlineTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) => action(cancellationToken);
    }

    private sealed class Fixture : IQueryExecutor
    {
        public Guid Id { get; } = Guid.CreateVersion7();
        public Guid? CategoryId { get; init; }
        public bool TitleCollision { get; init; }
        public bool HistoricalDuplicates { get; init; }
        public bool GuardMissing { get; init; }
        public bool CategoryActive { get; init; }
        public bool Deleted { get; init; } = true;
        public SqlStatement? CategoryStatement { get; private set; }
        public List<Dictionary<string, object?>> Writes { get; } = [];
        public List<SqlStatement> Queries { get; } = [];
        private readonly ICommandExecutor commands;
        private readonly IClock clock = Substitute.For<IClock>();
        private readonly IIdGenerator ids = Substitute.For<IIdGenerator>();

        public Fixture()
        {
            commands = Substitute.For<ICommandExecutor>();
            commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Writes.Add((Dictionary<string, object?>)call[1]!);
                return 1;
            });
            ids.NewId().Returns(Id);
        }

        private DocumentItemTagAssignmentService Tags() => new(this, commands,
            new(this, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer })));
        private DocumentItemCategoryReferenceService Categories() => new(this, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
        private DocumentItemTitleService Titles() => new(this, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
        private DocumentItemRestoreService Restores() => new(this, commands, Categories(), clock, Titles());
        private DocumentItemWriteGuard ItemWrites() => new(this, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
        public HostDocumentItemManagementService Items() => new(this, commands, new InlineTransaction(), null!, null!, null!, Tags(), clock, ids, Categories(), Restores(), Titles(), ItemWrites());
        public HostRecycleBinManagementService Recycle() => new(this, commands, new InlineTransaction(),
            new(null!, this, null!, null!, Tags(), Options.Create(new DatabaseOptions())), Restores(), ItemWrites(), Substitute.For<IOutboxWriter>(), Options.Create(new DatabaseOptions()));

        public Task<Result<HostDocumentItemResponse>> ExecuteAsync(string operation) => operation switch
        {
            "create" => Items().CreateAsync(Guid.CreateVersion7(), new("item", null)),
            "update" => Items().UpdateAsync(Id, Guid.CreateVersion7(), new("item", null, 1)),
            "restore" => Items().RestoreAsync(Id, Guid.CreateVersion7(), new(1)),
            _ => Recycle().RestoreAsync(Id, Guid.CreateVersion7(), new(1)),
        };

        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Queries.Add(statement);
            // 重现单行查询面对既有多条同名记录时抛异常的边界。
            if (typeof(T) == typeof(DocumentNameConflictRecord) && HistoricalDuplicates)
                throw new InvalidOperationException("Sequence contains more than one element.");
            if (typeof(T) == typeof(int)) return Task.FromResult((T?)(object)(GuardMissing ? 0 : 1));
            if (typeof(T) == typeof(long)) return Task.FromResult((T?)(object)(HistoricalDuplicates ? 2L : TitleCollision ? 1L : 0L));
            if (statement == DocumentItemSql.FindDeletedById && !Deleted) return Task.FromResult<T?>(default);
            if (typeof(T) == typeof(DocumentCategoryRecord)) CategoryStatement = statement;
            object? row = typeof(T) == typeof(DocumentItemRecord)
                ? new DocumentItemRecord { Id = Id, Title = "item", CategoryId = CategoryId, Version = 1 }
                : typeof(T) == typeof(DocumentItemDetailRecord)
                    ? new DocumentItemDetailRecord { Id = Id, Title = "item", CategoryId = CategoryId, Version = 1, DeletedAtUtc = Deleted ? DateTimeOffset.UtcNow : null }
                    : typeof(T) == typeof(DocumentCategoryRecord) && CategoryActive
                        ? new DocumentCategoryRecord { Id = CategoryId!.Value, Name = "category", Version = 1 }
                        : typeof(T) == typeof(DocumentNameConflictRecord) && TitleCollision
                            ? new DocumentNameConflictRecord { Id = Guid.CreateVersion7(), Name = "item", Version = 1 } : null;
            return Task.FromResult(row is null ? default : (T?)(object)row);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());
    }
}
