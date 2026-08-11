using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostRoles;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostRoleQueryServiceTests
{
    private static readonly Guid RoleId = Guid.CreateVersion7();

    [TestMethod]
    public async Task ListAsync_includes_permission_codes_for_each_role()
    {
        var query = Substitute.For<IQueryExecutor>();
        var service = new HostRoleQueryService(
            query,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));

        query.QuerySingleOrDefaultAsync<long>(
                IdentitySql.CountHostRoles,
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(1);
        query.QueryAsync<HostRoleListRow>(
                IdentitySql.ListHostRolesSqlServer,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns([
                new HostRoleListRow
                {
                    Id = RoleId,
                    Code = "host-viewer",
                    Name = "E2E 只读查看",
                    IsSystem = false,
                    IsActive = true,
                    IsSuperAdministrator = false,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = null,
                    Version = 2
                }
            ]);
        query.QueryAsync<IdentityRolePermission>(
                IdentitySql.ListRolePermissionsByRoleIds,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns([
                new IdentityRolePermission(RoleId, IdentityUserManagementPermissions.Read),
                new IdentityRolePermission(RoleId, IdentityRoleManagementPermissions.Read)
            ]);

        var result = await service.ListAsync(1, 20);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(1, result.Value!.Items);
        CollectionAssert.AreEqual(
            new[]
            {
                IdentityRoleManagementPermissions.Read,
                IdentityUserManagementPermissions.Read
            },
            result.Value.Items[0].PermissionCodes.ToArray());
    }
}
