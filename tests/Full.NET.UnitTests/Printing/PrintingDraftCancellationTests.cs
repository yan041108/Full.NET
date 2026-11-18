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
    [DataRow(false, DatabaseProvider.SqlServer)]
    [DataRow(true, DatabaseProvider.SqlServer)]
    [DataRow(false, DatabaseProvider.MySql)]
    [DataRow(true, DatabaseProvider.MySql)]
    public async Task Cancelled_draft_read_cannot_continue_to_write_when_query_ignores_token(bool update, DatabaseProvider provider)
    {
        using var cancellation = new CancellationTokenSource();
        var id = Guid.CreateVersion7(); var queries = Substitute.For<IQueryExecutor>();
        var statement = update
            ? provider == DatabaseProvider.MySql ? PrintingTemplateSql.LockTemplateMySql : PrintingTemplateSql.LockTemplateSqlServer
            : PrintingTemplateSql.FindTemplateByKey;
        queries.QuerySingleOrDefaultAsync<PrintingTemplateRecord>(statement, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { cancellation.Cancel(); return update ? new PrintingTemplateRecord { Id = id, Version = 1 } : null; });
        var commands = Substitute.For<ICommandExecutor>();
        var transaction = Substitute.For<ICommandTransaction>();
        // 结果事务及锁定读取与正式入口一致，保证取消发生在读取完成后，确实验证后继写入被阻止。
        transaction.ExecuteResultAsync(Arg.Any<Func<CancellationToken, Task<Result<PrintingTemplateResponse>>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<Func<CancellationToken, Task<Result<PrintingTemplateResponse>>>>(0)(call.ArgAt<CancellationToken>(1)));
        var service = new PrintingTemplateManagementService(queries, commands, transaction, new(queries),
            Substitute.For<IClock>(), Substitute.For<IIdGenerator>(),
            Microsoft.Extensions.Options.Options.Create(new DatabaseOptions { Provider = provider }), new([]));
        if (update) await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.UpdateAsync(id, new("Profile", "<p>ok</p>", true, 1), cancellation.Token));
        else await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.CreateAsync(new("profile", "Profile", PrintingFormSchemaKeys.TenantProfileCard, "<p>ok</p>", true), cancellation.Token));
        Assert.AreEqual(statement, queries.ReceivedCalls().Single().GetArguments()[0]);
        Assert.AreEqual(0, commands.ReceivedCalls().Count());
    }
}
