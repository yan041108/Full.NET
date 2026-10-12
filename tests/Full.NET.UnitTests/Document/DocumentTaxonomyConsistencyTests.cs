using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features.ManageHostDocumentCategories;
using Full.NET.Modules.Document.Features.ManageHostDocumentItems;
using Full.NET.Modules.Document.Features.ManageHostDocumentTags;
using Full.NET.Modules.Document.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;
using Errors = Full.NET.Modules.Document.Contracts.DocumentErrorCodes;

namespace Full.NET.UnitTests.Document;

/// <summary>分类、标签名称准入及引用事务的共性缺陷回归。</summary>
[TestClass]
public sealed class DocumentTaxonomyConsistencyTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Catalog_guards_use_distinct_host_rows_and_provider_locks(DatabaseProvider provider)
    {
        var fixture = new Fixture();
        var guard = new DocumentTaxonomyWriteGuard(fixture, Options.Create(new DatabaseOptions { Provider = provider }));
        await guard.LockCategoriesAsync(CancellationToken.None);
        await guard.LockTagsAsync(CancellationToken.None);
        CollectionAssert.AreEqual(new[] { DocumentTaxonomyWriteGuard.CategoryGuardId, DocumentTaxonomyWriteGuard.TagGuardId }, fixture.GuardKeys.ToArray());
        Assert.AreNotEqual(fixture.GuardKeys[0], fixture.GuardKeys[1]);
        foreach (var statement in fixture.Statements)
        {
            Assert.AreEqual(SqlDataScope.HostOnly, statement.Scope);
            StringAssert.Contains(statement.Text, "Id = @Id AND TenantId IS NULL");
            StringAssert.Contains(statement.Text, provider == DatabaseProvider.SqlServer ? "UPDLOCK, HOLDLOCK" : "FOR UPDATE");
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Missing_catalog_guard_fails_closed_before_other_queries(bool categories)
    {
        var fixture = new Fixture { GuardMissing = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => categories ? fixture.Guard().LockCategoriesAsync(CancellationToken.None) : fixture.Guard().LockTagsAsync(CancellationToken.None));
        Assert.AreEqual(1, fixture.Statements.Count);
        Assert.AreEqual(0, fixture.Writes);
    }

    [TestMethod]
    public async Task Empty_tag_validation_keeps_clear_assignment_path_without_guard()
    {
        var fixture = new Fixture();
        var service = new DocumentItemTagAssignmentService(fixture, fixture.Commands, fixture.Guard());
        Assert.IsTrue((await service.ValidateTagIdsAsync([], CancellationToken.None)).IsSuccess);
        Assert.AreEqual(0, fixture.Statements.Count);
    }

    [TestMethod]
    [DataRow("category-create")]
    [DataRow("category-update")]
    [DataRow("tag-create")]
    [DataRow("tag-update")]
    public async Task Legacy_duplicate_names_return_conflict_without_writing(string operation)
    {
        var fixture = new Fixture { DuplicateNames = true };
        var error = await fixture.ExecuteAsync(operation);
        Assert.IsNotNull(error);
        Assert.AreEqual(operation.StartsWith("category", StringComparison.Ordinal) ? Errors.CategoryNameExists : Errors.TagNameExists, error.Code);
        Assert.AreEqual(0, fixture.Writes);
    }

    [TestMethod]
    [DataRow("category-create")]
    [DataRow("category-update")]
    [DataRow("category-delete")]
    [DataRow("tag-create")]
    [DataRow("tag-update")]
    [DataRow("tag-delete")]
    public async Task Catalog_mutations_lock_scope_before_reading_even_when_empty(string operation)
    {
        var fixture = new Fixture { EmptyCategories = operation == "category-create" };
        Assert.IsNull(await fixture.ExecuteAsync(operation));
        Assert.IsTrue(fixture.Statements[0].Text.Contains("fn_document_taxonomy_write_guard", StringComparison.Ordinal),
            "A row-independent admission guard must exist before reading the catalog or counting names.");
    }

    [TestMethod]
    public async Task Tag_reference_validation_holds_same_gate_as_tag_deletion()
    {
        var fixture = new Fixture();
        var assignments = new DocumentItemTagAssignmentService(fixture, fixture.Commands, fixture.Guard());
        Assert.IsTrue((await assignments.ValidateTagIdsAsync([fixture.Id], CancellationToken.None)).IsSuccess);
        Assert.IsTrue(fixture.Statements[0].Text.Contains("fn_document_taxonomy_write_guard", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("tag-create")]
    [DataRow("tag-update")]
    public async Task Failed_tag_reload_after_write_rolls_back_transaction(string operation)
    {
        var fixture = new Fixture { MissingAfterWrite = true };
        Assert.IsNotNull(await fixture.ExecuteAsync(operation));
        Assert.AreEqual(1, fixture.Writes);
        await fixture.Coordinator.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await fixture.Coordinator.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
    }

    private sealed class Fixture : IQueryExecutor
    {
        internal Guid Id { get; } = Guid.CreateVersion7();
        internal bool DuplicateNames { get; init; }
        internal bool EmptyCategories { get; init; }
        internal bool MissingAfterWrite { get; init; }
        internal bool GuardMissing { get; init; }
        internal int Writes { get; private set; }
        internal List<SqlStatement> Statements { get; } = [];
        internal List<Guid> GuardKeys { get; } = [];
        internal IDbTransactionCoordinator Coordinator { get; } = Substitute.For<IDbTransactionCoordinator>();
        internal ICommandTransaction Transaction { get; }
        internal ICommandExecutor Commands { get; } = Substitute.For<ICommandExecutor>();
        private readonly IClock clock = Substitute.For<IClock>();
        private readonly IIdGenerator ids = Substitute.For<IIdGenerator>();

        internal Fixture()
        {
            // 执行正式 Dapper 事务实现，观察底层提交/回滚调用，不复制 Result 事务策略。
            Transaction = new DapperCommandTransaction(Coordinator);
            ids.NewId().Returns(Id);
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => { Writes++; return 1; });
        }

        internal DocumentTaxonomyWriteGuard Guard() => new(this, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));

        internal async Task<Error?> ExecuteAsync(string operation)
        {
            var categories = new HostDocumentCategoryManagementService(this, Commands, Transaction, new(this), clock, ids,
                Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }), Guard());
            var tags = new HostDocumentTagManagementService(this, Commands, Transaction, new(this), clock, ids, Guard());
            return operation switch
            {
                "category-create" => (await categories.CreateAsync(new("name", null, 0))).Error,
                "category-update" => (await categories.UpdateAsync(Id, new("name", null, 0, 1))).Error,
                "category-delete" => (await categories.DeleteAsync(Id, Guid.CreateVersion7(), new(1))).Error,
                "tag-create" => (await tags.CreateAsync(new("name"))).Error,
                "tag-update" => (await tags.UpdateAsync(Id, new("name", 1))).Error,
                _ => (await tags.DeleteAsync(Id, Guid.CreateVersion7(), new(1))).Error,
            };
        }

        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Statements.Add(statement);
            if (statement.Text.Contains("fn_document_taxonomy_write_guard", StringComparison.Ordinal))
                GuardKeys.Add((Guid)((Dictionary<string, object?>)parameters!)["Id"]!);
            // 单行 API 在多条历史同名记录时的真实失败边界。
            if (typeof(T) == typeof(DocumentNameConflictRecord) && DuplicateNames) throw new InvalidOperationException("Sequence contains more than one element.");
            object? result = typeof(T) == typeof(int) ? (GuardMissing ? 0 : 1)
                : typeof(T) == typeof(long) ? (statement.Text.Contains("Name = @Name", StringComparison.Ordinal) && DuplicateNames ? 2L : 0L)
                : typeof(T) == typeof(DocumentCategoryRecord) ? new DocumentCategoryRecord { Id = Id, Name = "name", Version = 1 }
                : typeof(T) == typeof(DocumentTagRecord) && !(MissingAfterWrite && Writes > 0) ? new DocumentTagRecord { Id = Id, Name = "name", Version = 1 } : null;
            return Task.FromResult(result is null ? default : (T?)(object)result);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Statements.Add(statement);
            IReadOnlyList<T> rows = typeof(T) == typeof(DocumentCategoryRecord) && !EmptyCategories
                ? [(T)(object)new DocumentCategoryRecord { Id = Id, Name = "name", Version = 1 }] : [];
            return Task.FromResult(rows);
        }
    }
}
