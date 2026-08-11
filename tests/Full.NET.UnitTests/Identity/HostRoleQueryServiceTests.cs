using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostRoles;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostRoleQueryServiceTests
{
    private static readonly Guid RoleId = Guid.CreateVersion7();

    [TestMethod]
    public async Task ListAsync_includes_permission_codes_for_each_role()
    {
        var query = new StubQueryExecutor(
            [
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
            ],
            [
                new IdentityRolePermission(
                    RoleId,
                    IdentityUserManagementPermissions.Read),
                new IdentityRolePermission(
                    RoleId,
                    IdentityRoleManagementPermissions.Read)
            ]);
        var service = new HostRoleQueryService(
            query,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));

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

    private sealed class StubQueryExecutor(
        IReadOnlyList<HostRoleListRow> roles,
        IReadOnlyList<IdentityRolePermission> permissions) : IQueryExecutor
    {
        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(IdentitySql.CountHostRoles, statement);
            return Task.FromResult<T?>((T)(object)(long)roles.Count);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (statement == IdentitySql.ListHostRolesSqlServer)
            {
                return Task.FromResult<IReadOnlyList<T>>(
                    roles.Cast<T>().ToArray());
            }

            Assert.AreEqual(
                IdentitySql.ListRolePermissionsByRoleIds,
                statement);
            return Task.FromResult<IReadOnlyList<T>>(
                permissions.Cast<T>().ToArray());
        }
    }
}
