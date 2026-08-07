using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ManageTenantUnits;
using Full.NET.Modules.Organization.Features.ManageTenantUserUnits;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Organization;

[TestClass]
public sealed class TenantUserUnitManagementServiceTests
{
    [TestMethod]
    public async Task Create_does_not_start_transaction_when_host_user_not_found()
    {
        var transaction = new RecordingTransaction();
        var hostUserDirectory = Substitute.For<IHostUserDirectory>();
        var userId = Guid.CreateVersion7();
        hostUserDirectory
            .FindActiveHostUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns((HostUserDirectoryEntry?)null);
        var service = CreateService(
            Substitute.For<IQueryExecutor>(),
            Substitute.For<ICommandExecutor>(),
            transaction,
            hostUserDirectory);

        var result = await service.CreateAsync(
            new CreateOrganizationUserUnitRequest(userId, Guid.CreateVersion7(), false));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(OrganizationErrorCodes.UserUnitUserNotFound, result.Error!.Code);
        Assert.AreEqual(0, transaction.ExecutionCount);
    }

    [TestMethod]
    public async Task Create_does_not_start_transaction_when_directory_throws()
    {
        var transaction = new RecordingTransaction();
        var hostUserDirectory = Substitute.For<IHostUserDirectory>();
        var userId = Guid.CreateVersion7();
        hostUserDirectory
            .FindActiveHostUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns<HostUserDirectoryEntry?>(_ => throw new InvalidOperationException("directory unavailable"));
        var service = CreateService(
            Substitute.For<IQueryExecutor>(),
            Substitute.For<ICommandExecutor>(),
            transaction,
            hostUserDirectory);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.CreateAsync(
                new CreateOrganizationUserUnitRequest(userId, Guid.CreateVersion7(), false)));

        Assert.AreEqual(0, transaction.ExecutionCount);
    }

    [TestMethod]
    public async Task Create_returns_failure_inside_transaction_when_unit_is_inactive()
    {
        var transaction = new RecordingTransaction();
        var command = Substitute.For<ICommandExecutor>();
        var query = Substitute.For<IQueryExecutor>();
        var hostUserDirectory = Substitute.For<IHostUserDirectory>();
        var userId = Guid.CreateVersion7();
        var unitId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        hostUserDirectory
            .FindActiveHostUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new HostUserDirectoryEntry(userId, "user", "用户"));
        query.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(
                OrganizationSql.FindUnitById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new OrganizationUnitRecord(
                unitId,
                tenantId,
                null,
                "sales",
                "Sales",
                1,
                false,
                DateTimeOffset.UtcNow,
                null,
                1));
        var service = CreateService(query, command, transaction, hostUserDirectory);

        var result = await service.CreateAsync(
            new CreateOrganizationUserUnitRequest(userId, unitId, false));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(OrganizationErrorCodes.UnitNotFound, result.Error!.Code);
        Assert.AreEqual(1, transaction.ExecutionCount);
        await command.DidNotReceiveWithAnyArgs().ExecuteAsync(
            default!,
            default,
            default);
    }

    private static TenantUserUnitManagementService CreateService(
        IQueryExecutor query,
        ICommandExecutor command,
        RecordingTransaction transaction,
        IHostUserDirectory hostUserDirectory)
    {
        var databaseOptions = Options.Create(new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
        });
        var unitQueries = new TenantUnitQueryService(
            query,
            Substitute.For<IUserDataScopeResolver>(),
            Substitute.For<IDataScopeSqlFilterBuilder>(),
            databaseOptions);
        var assignmentQueries = new TenantUserUnitQueryService(
            query,
            Substitute.For<IHostUserDisplayDirectory>(),
            Substitute.For<IUserDataScopeResolver>(),
            Substitute.For<IDataScopeSqlFilterBuilder>(),
            databaseOptions);
        return new TenantUserUnitManagementService(
            query,
            command,
            transaction,
            assignmentQueries,
            unitQueries,
            hostUserDirectory,
            CreateTenantContext(),
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());
    }

    private static ICurrentTenant CreateTenantContext()
    {
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsAvailable.Returns(true);
        tenant.IsHost.Returns(false);
        tenant.Id.Returns(Guid.CreateVersion7());
        return tenant;
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
