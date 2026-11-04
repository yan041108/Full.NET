using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.GoView.Contracts;
using Full.NET.Modules.GoView.Domain;
using Full.NET.Modules.GoView.Features.ManageProjects;
using Full.NET.Modules.GoView.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.GoView;

[TestClass]
public sealed class GoViewProjectTransactionTests
{
    [TestMethod]
    public async Task Publish_rolls_back_inserted_version_when_parent_cas_fails()
    {
        var fixture = new Fixture { FailParentWrite = true };
        var result = await fixture.Service.PublishAsync(fixture.Project.Id, fixture.Actor, new(null, 1));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(GoViewErrorCodes.ProjectConcurrencyConflict, result.Error!.Code);
        Assert.AreEqual(1, fixture.VersionInserts);
        fixture.AssertRollback();
    }

    [TestMethod]
    public async Task Publish_rolls_back_when_version_read_after_writes_returns_failure()
    {
        var fixture = new Fixture { MissingVersion = true };
        var result = await fixture.Service.PublishAsync(fixture.Project.Id, fixture.Actor, new(null, 1));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(1, fixture.VersionInserts);
        Assert.AreEqual(1, fixture.ParentWrites);
        fixture.AssertRollback();
    }

    [TestMethod]
    public async Task Create_rolls_back_insert_when_authoritative_read_returns_failure()
    {
        var fixture = new Fixture { MissingProjectAfterWrite = true };
        var result = await fixture.Service.CreateAsync(new("canvas", "Canvas", null, true));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(1, fixture.ParentWrites);
        fixture.AssertRollback();
    }

    [TestMethod]
    public async Task Update_rolls_back_when_authoritative_read_returns_failure()
    {
        var fixture = new Fixture { MissingProjectAfterWrite = true };
        var result = await fixture.Service.UpdateAsync(fixture.Project.Id, new("Canvas", GoViewCanvasPolicy.DefaultCanvasJson, true, 1));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(1, fixture.ParentWrites);
        fixture.AssertRollback();
    }

    [TestMethod]
    public async Task Update_cas_failure_does_not_commit()
    {
        var fixture = new Fixture { FailParentWrite = true };
        var result = await fixture.Service.UpdateAsync(fixture.Project.Id, new("Canvas", GoViewCanvasPolicy.DefaultCanvasJson, true, 1));
        Assert.IsFalse(result.IsSuccess);
        fixture.AssertRollback();
    }

    [TestMethod]
    public async Task Successful_create_commits()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.CreateAsync(new("canvas", "Canvas", null, true));
        Assert.IsTrue(result.IsSuccess);
        fixture.AssertCommit();
    }

    [TestMethod]
    public async Task Successful_update_commits()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.UpdateAsync(fixture.Project.Id, new("Canvas", GoViewCanvasPolicy.DefaultCanvasJson, true, 1));
        Assert.IsTrue(result.IsSuccess);
        fixture.AssertCommit();
    }

    [TestMethod]
    public async Task Successful_publish_commits_version_and_parent_once()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.PublishAsync(fixture.Project.Id, fixture.Actor, new(null, 1));
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, fixture.VersionInserts);
        Assert.AreEqual(1, fixture.ParentWrites);
        fixture.AssertCommit();
    }

    private sealed class Fixture
    {
        public readonly RecordingDbTransactionCoordinator Coordinator = new();
        public readonly Guid Actor = Guid.CreateVersion7();
        public readonly GoViewProjectRecord Project = new() { Id = Guid.CreateVersion7(), ProjectKey = "canvas", Name = "Canvas", CanvasJson = GoViewCanvasPolicy.DefaultCanvasJson, IsEnabled = true, Version = 1, CreatedAtUtc = DateTimeOffset.UtcNow };
        public bool FailParentWrite;
        public bool MissingVersion;
        public bool MissingProjectAfterWrite;
        public int ParentWrites;
        public int VersionInserts;
        public GoViewProjectManagementService Service { get; }

        public Fixture()
        {
            var query = Substitute.For<IQueryExecutor>();
            var commands = Substitute.For<ICommandExecutor>();
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
            query.QuerySingleOrDefaultAsync<GoViewProjectRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
                call.ArgAt<SqlStatement>(0) == GoViewProjectSql.FindProjectByKey || MissingProjectAfterWrite && ParentWrites > 0 ? null : Project);
            query.QuerySingleOrDefaultAsync<GoViewProjectVersionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => MissingVersion ? null : new GoViewProjectVersionRecord { Id = Guid.CreateVersion7(), ProjectId = Project.Id, VersionNumber = 1, CanvasJson = Project.CanvasJson, PublishedByUserId = Actor, PublishedAtUtc = clock.UtcNow });
            commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Assert.IsTrue(Coordinator.HasTransaction);
                if (call.ArgAt<SqlStatement>(0) == GoViewProjectSql.InsertVersion) { VersionInserts++; return 1; }
                ParentWrites++; return FailParentWrite ? 0 : 1;
            });
            Service = new(query, commands, new DapperCommandTransaction(Coordinator), new GoViewProjectQueryService(query), clock, ids);
        }

        public void AssertRollback() { Assert.AreEqual(0, Coordinator.CommitCount); Assert.AreEqual(1, Coordinator.RollbackCount); Assert.IsFalse(Coordinator.HasTransaction); }
        public void AssertCommit() { Assert.AreEqual(1, Coordinator.CommitCount); Assert.AreEqual(0, Coordinator.RollbackCount); Assert.IsFalse(Coordinator.HasTransaction); }
    }
}
