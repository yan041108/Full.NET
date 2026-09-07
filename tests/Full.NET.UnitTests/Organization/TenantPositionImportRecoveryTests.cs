using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ManageTenantPositions;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Organization;

[TestClass]
public sealed class TenantPositionImportRecoveryTests
{
    [TestMethod]
    public async Task Missing_assignment_rolls_back_position_creation()
    {
        var queries = Substitute.For<IQueryExecutor>();
        var commands = Substitute.For<ICommandExecutor>();
        var coordinator = new RecordingDbTransactionCoordinator();
        var tenant = new CurrentTenantAccessor();
        var tenantId = Guid.NewGuid();
        tenant.SetTenant(new TenantContext(tenantId, "test", "Test"));
        var id = Guid.NewGuid();
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(id);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(1);
        queries.QuerySingleOrDefaultAsync<OrganizationPositionRecord>(PositionSql.FindById, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new OrganizationPositionRecord(id, tenantId, "eng", "Engineer", null, null, null,
                null, null, null, 0, true, DateTimeOffset.UtcNow, null, 1));
        var service = new TenantPositionManagementService(queries, commands, new DapperCommandTransaction(coordinator),
            new TenantPositionQueryService(queries, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer })),
            tenant, clock, ids);
        var result = await service.ImportAsync(new ImportOrganizationPositionsRequest(
            [new ImportOrganizationPositionRow("eng", "Engineer", 0, "missing", null)]),
            new OrganizationPositionImportCapabilities(true, true));
        Assert.IsFalse(result.Value!.Results.Single().Succeeded);
        Assert.AreEqual(0, coordinator.CommitCount);
        Assert.AreEqual(1, coordinator.RollbackCount);
    }
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Committed_row_replays_only_matching_content_without_writes(bool sameContent)
    {
        var queries = Substitute.For<IQueryExecutor>();
        var commands = Substitute.For<ICommandExecutor>();
        var tenant = new CurrentTenantAccessor();
        tenant.SetTenant(new TenantContext(Guid.NewGuid(), "test", "Test"));
        var id = Guid.NewGuid();
        var row = new ImportOrganizationPositionRow("eng", "Engineer", 0, null, null);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(row,
                Full.NET.Modules.Organization.Serialization.OrganizationJsonSerializerContext.Default.ImportOrganizationPositionRow)));
        queries.QuerySingleOrDefaultAsync<PositionImportReceiptRecord>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new PositionImportReceiptRecord(id, sameContent ? hash : new string('B', 64)));
        var service = new TenantPositionManagementService(queries, commands,
            new DapperCommandTransaction(new RecordingDbTransactionCoordinator()),
            new TenantPositionQueryService(queries, Options.Create(new DatabaseOptions())),
            tenant, Substitute.For<IClock>(), Substitute.For<IIdGenerator>());
        var result = await service.ImportTaskRowAsync(Guid.NewGuid(), 1, row,
            new OrganizationPositionImportCapabilities(true, true), CancellationToken.None);
        Assert.AreEqual(sameContent, result.IsSuccess);
        if (sameContent) Assert.AreEqual(id, result.Value!.PositionId);
        Assert.AreEqual(0, commands.ReceivedCalls().Count());
    }
}
