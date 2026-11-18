using System.Data.Common;
using System.Net;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.ManageMfaRecoveryCodes;
using Full.NET.Modules.Identity.Features.OAuthFlow;
using Full.NET.Modules.Identity.Http;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>真实双库验证恢复码拒绝保留、消费审计失败回滚和协议拒绝的安全投影。</summary>
[TestClass]
public sealed class AuthenticationAuditRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Password_recovery_rolls_back_proof_and_password_when_success_audit_fails(DatabaseProvider provider)
    {
        var connectionString = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connectionString, IdentityOidcProtocolAssertions.Settings);
        await factory.InitializeAsync();
        var identity = await factory.CreateHostIdentityAsync("recovery-rollback-" + Guid.NewGuid().ToString("N"),
            [], password: FullNetApiFactory.TestPassword);
        await using DbConnection db = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(connectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        await db.OpenAsync();
        var userId = identity.UserId;
        Assert.AreEqual(1, await db.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM fn_identity_refresh_session WHERE UserId=@UserId AND RevokedAtUtc IS NULL", new { UserId = userId }));
        var before = await db.QuerySingleAsync<(string PasswordHash, string SecurityStamp)>(
            "SELECT PasswordHash, SecurityStamp FROM fn_identity_user WHERE Id=@UserId", new { UserId = userId });
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var recovery = await scope.ServiceProvider.GetRequiredService<MfaRecoveryCodeService>().RegenerateAsync(userId);
        Assert.IsTrue(recovery.IsSuccess);
        var accounts = scope.ServiceProvider.GetRequiredService<IdentityOidcAccountService>();

        // 成功审计故障发生在密码和认证权威更新之后，必须连同证明消费一起回滚。
        await db.ExecuteAsync("ALTER TABLE fn_identity_auth_audit ADD CONSTRAINT CK_Test_PasswordAuditFault CHECK (EventType <> 'password.recovery')");
        try
        {
            await Assert.ThrowsAsync<Exception>(async () =>
                await accounts.RecoverAsync(identity.Username, recovery.Value!.RecoveryCodes[0], "Recovered!2026Identity", default));
        }
        finally
        {
            await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
                ? "ALTER TABLE fn_identity_auth_audit DROP CONSTRAINT CK_Test_PasswordAuditFault"
                : "ALTER TABLE fn_identity_auth_audit DROP CHECK CK_Test_PasswordAuditFault");
        }
        var after = await db.QuerySingleAsync<(string PasswordHash, string SecurityStamp)>(
            "SELECT PasswordHash, SecurityStamp FROM fn_identity_user WHERE Id=@UserId", new { UserId = userId });
        Assert.AreEqual(before, after);
        Assert.AreEqual(1, await db.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM fn_identity_refresh_session WHERE UserId=@UserId AND RevokedAtUtc IS NULL", new { UserId = userId }));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM fn_identity_user_mfa_recovery_code WHERE UserId=@UserId AND ConsumedAtUtc IS NOT NULL",
            new { UserId = userId }));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM fn_identity_auth_audit WHERE UserId=@UserId AND EventType='mfa.recovery_code_consumed' AND Succeeded=1",
            new { UserId = userId }));
        Assert.IsTrue((await accounts.RecoverAsync(identity.Username, recovery.Value!.RecoveryCodes[0],
            "Recovered!2026Identity", default)).IsSuccess);
        Assert.AreEqual(0, await db.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM fn_identity_refresh_session WHERE UserId=@UserId AND RevokedAtUtc IS NULL", new { UserId = userId }));
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Recovery_consumption_rolls_back_when_audit_storage_fails_and_rejections_are_retained(DatabaseProvider provider)
    {
        var connectionString = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var settings = new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings)
            { ["Identity:Oidc:ProtocolRateLimitPermitLimitPerMinute"] = "1" };
        using var factory = new FullNetApiFactory(provider, connectionString, settings);
        await factory.InitializeAsync();
        await using DbConnection db = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(connectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        await db.OpenAsync();
        var userId = await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_identity_user WHERE ScopeKey='host' AND NormalizedUsername='ADMIN'");
        string code;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var result = await scope.ServiceProvider.GetRequiredService<MfaRecoveryCodeService>().RegenerateAsync(userId);
            Assert.IsTrue(result.IsSuccess);
            code = result.Value!.RecoveryCodes[0];
        }

        // 约束故障只针对本次消费事件；恢复码状态必须随审计失败一起回滚。
        await db.ExecuteAsync("ALTER TABLE fn_identity_auth_audit ADD CONSTRAINT CK_Test_ConsumeAuditFault CHECK (EventType <> 'mfa.recovery_code_consumed')");
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            await Assert.ThrowsAsync<Exception>(async () =>
                await scope.ServiceProvider.GetRequiredService<MfaRecoveryCodeService>().ConsumeAsync(userId, code));
        }
        finally
        {
            await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
                ? "ALTER TABLE fn_identity_auth_audit DROP CONSTRAINT CK_Test_ConsumeAuditFault"
                : "ALTER TABLE fn_identity_auth_audit DROP CHECK CK_Test_ConsumeAuditFault");
        }
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_identity_user_mfa_recovery_code WHERE UserId=@UserId AND ConsumedAtUtc IS NOT NULL", new { UserId = userId }));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var recovery = scope.ServiceProvider.GetRequiredService<MfaRecoveryCodeService>();
            Assert.IsTrue((await recovery.ConsumeAsync(userId, code)).IsSuccess);
            Assert.IsFalse((await recovery.ConsumeAsync(userId, code)).IsSuccess);
        }
        var consumption = (await db.QueryAsync<(int Succeeded, string ResultCode)>(
            "SELECT CASE WHEN Succeeded=1 THEN 1 ELSE 0 END AS Succeeded,ResultCode FROM fn_identity_auth_audit WHERE UserId=@UserId AND EventType=@EventType",
            new { UserId = userId, EventType = AuthenticationEventCatalog.RecoveryCodeConsumed })).ToArray();
        Assert.AreEqual(1, consumption.Count(row => row.Succeeded == 1));
        Assert.AreEqual(1, consumption.Count(row => row.Succeeded == 0));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var rejected = await scope.ServiceProvider.GetRequiredService<OAuthFlowService>().HandleCallbackAsync(
                "secret-authorization-code", "invalid-state", "http", "localhost", new ClientRequestContext(null, null));
            Assert.IsTrue(rejected.IsSuccess);
            Assert.IsTrue(rejected.Value!.RedirectUrl.Contains("oauth_error", StringComparison.Ordinal));
        }
        Assert.AreEqual(1, await db.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM fn_identity_auth_audit WHERE EventType=@EventType AND Succeeded=0 AND UserId IS NULL",
            new { EventType = AuthenticationEventCatalog.OAuthCallbackRejected }));
        using var client = factory.CreateClientForHost("localhost");
        using var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["client_id"] = "unknown-audit-client", ["refresh_token"] = "secret-raw-refresh"
        }));
        Assert.IsTrue(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized);
        var protocol = (await db.QueryAsync<(Guid? UserId, string? ClientId, string ResultCode)>(
            "SELECT UserId,ClientId,ResultCode FROM fn_identity_auth_audit WHERE EventType=@EventType",
            new { EventType = AuthenticationEventCatalog.ProtocolRejected })).ToArray();
        Assert.IsTrue(protocol.Length > 0);
        Assert.IsTrue(protocol.All(row => row.UserId is null && row.ClientId is null && !row.ResultCode.Contains("secret", StringComparison.Ordinal)));
        using var limited = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["grant_type"] = "refresh_token", ["client_id"] = "unknown-audit-client", ["refresh_token"] = "secret-raw-refresh" }));
        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.AreEqual(protocol.Length, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_identity_auth_audit WHERE EventType=@EventType",
            new { EventType = AuthenticationEventCatalog.ProtocolRejected }));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM fn_identity_auth_audit WHERE ResultCode LIKE '%secret-raw-refresh%' OR UsernameFingerprint LIKE '%secret-raw-refresh%'"));
    }
}
