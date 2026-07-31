using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Features.ManageHostSerialRules;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.SerialNumbers;

/// <summary>在真实双库上验证流水号原子性、作用域、幂等与 UTC 重置语义。</summary>
internal static class SerialNumberAllocationAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        MutableClock clock,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        var acme = await ReadTenantAsync(factory, "acme", cancellationToken);
        var beta = await ProvisionTenantAsync(
            factory,
            "beta-" + Guid.NewGuid().ToString("N")[..8],
            cancellationToken);

        var ordersRule = await CreateRuleAsync(
            factory,
            new CreateSerialNumberRuleRequest(
                "orders.daily",
                "租户订单号",
                null,
                SerialNumberRuleScope.Tenant,
                SerialNumberResetInterval.Day,
                "ORD-{utc:yyyy}{utc:MM}{utc:dd}-{tenant}-{sequence:4}",
                1,
                9999,
                10,
                true),
            cancellationToken);
        await CreateRuleAsync(
            factory,
            new CreateSerialNumberRuleRequest(
                "shipments.global",
                "全局发运号",
                null,
                SerialNumberRuleScope.Host,
                SerialNumberResetInterval.Never,
                "SHP-{sequence:4}",
                1,
                9999,
                20,
                true),
            cancellationToken);
        await CreateRuleAsync(
            factory,
            new CreateSerialNumberRuleRequest(
                "limited.global",
                "边界号",
                null,
                SerialNumberRuleScope.Host,
                SerialNumberResetInterval.Never,
                "LIM-{sequence:1}",
                1,
                2,
                30,
                true),
            cancellationToken);

        var concurrent = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(index => AllocateAsync(
                factory,
                acme,
                "orders.daily",
                $"order-{index}",
                cancellationToken)));
        Assert.IsTrue(concurrent.All(result => result.IsSuccess));
        CollectionAssert.AreEquivalent(
            Enumerable.Range(1, 20).Select(value => (long)value).ToArray(),
            concurrent.Select(result => result.Value!.SequenceValue).ToArray());
        await VerifyAllocationSemanticsLockedAsync(
            factory,
            ordersRule,
            cancellationToken);

        var replayed = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => AllocateAsync(
                factory,
                acme,
                "orders.daily",
                "same-request",
                cancellationToken)));
        Assert.IsTrue(replayed.All(result => result.IsSuccess));
        Assert.AreEqual(
            1,
            replayed.Select(result => result.Value!.SerialNumber).Distinct().Count());

        var betaFirst = await AllocateAsync(
            factory,
            beta,
            "orders.daily",
            "beta-first",
            cancellationToken);
        Assert.IsTrue(betaFirst.IsSuccess);
        Assert.AreEqual(1, betaFirst.Value!.SequenceValue);

        var hostFromAcme = await AllocateAsync(
            factory,
            acme,
            "shipments.global",
            "global-acme",
            cancellationToken);
        var hostFromBeta = await AllocateAsync(
            factory,
            beta,
            "shipments.global",
            "global-beta",
            cancellationToken);
        Assert.AreEqual(1, hostFromAcme.Value!.SequenceValue);
        Assert.AreEqual(2, hostFromBeta.Value!.SequenceValue);

        clock.UtcNow = clock.UtcNow.AddDays(1);
        var nextBucket = await AllocateAsync(
            factory,
            acme,
            "orders.daily",
            "next-day",
            cancellationToken);
        Assert.IsTrue(nextBucket.IsSuccess);
        Assert.AreEqual(1, nextBucket.Value!.SequenceValue);
        Assert.AreNotEqual(
            concurrent[0].Value!.ResetBucket,
            nextBucket.Value.ResetBucket);

        var limitedFirst = await AllocateAsync(
            factory,
            acme,
            "limited.global",
            "limited-1",
            cancellationToken);
        var limitedSecond = await AllocateAsync(
            factory,
            beta,
            "limited.global",
            "limited-2",
            cancellationToken);
        var exhausted = await AllocateAsync(
            factory,
            acme,
            "limited.global",
            "limited-3",
            cancellationToken);
        Assert.IsTrue(limitedFirst.IsSuccess);
        Assert.IsTrue(limitedSecond.IsSuccess);
        Assert.IsFalse(exhausted.IsSuccess);
        Assert.AreEqual(
            SerialNumberErrorCodes.SequenceExhausted,
            exhausted.Error!.Code);
    }

    public static void ConfigureClock(
        IServiceCollection services,
        MutableClock clock)
    {
        services.AddSingleton<IClock>(clock);
    }

    private static async Task<SerialNumberRuleResponse> CreateRuleAsync(
        FullNetApiFactory factory,
        CreateSerialNumberRuleRequest request,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var result = await scope.ServiceProvider
            .GetRequiredService<HostSerialRuleService>()
            .CreateAsync(Guid.CreateVersion7(), request, cancellationToken);
        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        return result.Value!;
    }

    private static async Task VerifyAllocationSemanticsLockedAsync(
        FullNetApiFactory factory,
        SerialNumberRuleResponse rule,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var result = await scope.ServiceProvider
            .GetRequiredService<HostSerialRuleService>()
            .UpdateAsync(
                rule.Id,
                Guid.CreateVersion7(),
                new UpdateSerialNumberRuleRequest(
                    rule.DisplayName,
                    rule.Description,
                    rule.Scope,
                    SerialNumberResetInterval.Never,
                    "ORD-{tenant}-{sequence:4}",
                    rule.MinimumValue,
                    rule.MaximumValue,
                    rule.DisplayOrder,
                    rule.IsEnabled,
                    rule.Version),
                cancellationToken);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            SerialNumberErrorCodes.RuleSemanticsLocked,
            result.Error!.Code);

        var metadataOnly = await scope.ServiceProvider
            .GetRequiredService<HostSerialRuleService>()
            .UpdateAsync(
                rule.Id,
                Guid.CreateVersion7(),
                new UpdateSerialNumberRuleRequest(
                    rule.DisplayName + " v2",
                    "Metadata remains editable after allocation.",
                    rule.Scope,
                    rule.ResetInterval,
                    rule.Pattern,
                    rule.MinimumValue,
                    rule.MaximumValue,
                    rule.DisplayOrder + 1,
                    rule.IsEnabled,
                    rule.Version),
                cancellationToken);
        Assert.IsTrue(metadataOnly.IsSuccess, metadataOnly.Error?.Code);
    }

    private static async Task<
        Full.NET.Abstractions.Results.Result<SerialNumberAllocation>>
        AllocateAsync(
            FullNetApiFactory factory,
            TenantContext tenant,
            string ruleKey,
            string idempotencyKey,
            CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider
            .GetRequiredService<CurrentTenantAccessor>()
            .SetTenant(tenant);
        return await scope.ServiceProvider
            .GetRequiredService<ISerialNumberAllocator>()
            .AllocateAsync(ruleKey, idempotencyKey, cancellationToken);
    }

    private static async Task<TenantContext> ReadTenantAsync(
        FullNetApiFactory factory,
        string identifier,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var row = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>()
            .QuerySingleOrDefaultAsync<TenantRow>(
                new SqlStatement(
                    "test.serial_numbers.find_tenant",
                    """
                    SELECT Id, Identifier, Name
                    FROM fn_tenancy_tenant
                    WHERE Identifier = @Identifier
                    """,
                    SqlDataScope.Global),
                new { Identifier = identifier },
                cancellationToken);
        Assert.IsNotNull(row);
        return new TenantContext(row.Id, row.Identifier, row.Name);
    }

    private static async Task<TenantContext> ProvisionTenantAsync(
        FullNetApiFactory factory,
        string identifier,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var result = await scope.ServiceProvider
            .GetRequiredService<
                Full.NET.Modules.Tenancy.Contracts.ITenantProvisioningService>()
            .ProvisionAsync(
                new Full.NET.Modules.Tenancy.Contracts.ProvisionTenantRequest(
                    identifier,
                    identifier,
                    $"{identifier}.localhost"),
                cancellationToken);
        Assert.IsTrue(result.IsSuccess);
        return await ReadTenantAsync(factory, identifier, cancellationToken);
    }

    internal sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class TenantRow
    {
        public Guid Id { get; set; }

        public string Identifier { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }
}
