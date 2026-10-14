using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.PreviewTasks;
using Full.NET.Modules.Document.Providers.OfficePreview;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Document;

/// <summary>候选 ID 不等于本实例领取结果；不能继续处理其他实例或已取消的行。</summary>
[TestClass]
public sealed class DocumentPreviewTaskClaimTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    public async Task Incomplete_claim_rolls_back_before_opening_any_source(int affected)
    {
        var f = new Fixture();
        f.Commands.ExecuteAsync(DocumentPreviewTaskSql.ClaimByIdsMySql, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(affected);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Runner().ProcessPendingAsync(default));
        Assert.AreEqual(0, f.Files.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("cancelled")]
    [DataRow("missing")]
    public async Task Unexpected_claim_reload_cannot_convert_unowned_rows(string state)
    {
        var f = new Fixture();
        f.Commands.ExecuteAsync(DocumentPreviewTaskSql.ClaimByIdsMySql, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(1);
        f.Queries.QueryAsync<DocumentPreviewTaskRecord>(DocumentPreviewTaskSql.SelectByIds, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(state == "missing" ? Array.Empty<DocumentPreviewTaskRecord>() : new[] { new DocumentPreviewTaskRecord { Id = f.Ids[0], StatusKey = state } });
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Runner().ProcessPendingAsync(default));
        Assert.AreEqual(0, f.Files.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Empty_queue_does_not_write_or_read_files()
    {
        var f = new Fixture();
        f.Queries.QueryAsync<Guid>(DocumentPreviewTaskSql.SelectClaimableIdsMySql, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
        Assert.AreEqual(0, await f.Runner().ProcessPendingAsync(default));
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count(call => !call.GetArguments().Contains(DocumentPreviewTaskSql.ExpireProcessingMySql)));
        Assert.AreEqual(0, f.Files.ReceivedCalls().Count());
    }

    private sealed class Fixture
    {
        internal Guid[] Ids { get; } = [Guid.NewGuid()];
        internal IQueryExecutor Queries { get; } = Substitute.For<IQueryExecutor>();
        internal ICommandExecutor Commands { get; } = Substitute.For<ICommandExecutor>();
        internal IHostFileContentReader Files { get; } = Substitute.For<IHostFileContentReader>();
        private readonly ICommandTransaction transaction = Substitute.For<ICommandTransaction>();
        private readonly IOptionsMonitor<DocumentOfficePreviewConversionOptions> options = Substitute.For<IOptionsMonitor<DocumentOfficePreviewConversionOptions>>();
        private readonly IClock clock = Substitute.For<IClock>();

        internal Fixture()
        {
            options.CurrentValue.Returns(new DocumentOfficePreviewConversionOptions { Enabled = true });
            clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            Queries.QueryAsync<Guid>(DocumentPreviewTaskSql.SelectClaimableIdsMySql, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(Ids);
            transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<IReadOnlyList<DocumentPreviewTaskRecord>>>>(), Arg.Any<CancellationToken>())
                .Returns(info => info.Arg<Func<CancellationToken, Task<IReadOnlyList<DocumentPreviewTaskRecord>>>>()(info.Arg<CancellationToken>()));
        }

        internal DocumentPreviewTaskRunner Runner() => new(Queries, Commands, transaction, Files, Substitute.For<IHostFileUploadWriter>(),
            new DocumentOfficePreviewConversionProviderResolver([], options), clock,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.MySql }), options);
    }
}
