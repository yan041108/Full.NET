using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageRegistrationPolicy;
using Full.NET.Modules.Identity.Features.MyTenantRegistrations;
using Full.NET.Modules.Identity.Features.TenantSeatOperations;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>开放注册必须留下可恢复成员意图；重试不创建第二账号，不复活管理员改动。</summary>
[TestClass]
public sealed class PublicRegistrationMembershipTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Public_registration_retries_original_membership_and_rejects_foreign_or_changed_targets(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var delivery = new CapturingIdentityChallengeDeliveryPort();
        var gate = new SeatGate();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services =>
        {
            services.AddSingleton<IIdentityChallengeDeliveryPort>(delivery);
            services.Replace(ServiceDescriptor.Scoped<ITenantMemberSeatQuotaPort>(sp => new GatedSeats(
                new TenantMemberSeatQuotaPort(sp.GetRequiredService<ITenantQuotaReservationService>()), gate)));
        });
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient, default);
        Guid wayId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var service = scope.ServiceProvider.GetRequiredService<RegistrationPolicyService>();
            var policy = await service.GetAsync(default);
            Assert.IsTrue((await service.UpdateAsync(new UpdateRegistrationPolicyRequest(true, policy.Value!.Version, IdentityRegistrationMode.Open), default)).IsSuccess);
        }
        wayId = await InvitedRegistrationAssertions.CreateRegistrationWayAsync(factory, tenant.Id, default);
        var first = await RegisterAsync();
        Assert.AreEqual("Requested", first.Progress.Phase);
        Assert.IsTrue(first.Progress.CanContinue);
        using (var stale = await client.PostAsJsonAsync($"/api/v1/me/tenant-registrations/{first.Progress.Id}/continue", new { version = first.Progress.Version + 1 }))
            Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        var cancelled = await ContinueAsync(first.Progress);
        Assert.AreEqual("Cancelled", cancelled.Phase);
        Assert.IsNotNull(cancelled.ErrorCode);
        gate.Reject = false;
        var activated = await ContinueAsync(cancelled);
        Assert.AreEqual("Activated", activated.Phase);
        Assert.IsFalse(activated.CanContinue);
        Assert.AreEqual("Activated", (await ContinueAsync(activated)).Phase);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var op = await query.QuerySingleOrDefaultAsync<TenantSeatOperation>(TenantSeatOperationSql.Find, new { Id = activated.Id });
            Assert.AreEqual(2, op!.Attempt);
            Assert.AreEqual(first.UserId, op.UserId);
            Assert.AreEqual(wayId, op.RegistrationWayId);
            var user = await query.QuerySingleOrDefaultAsync<IdentityUserRecord>(AccountLifecycleSql.FindUserByProfileEmail, new { Email = first.Email });
            Assert.AreEqual(first.UserId, user!.Id);
            Assert.IsNull(user.TenantId);
            Assert.AreEqual("host", user.ScopeKey);
            var way = await query.QuerySingleOrDefaultAsync<RegistrationWayRecord>(RegistrationWaySql.FindById, new { WayId = wayId });
            Assert.AreEqual(1, await query.QuerySingleOrDefaultAsync<int>(RegistrationMemberActivationSql.HasRole,
                new { UserId = user.Id, RoleId = way!.RoleId, TenantId = tenant.Id, ScopeKey = $"tenant:{tenant.Id:N}", WayId = wayId, WayVersion = way.Version }));
        }
        var available = await client.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available");
        Assert.IsTrue(available!.Any(row => row.Id == tenant.Id));

        gate.Reject = true;
        client.DefaultRequestHeaders.Authorization = null;
        var second = await RegisterAsync();
        Assert.AreNotEqual(first.UserId, second.UserId);
        var own = await client.GetFromJsonAsync<SelfTenantRegistrationResponse[]>("/api/v1/me/tenant-registrations");
        Assert.AreEqual(1, own!.Length);
        using (var foreign = await client.PostAsJsonAsync($"/api/v1/me/tenant-registrations/{first.Progress.Id}/continue", new { version = activated.Version }))
            Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
        var failed = await ContinueAsync(second.Progress);
        Assert.AreEqual("Cancelled", failed.Phase);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(new SqlStatement(
                "test.registration.admin_change", "UPDATE fn_identity_tenant_member SET Version=Version+1 WHERE UserId=@UserId AND TenantId=@TenantId", SqlDataScope.HostOnly),
                new { second.UserId, TenantId = tenant.Id });
        }
        gate.Reject = false;
        using (var rejected = await client.PostAsJsonAsync($"/api/v1/me/tenant-registrations/{failed.Id}/continue", new { version = failed.Version }))
            Assert.AreEqual(HttpStatusCode.Conflict, rejected.StatusCode);

        async Task<(Guid UserId, string Email, SelfTenantRegistrationResponse Progress)> RegisterAsync()
        {
            var email = $"public-{Guid.NewGuid():N}@example.test";
            using var challenge = await client.PostAsJsonAsync("/api/v1/auth/register/email-challenge", new SendRegistrationEmailChallengeRequest(
                email, IdentityAccountChallengePurpose.RegistrationEmailVerification));
            Assert.AreEqual(HttpStatusCode.OK, challenge.StatusCode, await challenge.Content.ReadAsStringAsync());
            var request = new RegisterAccountRequest(email, "Public member", "FullNet!2026Register", delivery.LastIntent!.ChallengeId, delivery.LastIntent.Credential, wayId);
            using var registered = await client.PostAsJsonAsync("/api/v1/auth/register", request);
            Assert.AreEqual(HttpStatusCode.OK, registered.StatusCode, await registered.Content.ReadAsStringAsync());
            var user = await registered.Content.ReadFromJsonAsync<RegisterAccountResponse>();
            Assert.IsTrue(user!.Created);
            var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, email, request.Password);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var rows = await client.GetFromJsonAsync<SelfTenantRegistrationResponse[]>("/api/v1/me/tenant-registrations");
            Assert.AreEqual(1, rows!.Length);
            Assert.AreEqual(tenant.Id, rows[0].TenantId);
            return (user.UserId, email, rows[0]);
        }
        async Task<SelfTenantRegistrationResponse> ContinueAsync(SelfTenantRegistrationResponse row)
        {
            using var response = await client.PostAsJsonAsync($"/api/v1/me/tenant-registrations/{row.Id}/continue", new { row.Version });
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<SelfTenantRegistrationResponse>())!;
        }
    }

    private sealed class SeatGate { public bool Reject = true; }
    private sealed class GatedSeats(ITenantMemberSeatQuotaPort inner, SeatGate gate) : ITenantMemberSeatQuotaPort
    {
        public Task<Result<bool>> TryReserveAsync(Guid tenantId, string operationId, CancellationToken cancellationToken = default) =>
            gate.Reject ? Task.FromResult(Result<bool>.Success(false)) : inner.TryReserveAsync(tenantId, operationId, cancellationToken);
        public Task<Result<bool>> ConfirmAsync(Guid tenantId, string operationId, CancellationToken cancellationToken = default) => inner.ConfirmAsync(tenantId, operationId, cancellationToken);
        public Task<Result<bool>> ReleaseAsync(Guid tenantId, string operationId, CancellationToken cancellationToken = default) => inner.ReleaseAsync(tenantId, operationId, cancellationToken);
        public Task<Result<bool>> CancelConsumptionAsync(Guid tenantId, string operationId, CancellationToken cancellationToken = default) => inner.CancelConsumptionAsync(tenantId, operationId, cancellationToken);
    }
}
