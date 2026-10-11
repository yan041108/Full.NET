using System.Data.Common;
using System.Diagnostics.Metrics;
using Dapper;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Budgets;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>生产 DI 与独立数据库连接验证原子竞争、跨月、价格快照及未知计量补录。</summary>
internal static class AiOperationBudgetAssertions
{
    internal sealed class TestClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }

    internal static async Task VerifyAsync(FullNetApiFactory factory, TestClock clock)
    {
        await factory.InitializeAsync();
        await using var db = Connection(factory);
        var now = clock.UtcNow.UtcDateTime;
        var model = Guid.CreateVersion7();
        await db.ExecuteAsync("""
            INSERT INTO fn_ai_model_config (Id, TenantId, Name, ProviderKey, EndpointBaseUrl, ModelId,
                IsDefault, IsEnabled, CreatedAtUtc, Version)
            VALUES (@Id, NULL, 'budget test', 'ollama', 'https://provider.test', 'model', 0, 1, @Now, 1)
            """, new { Id = model, Now = now });
        var price = Guid.CreateVersion7();
        await db.ExecuteAsync("""
            INSERT INTO fn_ai_model_price (Id, ModelConfigId, ProviderKey, ModelId, Currency, InputPerMillion,
                OutputPerMillion, CachedInputPerMillion, ValidFromUtc)
            VALUES (@Id, @Model, 'ollama', 'model', 'USD', 2, 4, 1, @Now)
            """, new { Id = price, Model = model, Now = now.AddDays(-1) });
        // 更新版本但大小写不同，既不能触发 SQL Server 排序规则冲突，也不能误选更晚的错误价格。
        await db.ExecuteAsync("""
            INSERT INTO fn_ai_model_price (Id, ModelConfigId, ProviderKey, ModelId, Currency, InputPerMillion,
                OutputPerMillion, CachedInputPerMillion, ValidFromUtc)
            VALUES (@Id, @Model, 'ollama', 'MODEL', 'USD', 200, 400, 100, @Now)
            """, new { Id = Guid.CreateVersion7(), Model = model, Now = now });
        var options = factory.Services.GetRequiredService<IOptions<AiOperationBudgetOptions>>().Value;
        options.MonthlyRequestLimit = 1;
        var request = new AiOperationRequest(Guid.CreateVersion7(), null, model, "chat", new string('A', 64), 20, 10,
            ProviderKey: "ollama", ModelId: "model");
        var competing = await Task.WhenAll(TryReserve(request), TryReserve(request with { OperationId = Guid.CreateVersion7() }));
        Assert.AreEqual(1, competing.Count(item => item is not null));
        var winner = competing.Single(item => item is not null)!;
        Assert.AreEqual(0.00008m, winner.ReservedCost);
        var accepted = request with { OperationId = winner.OperationId };
        Assert.IsFalse((await Use(store => store.ReserveAsync(accepted))).IsNew);
        await Assert.ThrowsAsync<AiBudgetException>(() => Use(store => store.ReserveAsync(accepted with { RequestHash = new string('B', 64) })));
        await Settle(winner.OperationId, new(null, null), "failed");
        Assert.AreEqual(30L, await Charged(winner.OperationId));
        var originalMonth = clock.UtcNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        clock.UtcNow = clock.UtcNow.AddMonths(1);
        await Use(store => store.ReserveAsync(request with { OperationId = Guid.CreateVersion7() }));
        await Settle(winner.OperationId, new(2, 1, 1), "failed");
        await Settle(winner.OperationId, new(2, 1, 1), "failed");
        await Settle(winner.OperationId, new(null, null), "failed");
        Assert.AreEqual(3L, await Charged(winner.OperationId));
        Assert.AreEqual(originalMonth, await db.QuerySingleAsync<string>("SELECT QuotaMonthKey FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = winner.OperationId }));
        Assert.AreEqual(0.000007m, await db.QuerySingleAsync<decimal>("SELECT ChargedCost FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = winner.OperationId }));
        await Assert.ThrowsAsync<AiBudgetException>(() => Settle(winner.OperationId, new(1, 1), "failed"));

        options.MonthlyRequestLimit = 100;
        options.RunRequestLimit = 1;
        var run = Guid.CreateVersion7();
        await Use(store => store.ReserveAsync(request with { OperationId = Guid.CreateVersion7(), RunId = run }));
        clock.UtcNow = clock.UtcNow.AddMonths(1);
        await Assert.ThrowsAsync<AiBudgetException>(() => Use(store => store.ReserveAsync(request with { OperationId = Guid.CreateVersion7(), RunId = run })));

        // 可信租户测试上下文直接注入数据作用域，不用伪造 HTTP 身份替代认证验证。
        var tenant = Guid.CreateVersion7();
        var isolated = await Use(store => store.ReserveAsync(request with { OperationId = Guid.CreateVersion7() }), tenant);
        Assert.IsTrue(isolated.IsNew);
        await Assert.ThrowsAsync<AiBudgetException>(() => Use(async store => { await store.SettleAsync(winner.OperationId, new(1, 1), "succeeded"); return true; }, tenant));
        Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE ScopeKey=@Scope", new { Scope = tenant.ToString("N") }));

        // 外层生产事务抛错时，scope/操作预留都回滚；相同 OperationId 随后可以重新预留。
        var retry = request with { OperationId = Guid.CreateVersion7() };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var transaction = scope.ServiceProvider.GetRequiredService<Full.NET.Abstractions.Messaging.ICommandTransaction>();
            await Assert.ThrowsAsync<IOException>(() => transaction.ExecuteAsync<bool>(async token =>
            {
                await scope.ServiceProvider.GetRequiredService<IAiOperationBudgetStore>().ReserveAsync(retry, token);
                throw new IOException("simulated persistence boundary failure");
            }, CancellationToken.None));
        }
        Assert.IsTrue((await Use(store => store.ReserveAsync(retry))).IsNew);
        // 结算写入后事务中断，回执和额度同时回滚，再次调用只结算一次。
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var transaction = scope.ServiceProvider.GetRequiredService<Full.NET.Abstractions.Messaging.ICommandTransaction>();
            await Assert.ThrowsAsync<IOException>(() => transaction.ExecuteAsync<bool>(async token =>
            {
                await scope.ServiceProvider.GetRequiredService<IAiOperationBudgetStore>().SettleAsync(retry.OperationId, new(1, 1), "succeeded", token);
                throw new IOException("simulated receipt commit failure");
            }, CancellationToken.None));
        }
        Assert.AreEqual(30L, await Charged(retry.OperationId));
        await Settle(retry.OperationId, new(1, 1), "succeeded");
        Assert.AreEqual(2L, await Charged(retry.OperationId));

        // 未执行操作的完整零用量释放租户兼容 Token 预留；请求次数仍按账本请求计数。
        await db.ExecuteAsync("""
            INSERT INTO fn_ai_tenant_quota (Id,TenantId,MonthlyTokenLimit,MonthlyRequestLimit,UsedTokensThisMonth,UsedRequestsThisMonth,QuotaMonthKey,IsEnabled,CreatedAtUtc,UpdatedAtUtc,Version)
            VALUES (@Id,@Tenant,10000,100,0,0,@Month,1,@Now,@Now,1)
            """, new { Id = Guid.CreateVersion7(), Tenant = tenant, Month = clock.UtcNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), Now = clock.UtcNow });
        var unused = request with { OperationId = Guid.CreateVersion7() };
        await Use(store => store.ReserveAsync(unused), tenant);
        Assert.AreEqual(30L, await db.QuerySingleAsync<long>("SELECT UsedTokensThisMonth FROM fn_ai_tenant_quota WHERE TenantId=@Tenant", new { Tenant = tenant }));
        await Use(async store => { await store.SettleAsync(unused.OperationId, new(0, 0), "cancelled"); return true; }, tenant);
        await Use(async store => { await store.SettleAsync(unused.OperationId, new(0, 0), "cancelled"); return true; }, tenant);
        Assert.AreEqual(0L, await Charged(unused.OperationId));
        Assert.AreEqual("known", await db.QuerySingleAsync<string>("SELECT UsageStatus FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = unused.OperationId }));
        Assert.AreEqual(0m, await db.QuerySingleAsync<decimal>("SELECT ChargedCost FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = unused.OperationId }));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT UsedTokensThisMonth FROM fn_ai_tenant_quota WHERE TenantId=@Tenant", new { Tenant = tenant }));
        Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT UsedRequestsThisMonth FROM fn_ai_tenant_quota WHERE TenantId=@Tenant", new { Tenant = tenant }));

        options.MonthlyRequestLimit = 1000;
        await VerifyCommitObservationsAsync(factory, db, request);

        async Task<AiOperationReservation?> TryReserve(AiOperationRequest value)
        {
            try { return await Use(store => store.ReserveAsync(value)); }
            catch (AiBudgetException error) when (error.Code == "ai.budget.limit_exceeded") { return null; }
        }
        Task<long> Charged(Guid id) => db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = id });
        Task<bool> Settle(Guid id, AiOperationUsage usage, string outcome) => Use(async store => { await store.SettleAsync(id, usage, outcome); return true; });
        async Task<T> Use<T>(Func<IAiOperationBudgetStore, Task<T>> action, Guid? tenantId = null)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            if (tenantId is { } id) context.SetTenant(new TenantContext(id, "budget-test", "预算测试"));
            else context.SetHost();
            try { return await action(scope.ServiceProvider.GetRequiredService<IAiOperationBudgetStore>()); }
            finally { context.Clear(); }
        }
    }

    private static async Task VerifyCommitObservationsAsync(FullNetApiFactory factory, DbConnection db, AiOperationRequest template)
    {
        var facts = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == AiOperationBudgetStore.MeterName) current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags) if (tag.Key == "status") facts.Add((string)tag.Value!);
        });
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) => facts.Add("cost"));
        listener.Start();
        var request = template with { OperationId = Guid.CreateVersion7() };
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var transaction = scope.ServiceProvider.GetRequiredService<Full.NET.Abstractions.Messaging.ICommandTransaction>();
        var store = scope.ServiceProvider.GetRequiredService<IAiOperationBudgetStore>();
        // 验证的是物理提交，而不是嵌套 ExecuteAsync 返回；异常后数据库和指标必须同时不留成功事实。
        await Assert.ThrowsAsync<IOException>(() => transaction.ExecuteAsync<bool>(async token =>
        {
            await store.ReserveAsync(request, token);
            Assert.HasCount(0, facts);
            throw new IOException("late reservation failure");
        }, CancellationToken.None));
        Assert.HasCount(0, facts);
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = request.OperationId }));
        await transaction.ExecuteAsync(async token =>
        {
            Assert.IsTrue((await store.ReserveAsync(request, token)).IsNew);
            Assert.HasCount(0, facts);
            return true;
        }, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "reserved" }, facts);
        facts.Clear();
        await Assert.ThrowsAsync<IOException>(() => transaction.ExecuteAsync<bool>(async token =>
        {
            await store.SettleAsync(request.OperationId, new(2, 1), "succeeded", token);
            Assert.HasCount(0, facts);
            throw new IOException("late settlement failure");
        }, CancellationToken.None));
        Assert.HasCount(0, facts);
        Assert.AreEqual("reserved", await db.QuerySingleAsync<string>("SELECT UsageStatus FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = request.OperationId }));
        await transaction.ExecuteAsync(async token =>
        {
            await store.SettleAsync(request.OperationId, new(2, 1), "succeeded", token);
            Assert.HasCount(0, facts);
            return true;
        }, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "succeeded", "cost" }, facts);
        Assert.AreEqual(3L, await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = request.OperationId }));
        await store.SettleAsync(request.OperationId, new(2, 1), "succeeded");
        CollectionAssert.AreEqual(new[] { "succeeded", "cost" }, facts);
    }

    internal static DbConnection Connection(FullNetApiFactory factory) => factory.Provider == DatabaseProvider.SqlServer
        ? new SqlConnection(factory.ConnectionString)
        : new MySqlConnection(MySqlConnectionStringPolicy.Create(factory.ConnectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
}
