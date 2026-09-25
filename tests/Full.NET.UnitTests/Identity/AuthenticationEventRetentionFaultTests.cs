using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Retention;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class AuthenticationEventRetentionFaultTests
{
    [TestMethod]
    public async Task Failed_batch_does_not_advance_runner_and_next_iteration_can_retry()
    {
        var options = new AuthenticationEventRetentionOptions
        {
            Enabled = true,
            RetentionDays = 365,
            BatchSize = 2,
            MaxBatchesPerRun = 1,
        };
        var commands = Substitute.For<ICommandExecutor>();
        var calls = 0;
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => ++calls == 1
                ? Task.FromException<int>(new TimeoutException("database temporarily unavailable"))
                : Task.FromResult(1));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero));
        var monitor = Substitute.For<IOptionsMonitor<AuthenticationEventRetentionOptions>>();
        monitor.CurrentValue.Returns(options);
        var runner = new AuthenticationEventRetentionRunner(
            Substitute.For<IQueryExecutor>(), commands,
            Substitute.For<ICommandTransaction>(), clock,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }), monitor);

        await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            runner.RunOnceAsync(options, CancellationToken.None));
        var recovered = await runner.RunOnceAsync(options, CancellationToken.None);

        Assert.AreEqual(1, recovered.Deleted);
        Assert.AreEqual(1, recovered.Batches);
        Assert.AreEqual(2, calls);
    }
}
