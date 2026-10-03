using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Seeding;
using Full.NET.Seeding.Abstractions;
using Full.NET.Seeding.Dapper;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>首次开发播种必须等待本地租户，只写本地成员且在成功或失败后恢复可信上下文。</summary>
[TestClass]
public sealed class DevelopmentBootstrapAdminTenantMembershipSeedContributorTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid LocalTenantId = Guid.CreateVersion7();
    private static readonly Guid OtherTenantId = Guid.CreateVersion7();
    private static readonly SeedContext Context = new(Guid.CreateVersion7(), SeedProfile.Development, "Development", "zh-CN", "seed-test");

    [TestMethod]
    public void Development_waits_for_local_tenant_and_other_profiles_exclude_the_overlay()
    {
        var fixture = CreateFixture();
        IDataSeedContributor[] contributors = [fixture.Contributor,
            new Dependency("identity.bootstrap_admin_tenant_membership", SeedProfile.Baseline),
            new Dependency("tenancy.local_tenant", SeedProfile.Development)];
        var ordered = SeedContributorGraph.Order(contributors, SeedProfile.Development);
        Assert.AreEqual(fixture.Contributor.Name, ordered[^1].Name);
        foreach (var profile in new[] { SeedProfile.Baseline, SeedProfile.Demo, SeedProfile.Test })
            Assert.IsFalse(SeedContributorGraph.Order(contributors, profile).Contains(fixture.Contributor));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Only_trusted_local_tenant_is_written_and_host_context_is_restored(bool includeLocal)
    {
        var fixture = CreateFixture(includeLocal);
        var result = await fixture.Contributor.SeedAsync(Context);
        Assert.AreEqual(includeLocal ? 1 : 0, result.CreatedCount);
        Assert.AreEqual(0, result.UpdatedCount);
        fixture.Tenant.DidNotReceive().SetTenant(Arg.Is<TenantContext>(tenant => tenant != null && tenant.Id == OtherTenantId));
        fixture.Tenant.Received(2).SetHost();
        if (includeLocal)
        {
            fixture.Tenant.Received(1).SetTenant(Arg.Is<TenantContext>(tenant => tenant != null && tenant.Id == LocalTenantId));
            await fixture.Command.Received(1).ExecuteAsync(TenantMembershipSql.InsertMember,
                Arg.Is<object>(parameters => ReadSqlParameter<Guid>(parameters, "UserId") == UserId), Arg.Any<CancellationToken>());
        }
        else
            await fixture.Command.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    public async Task Existing_active_member_is_skipped_without_rewriting_role_or_audit()
    {
        var fixture = CreateFixture();
        fixture.Query.QuerySingleOrDefaultAsync<TenantMemberRecord>(TenantMembershipSql.FindMemberByTenantAndUser,
            Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(new TenantMemberRecord(Guid.CreateVersion7(), LocalTenantId,
                UserId, TenantMemberRoles.Owner, TenantMemberStatuses.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 2));
        var result = await fixture.Contributor.SeedAsync(Context);
        Assert.AreEqual(1, result.SkippedCount);
        await fixture.Command.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    public async Task Failed_write_restores_previous_tenant_context()
    {
        var fixture = CreateFixture();
        fixture.Tenant.IsHost.Returns(false);
        fixture.Tenant.Id.Returns(OtherTenantId);
        fixture.Tenant.Identifier.Returns("previous");
        fixture.Tenant.Name.Returns("Previous");
        fixture.Command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("seed write failed")));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Contributor.SeedAsync(Context));
        fixture.Tenant.Received(1).SetTenant(new TenantContext(OtherTenantId, "previous", "Previous"));
    }

    private static Fixture CreateFixture(bool includeLocal = true)
    {
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var tenant = Substitute.For<ICurrentTenantContextWriter>();
        tenant.IsHost.Returns(true);
        query.QuerySingleOrDefaultAsync<IdentityUserRecord>(IdentitySql.FindUserByScopeAndUsername,
            Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(new IdentityUserRecord { Id = UserId, IsActive = true });
        List<BootstrapAdminTenantMembershipSeedContributor.ActiveTenantSeedRow> tenants =
            [new(OtherTenantId, "other", "Other")];
        if (includeLocal) tenants.Add(new(LocalTenantId, "local", "Local"));
        query.QueryAsync<BootstrapAdminTenantMembershipSeedContributor.ActiveTenantSeedRow>(IdentityBaselineSeedSql.ListActiveTenants,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(tenants);
        var contributor = new DevelopmentBootstrapAdminTenantMembershipSeedContributor(query, command, tenant,
            Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), Options.Create(new IdentityOptions
            { Bootstrap = new IdentityBootstrapOptions { Username = "admin" } }));
        return new(contributor, query, command, tenant);
    }

    private sealed record Fixture(DevelopmentBootstrapAdminTenantMembershipSeedContributor Contributor,
        IQueryExecutor Query, ICommandExecutor Command, ICurrentTenantContextWriter Tenant);

    private sealed record Dependency(string Name, SeedProfile Profile) : IDataSeedContributor
    {
        public int Version => 1;
        public IReadOnlySet<SeedProfile> Profiles { get; } = new HashSet<SeedProfile> { Profile };
        public IReadOnlyCollection<string> Dependencies => [];
        public Task<SeedContributionResult> SeedAsync(SeedContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SeedContributionResult(0, 0, 1, "seeding.data.skipped"));
    }
}
