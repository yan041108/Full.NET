using System.Net;
using System.Net.Http.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Tenancy;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.RecoverAccount;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

internal static class PasswordRecoveryConcurrencyAssertions
{
    public static async Task VerifyAsync(FullNetApiFactory factory)
    {
        var delivery = new CapturingIdentityChallengeDeliveryPort();
        using var scopedFactory = new FullNetApiFactory(factory.Provider, factory.ConnectionString,
            configureTestServices: services => services.AddSingleton<Full.NET.Modules.Notifications.Contracts.IIdentityChallengeDeliveryPort>(delivery));
        await scopedFactory.InitializeAsync();
        using var client = scopedFactory.CreateClientForHost("localhost");
        var adminToken = await AccountLifecycleAssertions.LoginAsHostAdminAsync(client, CancellationToken.None);
        var email = $"concurrent-recovery-{Guid.NewGuid():N}@example.com";
        var user = await AccountLifecycleAssertions.CreateHostUserWithEmailAsync(client, adminToken, email, CancellationToken.None);
        using var accepted = await client.PostAsJsonAsync("/api/v1/auth/recover-password",
            new RequestPasswordRecoveryRequest(email));
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
        var intent = delivery.LastIntent;
        Assert.IsNotNull(intent);
        var confirm = new ConfirmCommand(new ConfirmPasswordRecoveryRequest(intent.ChallengeId,
            intent.Credential, "FullNet!2026Recovered"));

        await using var delayedScope = scopedFactory.Services.CreateAsyncScope();
        // 直接处理器复现保持真实匿名 Host 请求上下文，避免会话撤销守卫掩盖竞争。
        delayedScope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var gate = new UserReadGate(delayedScope.ServiceProvider.GetRequiredService<IQueryExecutor>());
        var handler = ActivatorUtilities.CreateInstance<ConfirmHandler>(delayedScope.ServiceProvider, gate);
        var pending = handler.HandleAsync(confirm, CancellationToken.None);
        Exception? failure = null;
        IdentityUserRecord? changed = null;
        try
        {
            await gate.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            // 恢复事务尚未写入；独立连接模拟管理员改密并提交，制造真实的过期账号快照。
            await using var mutationScope = scopedFactory.Services.CreateAsyncScope();
            var commands = mutationScope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            var affected = await commands.ExecuteAsync(new SqlStatement(
                "test.identity_concurrent_recovery_password_change",
                """
                UPDATE fn_identity_user
                SET PasswordHash = @PasswordHash, SecurityStamp = @SecurityStamp, Version = Version + 1
                WHERE Id = @UserId
                """, SqlDataScope.Global), IdentitySqlParameters.Create(
                    ("UserId", user.Id), ("PasswordHash", "independently-committed-password-hash"),
                    ("SecurityStamp", Guid.CreateVersion7().ToString("N"))));
            Assert.AreEqual(1, affected);
            changed = await ReadUserAsync();
        }
        finally
        {
            gate.Release.TrySetResult(true);
            try { await pending.WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (Exception exception) { failure = exception; }
        }
        Assert.IsInstanceOfType<InvalidOperationException>(failure,
            "并发修改必须触发恢复事务回滚，不能覆盖已经提交的新密码。");
        Assert.AreEqual("Password recovery state changed concurrently.", failure!.Message,
            "必须由账号更新冲突触发回滚，不能把其他数据库或作用域错误当成并发保护。");
        Assert.IsNotNull(changed);
        var after = await ReadUserAsync();
        Assert.AreEqual(changed.PasswordHash, after.PasswordHash);
        Assert.AreEqual(changed.SecurityStamp, after.SecurityStamp);
        Assert.AreEqual(changed.Version, after.Version);
        await using (var readScope = scopedFactory.Services.CreateAsyncScope())
        {
            var query = readScope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var challenge = await query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(AccountChallengeSql.FindById,
                IdentitySqlParameters.Create(("ChallengeId", intent.ChallengeId)));
            Assert.IsNotNull(challenge);
            Assert.IsNull(challenge.ConsumedAtUtc, "账号更新失败必须回滚已执行的挑战消费。");
            Assert.AreEqual(1, challenge.Version);
            var auditCount = await query.QuerySingleOrDefaultAsync<int>(new SqlStatement(
                "test.identity_concurrent_recovery_audits",
                "SELECT COUNT(*) FROM fn_identity_auth_audit WHERE UserId = @UserId AND EventType = @EventType",
                SqlDataScope.Global), IdentitySqlParameters.Create(("UserId", user.Id),
                    ("EventType", "password_recovery.completed")));
            Assert.AreEqual(0, auditCount, "回滚事务不能留下改密成功审计。");
        }

        // 新请求重新读取权威版本；被回滚的凭据仍可按既有规则消费一次，并拒绝重放。
        using var retried = await client.PostAsJsonAsync("/api/v1/auth/recover-password/confirm", confirm.Request);
        Assert.AreEqual(HttpStatusCode.NoContent, retried.StatusCode);
        after = await ReadUserAsync();
        Assert.AreNotEqual(changed.PasswordHash, after.PasswordHash);
        Assert.AreNotEqual(changed.SecurityStamp, after.SecurityStamp);
        Assert.AreEqual(changed.Version + 1, after.Version);
        using var replay = await client.PostAsJsonAsync("/api/v1/auth/recover-password/confirm", confirm.Request);
        Assert.AreEqual(HttpStatusCode.BadRequest, replay.StatusCode);

        async Task<IdentityUserRecord> ReadUserAsync()
        {
            await using var readScope = scopedFactory.Services.CreateAsyncScope();
            var record = await readScope.ServiceProvider.GetRequiredService<IQueryExecutor>()
                .QuerySingleOrDefaultAsync<IdentityUserRecord>(AccountLifecycleSql.FindUserByProfileEmail,
                    IdentitySqlParameters.Create(("Email", email)));
            Assert.IsNotNull(record);
            return record;
        }
    }

    private sealed class UserReadGate(IQueryExecutor inner) : IQueryExecutor
    {
        public TaskCompletionSource<bool> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);
            if (statement == AccountLifecycleSql.FindUserByProfileEmail)
            {
                Ready.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default) => inner.QueryAsync<T>(statement, parameters, cancellationToken);
    }
}
