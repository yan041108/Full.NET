using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageTenantQuotas;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>AI 租户配额写入不得把 Identity 租户目录带进本地事务。</summary>
[TestClass]
public sealed class AiTenantQuotaTransactionTests
{
    /// <summary>租户目录读取必须发生在配额写入事务之前。</summary>
    [TestMethod]
    public async Task Tenant_directory_is_checked_before_quota_transaction_async()
    {
        var fixture = CreateFixture();
        fixture.Tenants.IsActiveTenantAsync(fixture.TenantId, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Assert.IsFalse(fixture.Coordinator.HasTransaction);
                Assert.AreEqual(0, fixture.Coordinator.BeginCount);
                return true;
            });

        var result = await fixture.Service.UpsertAsync(
            fixture.TenantId,
            new UpdateAiTenantQuotaRequest(1000, 10, true, 1));

        Assert.IsTrue(result.IsSuccess);
        await fixture.Tenants.Received(1).IsActiveTenantAsync(fixture.TenantId, Arg.Any<CancellationToken>());
        Assert.IsGreaterThanOrEqualTo(1, fixture.Coordinator.CommitCount);
    }

    /// <summary>非活动租户必须在开启事务前失败。</summary>
    [TestMethod]
    public async Task Inactive_tenant_does_not_begin_quota_transaction_async()
    {
        var fixture = CreateFixture();
        fixture.Tenants.IsActiveTenantAsync(fixture.TenantId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await fixture.Service.UpsertAsync(
            fixture.TenantId,
            new UpdateAiTenantQuotaRequest(1000, 10, true, 1));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(AiErrorCodes.TenantNotFound, result.Error!.Code);
        Assert.AreEqual(0, fixture.Coordinator.BeginCount);
        Assert.AreEqual(0, fixture.Store.Quotas.Count);
    }

    /// <summary>建立带真实事务提交语义的配额服务。</summary>
    /// <returns>隔离测试夹具。</returns>
    private static QuotaFixture CreateFixture()
    {
        var tenantId = Guid.NewGuid();
        var store = new QuotaStore();
        var coordinator = new RecordingDbTransactionCoordinator();
        var transaction = new DapperCommandTransaction(coordinator);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 9, 7, 2, 0, 0, TimeSpan.Zero));
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.NewGuid());
        var tenants = Substitute.For<IIdentityActiveTenantDirectory>();
        tenants.IsActiveTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(true);
        var options = Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer });
        return new QuotaFixture(
            tenantId,
            store,
            coordinator,
            tenants,
            new AiTenantQuotaManagementService(
                store,
                store,
                transaction,
                new AiTenantQuotaQueryService(store, options),
                tenants,
                clock,
                ids));
    }

    /// <summary>配额事务测试夹具。</summary>
    /// <param name="TenantId">租户标识。</param>
    /// <param name="Store">内存持久化。</param>
    /// <param name="Coordinator">事务记录器。</param>
    /// <param name="Tenants">权威租户目录替身。</param>
    /// <param name="Service">配额服务。</param>
    private sealed record QuotaFixture(
        Guid TenantId,
        QuotaStore Store,
        RecordingDbTransactionCoordinator Coordinator,
        IIdentityActiveTenantDirectory Tenants,
        AiTenantQuotaManagementService Service);

    /// <summary>按语句名维护租户配额内存状态。</summary>
    private sealed class QuotaStore : IQueryExecutor, ICommandExecutor
    {
        /// <summary>已提交配额。</summary>
        public Dictionary<Guid, AiTenantQuotaRecord> Quotas { get; } = [];

        /// <inheritdoc />
        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            var values = (IReadOnlyDictionary<string, object?>)parameters!;
            if (typeof(T) == typeof(AiTenantQuotaRecord)
                && values.TryGetValue("TenantId", out var tenantId)
                && tenantId is Guid id)
            {
                var match = Quotas.Values.SingleOrDefault(row => row.TenantId == id);
                return Task.FromResult((T?)(object?)match);
            }

            return Task.FromResult(default(T?));
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>([]);

        /// <inheritdoc />
        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            var values = (IReadOnlyDictionary<string, object?>)parameters!;
            if (statement.Name == "ai.insert_tenant_quota")
            {
                var record = new AiTenantQuotaRecord
                {
                    Id = (Guid)values["Id"]!,
                    TenantId = (Guid)values["TenantId"]!,
                    MonthlyTokenLimit = values["MonthlyTokenLimit"] as long?,
                    MonthlyRequestLimit = values["MonthlyRequestLimit"] as long?,
                    UsedTokensThisMonth = (long)values["UsedTokensThisMonth"]!,
                    UsedRequestsThisMonth = (long)values["UsedRequestsThisMonth"]!,
                    QuotaMonthKey = (string)values["QuotaMonthKey"]!,
                    IsEnabled = (bool)values["IsEnabled"]!,
                    CreatedAtUtc = (DateTimeOffset)values["CreatedAtUtc"]!,
                    Version = (int)values["Version"]!,
                };
                Quotas[record.Id] = record;
                return Task.FromResult(1);
            }

            return Task.FromResult(0);
        }
    }
}
