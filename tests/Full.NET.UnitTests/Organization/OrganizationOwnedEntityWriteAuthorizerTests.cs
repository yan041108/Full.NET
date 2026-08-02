using Full.NET.Data.Abstractions;
using Full.NET.Modules.Organization.Authorization;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Organization;

[TestClass]
public sealed class OrganizationOwnedEntityWriteAuthorizerTests
{
    [TestMethod]
    public async Task EnsureCanWriteAsync_returns_not_found_when_unit_is_missing()
    {
        var tenantId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var directory = Substitute.For<ITenantOrganizationUnitDirectory>();
        directory
            .FindActiveUnitAsync(tenantId, unitId, Arg.Any<CancellationToken>())
            .Returns((TenantOrganizationUnitDirectoryEntry?)null);
        var authorizer = new OrganizationOwnedEntityWriteAuthorizer(
            directory,
            Substitute.For<IQueryExecutor>());

        var result = await authorizer.EnsureCanWriteAsync(
            tenantId,
            unitId,
            actorId,
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(OrganizationErrorCodes.UnitNotFound, result.Error!.Code);
    }

    [TestMethod]
    public async Task EnsureCanWriteAsync_returns_forbidden_without_active_membership()
    {
        var tenantId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var directory = Substitute.For<ITenantOrganizationUnitDirectory>();
        directory
            .FindActiveUnitAsync(tenantId, unitId, Arg.Any<CancellationToken>())
            .Returns(new TenantOrganizationUnitDirectoryEntry(unitId, "sales", "Sales"));
        var query = Substitute.For<IQueryExecutor>();
        query
            .QuerySingleOrDefaultAsync<OrganizationUserUnitRecord>(
                OrganizationSql.FindUserUnitByTenantUserAndUnit,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns((OrganizationUserUnitRecord?)null);
        var authorizer = new OrganizationOwnedEntityWriteAuthorizer(directory, query);

        var result = await authorizer.EnsureCanWriteAsync(
            tenantId,
            unitId,
            actorId,
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(OrganizationErrorCodes.WriteAccessDenied, result.Error!.Code);
    }

    [TestMethod]
    public async Task EnsureCanWriteAsync_succeeds_with_active_membership()
    {
        var tenantId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var directory = Substitute.For<ITenantOrganizationUnitDirectory>();
        directory
            .FindActiveUnitAsync(tenantId, unitId, Arg.Any<CancellationToken>())
            .Returns(new TenantOrganizationUnitDirectoryEntry(unitId, "sales", "Sales"));
        var query = Substitute.For<IQueryExecutor>();
        query
            .QuerySingleOrDefaultAsync<OrganizationUserUnitRecord>(
                OrganizationSql.FindUserUnitByTenantUserAndUnit,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(new OrganizationUserUnitRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = actorId,
                UnitId = unitId,
                IsActive = true,
            });
        var authorizer = new OrganizationOwnedEntityWriteAuthorizer(directory, query);

        var result = await authorizer.EnsureCanWriteAsync(
            tenantId,
            unitId,
            actorId,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
    }
}