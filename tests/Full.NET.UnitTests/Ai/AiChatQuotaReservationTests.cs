using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Streaming;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>配额先持久化预留、按原月份幂等结算；不依赖外部模型服务。</summary>
[TestClass]
public sealed class AiChatQuotaReservationTests
{
    /// <summary>数据库拒绝原子预留时不得发放许可或创建预留记录。</summary>
    [TestMethod]
    public async Task Atomic_reservation_rejection_denies_request_async()
    {
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        var guard = Guard(commands);
        var result = await guard.ReserveAsync(Guid.NewGuid(), 5000);
        Assert.IsFalse(result.IsSuccess);
        await commands.DidNotReceive().ExecuteAsync(
            Arg.Is<SqlStatement>(statement => statement!.Name == "ai.insert_quota_reservation"), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>重复结算只允许第一笔变更用量，不能反复退还 Token。</summary>
    [TestMethod]
    public async Task Settlement_is_idempotent_async()
    {
        var commands = Substitute.For<ICommandExecutor>();
        var settled = 0;
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SqlStatement>()!.Name == "ai.settle_quota_reservation"
                ? (Interlocked.Increment(ref settled) == 1 ? 1 : 0) : 1);
        var guard = Guard(commands);
        var reservation = (await guard.ReserveAsync(Guid.NewGuid(), 5000)).Value!;
        await guard.SettleAsync(reservation, 100, 200);
        await guard.SettleAsync(reservation, 100, 200);
        await commands.Received(1).ExecuteAsync(
            Arg.Is<SqlStatement>(statement => statement!.Name == "ai.adjust_reserved_quota_usage"), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>无法确认外部用量时保留全部预留，取消不能绕过配额。</summary>
    [TestMethod]
    public async Task Unknown_usage_does_not_refund_reservation_async()
    {
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var guard = Guard(commands);
        var reservation = (await guard.ReserveAsync(Guid.NewGuid(), 5000)).Value!;
        await guard.SettleAsync(reservation, null, null);
        await commands.Received(1).ExecuteAsync(
            Arg.Is<SqlStatement>(statement => statement!.Name == "ai.adjust_reserved_quota_usage"),
            Arg.Is<object?>(parameters => ((IReadOnlyDictionary<string, object?>)parameters!)["TokenDelta"]!.Equals(0L)), Arg.Any<CancellationToken>());
    }

    /// <summary>建立带真实事务结果语义的配额守卫。</summary>
    /// <param name="commands">模拟原子 SQL 结果的命令执行器。</param>
    private static AiChatQuotaGuard Guard(ICommandExecutor commands)
    {
        var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<AiTenantQuotaRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiTenantQuotaRecord { IsEnabled = true, QuotaMonthKey = "2026-09" });
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero));
        return new AiChatQuotaGuard(queries, commands, new DapperCommandTransaction(new RecordingDbTransactionCoordinator()), clock);
    }
}
