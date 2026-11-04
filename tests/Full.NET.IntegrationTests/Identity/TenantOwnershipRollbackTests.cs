using System.Net;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Features.ManageTenantOwnership;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.IntegrationTests.Identity;
using Full.NET.IntegrationTests.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>在真实本地事务的两个写入阶段注入失败，核对职责、回执和测试撤销事实均回滚。</summary>
[TestClass]
public sealed class TenantOwnershipRollbackTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.MySql, false)]
    [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, true)]
    public async Task Failed_target_CAS_or_late_session_loss_rolls_back_handoff(DatabaseProvider provider, bool sessionLoss)
    {
        var fault = new Fault { RevokeAfterReceipt = sessionLoss };
        using var f = await TenantOwnershipHandoffTests.Fixture.CreateAsync(provider, services =>
        {
            var registration = services.Last(item => item.ServiceType == typeof(ICommandExecutor)); services.Remove(registration);
            services.AddScoped<ICommandExecutor>(sp => new FaultingExecutor(ResolveOriginal(sp, registration),
                sp.GetRequiredService<IQueryExecutor>(), fault));
        });
        var source = await f.AddPersonAsync("rollback-source", Full.NET.Modules.Identity.Contracts.TenantMemberRoles.Owner);
        var target = await f.AddPersonAsync("rollback-target"); var token = await f.TenantTokenAsync(source);
        fault.SourceMember = source.MemberId; fault.TargetMember = target.MemberId; fault.Actor = source.UserId;
        fault.Session = Guid.Parse(new JsonWebToken(token).GetClaim("sid").Value);
        var request = new TransferTenantOwnerRequest(Guid.CreateVersion7(), source.MemberId, 1, target.MemberId, 1, FullNetApiFactory.TestPassword, null);
        using var response = await f.TransferAsync(token, request);
        Assert.AreEqual(sessionLoss ? HttpStatusCode.Forbidden : HttpStatusCode.Conflict, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(1, fault.Hits); Assert.AreEqual("Admin", fault.SourceRoleInsideTransaction);
        if (sessionLoss) Assert.AreEqual(1L, fault.ReceiptCountInsideTransaction);
        var owner = (await f.StateAsync(token)).Owner!;
        Assert.AreEqual(source.MemberId, owner.Id); Assert.AreEqual(1, owner.Version);
        var receiving = await f.MemberAsync(token, target.MemberId);
        Assert.AreEqual("Member", receiving.MemberRole); Assert.AreEqual(1, receiving.Version);
        using var missing = await f.SendAsync(token, System.Net.Http.HttpMethod.Get,
            $"/api/v1/identity/tenant-members/ownership/operations/{request.OperationId:D}");
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode); Assert.AreEqual(1L, await f.OwnerCountAsync());
    }

    private static ICommandExecutor ResolveOriginal(IServiceProvider sp, ServiceDescriptor registration) =>
        registration.ImplementationInstance as ICommandExecutor
        ?? registration.ImplementationFactory?.Invoke(sp) as ICommandExecutor
        ?? (ICommandExecutor)ActivatorUtilities.CreateInstance(sp, registration.ImplementationType!);
    private sealed class Fault
    {
        internal Guid SourceMember, TargetMember, Actor, Session;
        internal bool RevokeAfterReceipt;
        internal int Hits;
        internal string? SourceRoleInsideTransaction;
        internal long ReceiptCountInsideTransaction;
    }
    private sealed class FaultingExecutor(ICommandExecutor inner, IQueryExecutor queries, Fault fault) : ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (!fault.RevokeAfterReceipt && statement.Name == TenantMembershipSql.UpdateMember.Name
                && parameters is IReadOnlyDictionary<string, object?> values && Equals(values["MemberId"], fault.TargetMember)
                && Equals(values["MemberRole"], "Owner") && Interlocked.CompareExchange(ref fault.Hits, 1, 0) == 0)
            {
                fault.SourceRoleInsideTransaction = await ReadSourceAsync(cancellationToken);
                return 0;
            }
            var changed = await inner.ExecuteAsync(statement, parameters, cancellationToken);
            if (fault.RevokeAfterReceipt && statement.Name == TenantOwnershipSql.InsertReceipt.Name
                && parameters is IReadOnlyDictionary<string, object?> receipt && Interlocked.CompareExchange(ref fault.Hits, 1, 0) == 0)
            {
                fault.SourceRoleInsideTransaction = await ReadSourceAsync(cancellationToken);
                fault.ReceiptCountInsideTransaction = await queries.QuerySingleOrDefaultAsync<long>(new SqlStatement("test.owner.pending_receipt",
                    "SELECT COUNT(1) FROM fn_identity_tenant_owner_transfer WHERE TenantId=@TenantId AND OperationId=@OperationId",
                    SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId), IdentitySqlParameters.Create(("OperationId", receipt["OperationId"])), cancellationToken);
                // 测试专属撤销事实与角色写入共用真实事务；最终权威复核必须拒绝，并将这次注入一起回滚。
                await inner.ExecuteAsync(new SqlStatement("test.owner.revoke_pending_session", """
                    UPDATE fn_identity_refresh_session SET RevokedAtUtc=@Now,Version=Version+1
                    WHERE Id=@SessionId AND UserId=@UserId
                    """, SqlDataScope.Global), IdentitySqlParameters.Create(("Now", DateTimeOffset.UtcNow),
                        ("SessionId", fault.Session), ("UserId", fault.Actor)), cancellationToken);
            }
            return changed;
        }
        private Task<string?> ReadSourceAsync(CancellationToken token) => queries.QuerySingleOrDefaultAsync<string>(new SqlStatement("test.owner.pending_source",
            "SELECT MemberRole FROM fn_identity_tenant_member WHERE TenantId=@TenantId AND Id=@MemberId",
            SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId), IdentitySqlParameters.Create(("MemberId", fault.SourceMember)), token);
    }
}
