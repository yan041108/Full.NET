using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Features.ImportExport;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Identity.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.EnterpriseRequest;

/// <summary>导入授权必须先于本地事务，重放只返回原业务身份，篡改不能产生第二条业务行。</summary>
[TestClass]
public sealed class EnterpriseRequestImportWriterTests
{
    [TestMethod]
    public async Task False_organization_authorization_never_queries_or_starts_a_transaction()
    {
        var fixture = new Fixture();
        fixture.Authorizer.EnsureCanWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(false));
        var result = await fixture.Writer.WriteAsync(fixture.Row, fixture.Context, default);
        Assert.AreEqual(ErrorType.Forbidden, result.Error!.Type);
        Assert.IsFalse(fixture.Queries.ReceivedCalls().Any());
        Assert.IsFalse(fixture.Transaction.ReceivedCalls().Any());
        Assert.IsFalse(fixture.Commands.ReceivedCalls().Any());
    }

    [TestMethod]
    public async Task Durable_task_and_source_are_required_before_any_authorization_or_write()
    {
        var fixture = new Fixture();
        Assert.IsFalse((await fixture.Writer.WriteAsync(fixture.Row, fixture.Context with { TaskId = null }, default)).IsSuccess);
        Assert.IsFalse((await fixture.Writer.WriteAsync(fixture.Row, fixture.Context with { SourceFileId = null }, default)).IsSuccess);
        Assert.IsFalse(fixture.Authorizer.ReceivedCalls().Any());
        Assert.IsFalse(fixture.Commands.ReceivedCalls().Any());
    }

    [TestMethod]
    public async Task Successful_row_and_receipt_share_one_transaction_and_replay_does_not_write()
    {
        var fixture = new Fixture();
        string? savedDigest = null;
        fixture.Commands.ExecuteAsync(EnterpriseRequestImportWriter.InsertReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call => { savedDigest = (string)((Dictionary<string, object?>)call[1]!)["Digest"]!; return 1; });
        var result = await fixture.Writer.WriteAsync(fixture.Row, fixture.Context, default);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(fixture.RequestId, result.Value);
        Assert.AreEqual(2, fixture.Commands.ReceivedCalls().Count());
        Assert.AreEqual(1, fixture.Transaction.ReceivedCalls().Count());
        fixture.Queries.QuerySingleOrDefaultAsync<Guid?>(EnterpriseRequestImportWriter.FindRequest, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(fixture.RequestId);
        fixture.Queries.QuerySingleOrDefaultAsync<string>(EnterpriseRequestImportWriter.FindDigest, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(savedDigest);
        var replay = await fixture.Writer.WriteAsync(fixture.Row, fixture.Context, default);
        Assert.AreEqual(fixture.RequestId, replay.Value);
        Assert.AreEqual(2, fixture.Commands.ReceivedCalls().Count());
        var changed = fixture.Row with { Request = fixture.Row.Request with { Title = "changed" } };
        Assert.AreEqual(ErrorType.Conflict, (await fixture.Writer.WriteAsync(changed, fixture.Context, default)).Error!.Type);
        Assert.AreEqual(2, fixture.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Revoked_source_permission_or_missing_session_never_enters_row_transaction()
    {
        var fixture = new Fixture();
        fixture.Sessions.AuthorizeAllAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns((AuthorizedSessionActor?)null);
        Assert.AreEqual(ImportTaskAuthorityErrorCodes.Denied, (await fixture.Writer.WriteAsync(fixture.Row, fixture.Context, default)).Error!.Code);
        Assert.AreEqual(ImportTaskAuthorityErrorCodes.Denied, (await fixture.Writer.WriteAsync(fixture.Row, fixture.Context with { SessionBinding = null }, default)).Error!.Code);
        Assert.IsFalse(fixture.Transaction.ReceivedCalls().Any()); Assert.IsFalse(fixture.Commands.ReceivedCalls().Any());
    }

    private sealed class Fixture
    {
        internal IQueryExecutor Queries { get; } = Substitute.For<IQueryExecutor>();
        internal ICommandExecutor Commands { get; } = Substitute.For<ICommandExecutor>();
        internal ICommandTransaction Transaction { get; } = Substitute.For<ICommandTransaction>();
        internal IOrganizationOwnedEntityWriteAuthorizer Authorizer { get; } = Substitute.For<IOrganizationOwnedEntityWriteAuthorizer>();
        internal IBackgroundSessionAuthorization Sessions { get; } = Substitute.For<IBackgroundSessionAuthorization>();
        internal Guid RequestId { get; } = Guid.CreateVersion7();
        internal StaticImportPreviewContext Context { get; } = new(Guid.CreateVersion7(), new Dictionary<string, bool>())
        { TaskId = Guid.CreateVersion7(), SourceFileId = Guid.CreateVersion7() };
        internal EnterpriseRequestWorkbookReader.Row Row { get; } = new(2,
            new("REQ", "Title", "Draft", 12.50m, Guid.CreateVersion7()), Guid.CreateVersion7(), null);
        internal EnterpriseRequestImportWriter Writer { get; }

        internal Fixture()
        {
            var tenant = new CurrentTenantAccessor();
            ((ICurrentTenantContextWriter)tenant).SetTenant(new(Guid.CreateVersion7(), "t", "T"));
            Context = Context with { SessionBinding = new(Context.RequestedByUserId, tenant.Id, Guid.CreateVersion7(), "stamp", "host", $"tenant:{tenant.Id:N}") };
            Sessions.AuthorizeAllAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(new AuthorizedSessionActor(Context.RequestedByUserId, tenant.Id, Context.SessionBinding.SessionId));
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(RequestId, Guid.CreateVersion7());
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            Authorizer.EnsureCanWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Result<bool>.Success(true));
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Transaction.ExecuteResultAsync(Arg.Any<Func<CancellationToken, Task<Result<Guid>>>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<Func<CancellationToken, Task<Result<Guid>>>>()!(call.Arg<CancellationToken>()));
            Writer = new(Queries, Commands, Transaction, tenant, Authorizer, ids, clock, Sessions);
        }
    }
}
