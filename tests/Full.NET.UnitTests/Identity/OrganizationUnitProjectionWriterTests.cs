using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Features.OrganizationUnitProjection;
using Full.NET.Modules.Organization.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class OrganizationUnitProjectionWriterTests
{
    [TestMethod]
    public async Task ApplyAsync_ignores_older_version_after_newer_projection_exists()
    {
        var command = Substitute.For<ICommandExecutor>();
        var transaction = new RecordingTransaction();
        var clock = Substitute.For<IClock>();
        var tenantId = Guid.CreateVersion7();
        var unitId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        clock.UtcNow.Returns(now);
        command.ExecuteAsync(
                OrganizationUnitProjectionSql.UpdateIfNewer,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(0);
        var writer = new OrganizationUnitProjectionWriter(command, transaction, clock);
        var newer = new OrganizationUnitChangedIntegrationEvent(
            tenantId,
            unitId,
            "New Name",
            true,
            3,
            now);
        var older = newer with { Version = 2, Name = "Stale" };

        await writer.ApplyAsync(newer, CancellationToken.None);
        await writer.ApplyAsync(older, CancellationToken.None);

        Assert.AreEqual(2, transaction.ExecutionCount);
        await command.Received(2).ExecuteAsync(
            OrganizationUnitProjectionSql.UpdateIfNewer,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
        await command.Received(2).ExecuteAsync(
            OrganizationUnitProjectionSql.InsertIfMissing,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    private sealed class RecordingTransaction : ICommandTransaction
    {
        public int ExecutionCount { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return await action(cancellationToken);
        }
    }
}
