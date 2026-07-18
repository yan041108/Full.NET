using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

internal static class SuperAdministratorConcurrencyAssertions
{
    public static async Task VerifyAsync(
        Api.FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        Guid firstUserId;
        Guid secondUserId;
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            setupScope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var bootstrap = setupScope.ServiceProvider
                .GetRequiredService<IIdentityBootstrapService>();
            var second = await bootstrap.BootstrapHostAdminAsync(
                new BootstrapHostAdminRequest(
                    "admin-second",
                    Api.FullNetApiFactory.TestPassword,
                    "第二超级管理员"),
                cancellationToken);
            Assert.IsTrue(second.IsSuccess);

            var query = setupScope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            firstUserId = (await query.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindUserByScopeAndUsername,
                new { ScopeKey = "host", NormalizedUsername = "ADMIN" },
                cancellationToken))!.Id;
            secondUserId = (await query.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindUserByScopeAndUsername,
                new { ScopeKey = "host", NormalizedUsername = "ADMIN-SECOND" },
                cancellationToken))!.Id;
        }

        var results = await Task.WhenAll(
            RevokeAsync(factory, firstUserId, secondUserId, cancellationToken),
            RevokeAsync(factory, secondUserId, firstUserId, cancellationToken));

        Assert.AreEqual(1, results.Count(result =>
            result.IsSuccess && result.Value!.Changed));
        Assert.AreEqual(1, results.Count(result => !result.IsSuccess));
        Assert.IsTrue(results.Where(result => !result.IsSuccess).All(result =>
            result.Error?.Code is "identity.super_administrator.last_remaining"
                or "identity.super_administrator.operator_required"));

        await using var verifyScope = factory.Services.CreateAsyncScope();
        verifyScope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var verifyQuery = verifyScope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var activeCount = await verifyQuery.QuerySingleOrDefaultAsync<long>(
            IdentitySql.CountActiveSuperAdministrators,
            cancellationToken: cancellationToken);
        Assert.AreEqual(1L, activeCount);
        var auditCount = await verifyQuery.QuerySingleOrDefaultAsync<long>(
            new SqlStatement(
                "test.count-super-administrator-audits",
                """
                SELECT COUNT(*) FROM fn_identity_auth_audit
                WHERE EventType = 'identity.super_administrator.revoked'
                """,
                SqlDataScope.HostOnly),
            cancellationToken: cancellationToken);
        Assert.AreEqual(1L, auditCount);
    }

    private static async Task<Full.NET.Abstractions.Results.Result<
        SuperAdministratorChangeResponse>> RevokeAsync(
        Api.FullNetApiFactory factory,
        Guid operatorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        return await scope.ServiceProvider
            .GetRequiredService<ISuperAdministratorService>()
            .RevokeAsync(operatorUserId, targetUserId, cancellationToken);
    }
}
