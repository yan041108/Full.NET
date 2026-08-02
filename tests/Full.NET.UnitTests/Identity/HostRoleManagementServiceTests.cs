using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostRoles;
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
                && (Guid)parameters.GetType().GetProperty("RoleId")!.GetValue(parameters)! == RoleId),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.RevokeSessionsByRole,
            Arg.Is<object>(parameters =>
                parameters != null
                && (Guid)parameters.GetType().GetProperty("RoleId")!.GetValue(parameters)! == RoleId),
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

    private sealed class Fixture
    {
        public Fixture()
        {
            Query = Substitute.For<IQueryExecutor>();
            Query.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                    IdentitySql.FindHostRoleById,
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(new IdentityRoleRecord(
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
                    3));
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
                clock,
                ids);
        }

        public ICommandExecutor Command { get; }

        public IQueryExecutor Query { get; }

        public HostRoleManagementService Service { get; }
    }

    private sealed class PassThroughTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }
}
