using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Printing.Domain;
using Full.NET.Modules.Printing.Features.ManageTemplates;
using Full.NET.Modules.Printing.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Printing;

[TestClass]
public sealed class PrintingDraftCancellationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cancelled_draft_read_cannot_continue_to_write_when_query_ignores_token(bool update)
    {
        using var cancellation = new CancellationTokenSource();
        var id = Guid.CreateVersion7(); var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<PrintingTemplateRecord>(PrintingTemplateSql.FindTemplateByKey, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { cancellation.Cancel(); return (PrintingTemplateRecord?)null; });
        queries.QuerySingleOrDefaultAsync<PrintingTemplateRecord>(PrintingTemplateSql.FindTemplateById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { cancellation.Cancel(); return new PrintingTemplateRecord { Id = id, Version = 1 }; });
        var commands = Substitute.For<ICommandExecutor>();
        var transaction = Substitute.For<ICommandTransaction>();
        transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<Result<PrintingTemplateResponse>>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<Func<CancellationToken, Task<Result<PrintingTemplateResponse>>>>(0)(call.ArgAt<CancellationToken>(1)));
        var service = new PrintingTemplateManagementService(queries, commands, transaction, new(queries),
            Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), new([]));
        if (update) await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.UpdateAsync(id, new("Profile", "<p>ok</p>", true, 1), cancellation.Token));
        else await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.CreateAsync(new("profile", "Profile", PrintingFormSchemaKeys.TenantProfileCard, "<p>ok</p>", true), cancellation.Token));
        Assert.AreEqual(0, commands.ReceivedCalls().Count());
    }
}
