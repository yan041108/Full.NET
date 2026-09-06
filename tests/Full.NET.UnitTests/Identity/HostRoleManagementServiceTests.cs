using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostRoles;
using Full.NET.Modules.Identity.FieldProjection;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Organization;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostRoleManagementServiceTests
{
    private static readonly Guid RoleId = Guid.CreateVersion7();
    private static readonly Guid ActorUserId = Guid.CreateVersion7();

    [TestMethod]
    public async Task Copy_rejects_super_administrator_source()
    {
        var sourceRoleId = Guid.CreateVersion7();
        var fixture = new Fixture();
        fixture.Query.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindHostRoleById,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var parameters = callInfo.Arg<object>();
                var roleId = ExtractRoleId(parameters);
                if (roleId == sourceRoleId)
                {
                    return new IdentityRoleRecord(
                        sourceRoleId,
                        null,
                        "host",
                        "host-administrator",
                        "超级管理员",
                        true,
                        true,
                        true,
                        RoleDataScopeKinds.All,
                        DateTimeOffset.UtcNow,
                        null,
                        1);
                }

                return fixture.DefaultRole;
            });

        var result = await fixture.Service.CopyAsync(
            sourceRoleId,
            ActorUserId,
            new CopyHostRoleRequest("copied-role", "复制角色"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.RoleCopySourceNotAllowed, result.Error!.Code);
        await fixture.Command.DidNotReceive().ExecuteAsync(
            IdentitySql.InsertRole,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Copy_intersects_permissions_with_actor_when_not_super_administrator()
    {
        var sourceRoleId = Guid.CreateVersion7();
        var fixture = new Fixture();
        fixture.Query.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindHostRoleById,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var roleId = ExtractRoleId(callInfo.Arg<object>());
                return roleId == sourceRoleId
                    ? fixture.DefaultRole with { Id = sourceRoleId, Code = "source", Name = "源角色" }
                    : fixture.DefaultRole;
            });
        fixture.PermissionSnapshots.ReadAsync(
                ActorUserId,
                "host",
                null,
                Arg.Any<CancellationToken>())
            .Returns(new PermissionSnapshot(
                [IdentityUserManagementPermissions.Read],
                false));
        fixture.Query.QueryAsync<string>(
                IdentitySql.GetRolePermissionCodes,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var roleId = ExtractRoleId(callInfo.Arg<object>());
                return roleId == sourceRoleId
                    ? new[]
                    {
                        IdentityUserManagementPermissions.Read,
                        IdentityUserManagementPermissions.ResetPassword,
                    }
                    : new[] { IdentityUserManagementPermissions.Read };
            });
        fixture.Query.QueryAsync<HostRoleListRow>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<HostRoleListRow>());

        var result = await fixture.Service.CopyAsync(
            sourceRoleId,
            ActorUserId,
            new CopyHostRoleRequest("copied-role", "复制角色"));

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(
            new[] { IdentityUserManagementPermissions.Read },
            result.Value!.PermissionCodes.ToArray());
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.EnsureRolePermission,
            Arg.Is<IdentityRolePermission>(item =>
                item != null
                && item.PermissionCode == IdentityUserManagementPermissions.Read),
            Arg.Any<CancellationToken>());
        await fixture.Command.DidNotReceive().ExecuteAsync(
            IdentitySql.EnsureRolePermission,
            Arg.Is<IdentityRolePermission>(item =>
                item != null
                && item.PermissionCode == IdentityUserManagementPermissions.ResetPassword),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Copy_creates_non_system_role_without_super_administrator_flag()
    {
        var sourceRoleId = Guid.CreateVersion7();
        var fixture = new Fixture();
        fixture.Query.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindHostRoleById,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var roleId = ExtractRoleId(callInfo.Arg<object>());
                return roleId == sourceRoleId
                    ? fixture.DefaultRole with
                    {
                        Id = sourceRoleId,
                        DataScopeKind = RoleDataScopeKinds.Self,
                    }
                    : fixture.DefaultRole;
            });
        fixture.Query.QueryAsync<string>(
                IdentitySql.GetRolePermissionCodes,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns([IdentityUserManagementPermissions.Read]);

        var result = await fixture.Service.CopyAsync(
            sourceRoleId,
            ActorUserId,
            new CopyHostRoleRequest("copied-role", "复制角色"));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.Value!.IsSystem);
        Assert.IsFalse(result.Value.IsSuperAdministrator);
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.InsertRole,
            Arg.Is<InsertIdentityRole>(role =>
                role != null
                && role.IsSystem == false
                && role.IsSuperAdministrator == false
                && role.DataScopeKind == RoleDataScopeKinds.Self),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ReplacePermissions_rejects_action_without_parent_page_permission()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ReplacePermissionsAsync(
            RoleId,
            new ReplaceHostRolePermissionsRequest(
                [IdentityUserManagementPermissions.ResetPassword],
                3));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.ActionRequiresPage, result.Error!.Code);
        await fixture.Command.DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    public async Task ReplacePermissions_preserves_tenant_permission_for_cross_context_host_role()
    {
        var fixture = new Fixture();
        fixture.Query.QueryAsync<string>(
                IdentitySql.GetRolePermissionCodes,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns([OrganizationUnitManagementPermissions.Read]);

        var result = await fixture.Service.ReplacePermissionsAsync(
            RoleId,
            new ReplaceHostRolePermissionsRequest(
                [OrganizationUnitManagementPermissions.Read],
                3));

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(
            new[] { OrganizationUnitManagementPermissions.Read },
            result.Value!.PermissionCodes.ToArray());
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.EnsureRolePermission,
            Arg.Is<IdentityRolePermission>(item =>
                item != null
                && item.PermissionCode == OrganizationUnitManagementPermissions.Read),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ReplacePermissions_rejects_blank_permission_code()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ReplacePermissionsAsync(
            RoleId,
            new ReplaceHostRolePermissionsRequest(
                [IdentityUserManagementPermissions.Read, "   "],
                3));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ValidationErrorCodes.Failed, result.Error!.Code);
        await fixture.Command.DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    public async Task ReplacePermissions_rejects_duplicate_permission_code_after_trimming()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ReplacePermissionsAsync(
            RoleId,
            new ReplaceHostRolePermissionsRequest(
                [
                    IdentityUserManagementPermissions.Read,
                    $" {IdentityUserManagementPermissions.Read} ",
                ],
                3));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ValidationErrorCodes.Failed, result.Error!.Code);
        await fixture.Command.DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    public async Task ReplacePermissions_persists_page_and_action_permissions_together()
    {
        var fixture = new Fixture();
        fixture.Query.QueryAsync<string>(
                IdentitySql.GetRolePermissionCodes,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(
            [
                IdentityUserManagementPermissions.Read,
                IdentityUserManagementPermissions.ResetPassword,
            ]);

        var result = await fixture.Service.ReplacePermissionsAsync(
            RoleId,
            new ReplaceHostRolePermissionsRequest(
                [
                    IdentityUserManagementPermissions.ResetPassword,
                    IdentityUserManagementPermissions.Read,
                ],
                3));

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(
            new[]
            {
                IdentityUserManagementPermissions.Read,
                IdentityUserManagementPermissions.ResetPassword,
            },
            result.Value!.PermissionCodes.ToArray());
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.UpdateHostRoleVersion,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.DeleteRolePermissions,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.EnsureRolePermission,
            Arg.Is<IdentityRolePermission>(item =>
                item != null
                && item.PermissionCode == IdentityUserManagementPermissions.Read),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.EnsureRolePermission,
            Arg.Is<IdentityRolePermission>(item =>
                item != null
                && item.PermissionCode == IdentityUserManagementPermissions.ResetPassword),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.RotateSecurityStampsByRole,
            Arg.Is<object>(parameters =>
                parameters != null
                && ReadSqlParameter<Guid>(parameters, "RoleId") == RoleId),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.RevokeSessionsByRole,
            Arg.Is<object>(parameters =>
                parameters != null
                && ReadSqlParameter<Guid>(parameters, "RoleId") == RoleId),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Disable_revokes_sessions_and_rotates_security_stamps_for_role_members()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.DisableAsync(RoleId);

        Assert.IsTrue(result.IsSuccess);
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.DisableHostRole,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.RotateSecurityStampsByRole,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.RevokeSessionsByRole,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Enable_activates_inactive_custom_role()
    {
        var fixture = new Fixture();
        fixture.Query.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindHostRoleById,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(fixture.DefaultRole with { IsActive = false });

        var result = await fixture.Service.EnableAsync(RoleId);

        Assert.IsTrue(result.IsSuccess);
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.EnableHostRole,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Enable_rejects_system_role()
    {
        var fixture = new Fixture();
        fixture.Query.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindHostRoleById,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(fixture.DefaultRole with { IsSystem = true });

        var result = await fixture.Service.EnableAsync(RoleId);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.RoleSystemLocked, result.Error!.Code);
        await fixture.Command.DidNotReceive().ExecuteAsync(
            IdentitySql.EnableHostRole,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Delete_rejects_role_with_members()
    {
        var fixture = new Fixture();
        fixture.Query.QuerySingleOrDefaultAsync<long>(
                IdentitySql.CountHostRoleMembers,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(2);

        var result = await fixture.Service.DeleteAsync(RoleId);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.RoleHasMembers, result.Error!.Code);
        await fixture.Command.DidNotReceive().ExecuteAsync(
            IdentitySql.DeleteHostRole,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Delete_cascades_permissions_and_field_grants_when_no_members()
    {
        var fixture = new Fixture();
        fixture.Query.QuerySingleOrDefaultAsync<long>(
                IdentitySql.CountHostRoleMembers,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await fixture.Service.DeleteAsync(RoleId);

        Assert.IsTrue(result.IsSuccess);
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.DeleteRolePermissions,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.DeleteAllHostRoleFieldGrants,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.DeleteHostRole,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Query = Substitute.For<IQueryExecutor>();
            DefaultRole = new IdentityRoleRecord(
                RoleId,
                null,
                "host",
                "auditor",
                "Auditor",
                false,
                true,
                false,
                RoleDataScopeKinds.All,
                DateTimeOffset.UtcNow,
                null,
                3);
            Query.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                    IdentitySql.FindHostRoleById,
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(DefaultRole);
            Query.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                    IdentitySql.FindRoleByScopeAndCode,
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns((IdentityRoleRecord?)null);
            Query.QueryAsync<IdentityRoleFieldGrantRow>(
                    IdentitySql.ListHostRoleFieldGrantRowsByRoleId,
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(Array.Empty<IdentityRoleFieldGrantRow>());
            Query.QueryAsync<Guid>(
                    IdentitySql.GetRoleDataScopeUnitIds,
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(Array.Empty<Guid>());
            Command = Substitute.For<ICommandExecutor>();
            Command.ExecuteAsync(
                    Arg.Any<SqlStatement>(),
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(1);
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(DateTimeOffset.Parse("2026-08-02T00:00:00Z"));
            var ids = Substitute.For<IIdGenerator>();
            ids.NewId().Returns(_ => Guid.CreateVersion7());
            var catalog = AuthorizationCatalog.Create(
                [
                    new IdentityAuthorizationContributor(),
                    new TenancyAuthorizationContributor(),
                    new OrganizationAuthorizationContributor(),
                ]);
            PermissionSnapshots = Substitute.For<IPermissionSnapshotReader>();
            PermissionSnapshots.ReadAsync(
                    Arg.Any<Guid>(),
                    "host",
                    null,
                    Arg.Any<CancellationToken>())
                .Returns(new PermissionSnapshot(
                    catalog.Permissions
                        .Where(permission => (permission.Scope & AuthorizationScope.Host) != 0)
                        .Select(permission => permission.Code)
                        .ToArray(),
                    true));
            var roleQueries = new HostRoleQueryService(
                Query,
                Options.Create(new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                    ConnectionString = "Server=.;Database=test;",
                }));
            Service = new HostRoleManagementService(
                Query,
                Command,
                new PassThroughTransaction(),
                roleQueries,
                catalog,
                FieldProjectionCatalog.CreateDefault(),
                PermissionSnapshots,
                clock,
                ids);
        }

        public IdentityRoleRecord DefaultRole { get; }

        public ICommandExecutor Command { get; }

        public IPermissionSnapshotReader PermissionSnapshots { get; }

        public IQueryExecutor Query { get; }

        public HostRoleManagementService Service { get; }
    }

    private static Guid ExtractRoleId(object? parameters)
    {
        if (parameters is Dictionary<string, object?> dictionary
            && dictionary.TryGetValue("RoleId", out var value)
            && value is Guid roleId)
        {
            return roleId;
        }

        return Guid.Empty;
    }

    private sealed class PassThroughTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }
}
