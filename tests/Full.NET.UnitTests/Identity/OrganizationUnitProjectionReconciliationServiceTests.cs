using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.OrganizationUnitProjection;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class OrganizationUnitProjectionReconciliationServiceTests
{
    [TestMethod]
    public async Task DryRun_reports_missing_without_writer_commands()
    {
        var tenantId = Guid.CreateVersion7();
        var unitId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var source = Substitute.For<IIdentityOrganizationUnitProjectionSource>();
        source.ListAsync(tenantId, null, 100, Arg.Any<CancellationToken>())
            .Returns(Result<IdentityOrganizationUnitProjectionPage>.Success(
                new IdentityOrganizationUnitProjectionPage(
                    [new IdentityOrganizationUnitProjectionSnapshot(unitId, "Missing", true, 1, now)],
                    unitId,
                    false)));
        var query = new StubQueryExecutor(Array.Empty<OrganizationUnitProjectionRecord>());
        var command = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var writer = new OrganizationUnitProjectionWriter(command, new RecordingTransaction(), clock);
        var service = new OrganizationUnitProjectionReconciliationService(source, query, writer);
        var result = await service.ReconcileAsync(
            tenantId,
            null,
            100,
            IdentityOrganizationUnitProjectionReconciliationModes.DryRun,
            CancellationToken.None);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value!.Missing);
        Assert.AreEqual(0, result.Value.Applied);
        await command.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default);
    }

    [TestMethod]
    public async Task Invalid_mode_and_page_size_are_rejected()
    {
        var tenantId = Guid.CreateVersion7();
        var source = Substitute.For<IIdentityOrganizationUnitProjectionSource>();
        var query = new StubQueryExecutor(Array.Empty<OrganizationUnitProjectionRecord>());
        var command = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>();
        var writer = new OrganizationUnitProjectionWriter(command, new RecordingTransaction(), clock);
        var service = new OrganizationUnitProjectionReconciliationService(source, query, writer);
        var invalidMode = await service.ReconcileAsync(tenantId, null, 10, "offset", CancellationToken.None);
        var invalidPageSize = await service.ReconcileAsync(
            tenantId,
            null,
            101,
            IdentityOrganizationUnitProjectionReconciliationModes.DryRun,
            CancellationToken.None);
        Assert.IsFalse(invalidMode.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.OrganizationUnitProjectionInvalidMode, invalidMode.Error!.Code);
        Assert.IsFalse(invalidPageSize.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.OrganizationUnitProjectionInvalidPageSize, invalidPageSize.Error!.Code);
    }

    private sealed class StubQueryExecutor(
        IReadOnlyList<OrganizationUnitProjectionRecord> localRows)
        : IQueryExecutor
    {
        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(OrganizationUnitProjectionSql.FindByTenantAndUnits, statement);
            return Task.FromResult<IReadOnlyList<T>>(localRows.Cast<T>().ToArray());
        }
    }

    private sealed class RecordingTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) =>
            action(cancellationToken);
    }
}