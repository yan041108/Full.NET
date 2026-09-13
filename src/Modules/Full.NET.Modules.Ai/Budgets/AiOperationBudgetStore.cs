using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Streaming;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Budgets;

/// <summary>预算账本归 Ai 所有；同 scope 行锁串行化跨实例计数，外部模型不进入事务。</summary>
internal sealed class AiOperationBudgetStore(IQueryExecutor queries, ICommandExecutor commands, ICommandTransaction transaction,
    ICurrentTenant tenant, IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> database,
    IOptions<AiOperationBudgetOptions> options, AiChatQuotaGuard legacy) : IAiOperationBudgetStore
{
    internal const string MeterName = "Full.NET.Ai.Budgets";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Calls = Meter.CreateCounter<long>("fullnet.ai.operations");
    private static readonly Counter<double> Cost = Meter.CreateCounter<double>("fullnet.ai.charged_cost", "{currency}");

    public async Task<AiOperationReservation> ReserveAsync(AiOperationRequest request, CancellationToken cancellationToken = default)
    {
        try { return await ReserveCoreAsync(request, cancellationToken).ConfigureAwait(false); }
        catch (AiBudgetException) { Calls.Add(1, new KeyValuePair<string, object?>("status", "rejected")); throw; }
        catch (OperationCanceledException) { Calls.Add(1, new KeyValuePair<string, object?>("status", "cancelled")); throw; }
        catch (Exception) { Calls.Add(1, new KeyValuePair<string, object?>("status", "failed")); throw; }
    }

    private async Task<AiOperationReservation> ReserveCoreAsync(AiOperationRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var limits = options.Value;
        // Provider 尚无硬费用上界认证。不能把客户端传来的 Token 估算值当成计量保证。
        if (request.RequireHardCostLimit || limits.MonthlyCostLimit.HasValue || (request.RunId.HasValue && limits.RunCostLimit.HasValue))
        {
            throw new AiBudgetException("ai.budget.hard_cost_unavailable");
        }
        var scope = Scope();
        var fingerprint = Fingerprint(request);
        var result = await transaction.ExecuteAsync(async token =>
        {
            await LockAsync(scope, token).ConfigureAwait(false);
            var existing = await FindAsync(request.OperationId, scope, token).ConfigureAwait(false);
            if (existing is not null)
            {
                if (existing.RequestHash != fingerprint) throw new AiBudgetException("ai.budget.operation_conflict");
                return new AiOperationReservation(existing.Id, false, existing.UsageStatus, existing.ReservedTokens, existing.ReservedCost, existing.Currency);
            }
            var now = clock.UtcNow;
            var month = now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var reserved = checked(request.InputTokenLimit + request.OutputTokenLimit);
            var totals = await queries.QuerySingleOrDefaultAsync<AiOperationBudgetTotals>(AiOperationBudgetSql.Totals,
                AiSqlParameters.Create(("ScopeKey", scope), ("QuotaMonthKey", month), ("RunId", request.RunId)), token).ConfigureAwait(false)
                ?? new AiOperationBudgetTotals();
            if (totals.MonthlyRequests >= limits.MonthlyRequestLimit || totals.MonthlyTokens > limits.MonthlyTokenLimit - reserved
                || (request.RunId.HasValue && (totals.RunRequests >= limits.RunRequestLimit || totals.RunTokens > limits.RunTokenLimit - reserved)))
                throw new AiBudgetException("ai.budget.limit_exceeded");
            var priceRow = await queries.QuerySingleOrDefaultAsync<AiModelPriceRecord>(
                database.Value.Provider == DatabaseProvider.MySql ? AiModelPriceSql.FindMySql : AiModelPriceSql.FindSqlServer,
                AiSqlParameters.Create(("ModelConfigId", request.ModelConfigId), ("ProviderKey", request.ProviderKey), ("ModelId", request.ModelId), ("ScopeTenantId", tenant.Id), ("Now", now)), token).ConfigureAwait(false);
            AiModelPrice? price = priceRow is null ? null : new(priceRow.Id, priceRow.Currency,
                priceRow.InputPerMillion, priceRow.OutputPerMillion, priceRow.CachedInputPerMillion);
            var cost = AiExecutionBudget.CalculateCost(price, new(request.InputTokenLimit, request.OutputTokenLimit));
            AiQuotaReservation? compatibility = null;
            if (tenant.Id.HasValue)
            {
                // 嵌套协调器复用当前短事务：兼容配额拒绝或后续插入失败均回滚全部记账。
                var old = await legacy.ReserveAtAsync(request.OperationId, reserved, now, token).ConfigureAwait(false);
                if (!old.IsSuccess) throw new AiBudgetException(old.Error!.Code);
                compatibility = old.Value;
            }
            var affected = await commands.ExecuteAsync(AiOperationBudgetSql.Insert, AiSqlParameters.Create(
                ("OperationId", request.OperationId), ("ScopeKey", scope), ("RunId", request.RunId), ("ModelConfigId", request.ModelConfigId),
                ("ProviderKey", request.ProviderKey), ("ModelId", request.ModelId), ("RequestHash", fingerprint), ("QuotaMonthKey", month), ("ReservedTokens", reserved), ("ReservedCost", cost),
                ("Currency", price?.Currency), ("PriceVersionId", price?.VersionId), ("InputPerMillion", price?.InputPerMillion),
                ("OutputPerMillion", price?.OutputPerMillion), ("CachedInputPerMillion", price?.CachedInputPerMillion),
                ("LegacyTracked", compatibility?.IsTracked ?? false), ("Now", now), ("TraceId", Activity.Current?.TraceId.ToString())), token).ConfigureAwait(false);
            if (affected != 1) throw new AiBudgetException("ai.budget.persistence_failed");
            return new AiOperationReservation(request.OperationId, true, "reserved", reserved, cost, price?.Currency);
        }, cancellationToken).ConfigureAwait(false);
        if (result.IsNew) Calls.Add(1, new KeyValuePair<string, object?>("status", "reserved"));
        return result;
    }

    public async Task SettleAsync(Guid operationId, AiOperationUsage usage, string outcome, CancellationToken cancellationToken = default)
    {
        AiExecutionBudget.ValidateUsage(usage);
        if (operationId == Guid.Empty || outcome is not ("succeeded" or "failed" or "cancelled"))
            throw new AiBudgetException("ai.budget.invalid_usage");
        var scope = Scope();
        var charged = await transaction.ExecuteAsync(async token =>
        {
            await LockAsync(scope, token).ConfigureAwait(false);
            var row = await FindAsync(operationId, scope, token).ConfigureAwait(false)
                ?? throw new AiBudgetException("ai.budget.operation_not_found");
            var complete = usage.InputTokens.HasValue && usage.OutputTokens.HasValue;
            if (row.UsageStatus == "known")
            {
                // 较旧的 unknown 回执不能覆盖已知事实；相同完整回执是无操作重试。
                if (!complete || (row.InputTokens == usage.InputTokens && row.OutputTokens == usage.OutputTokens
                    && row.CachedInputTokens == usage.CachedInputTokens && row.Outcome == outcome)) return (Cost: (decimal?)null, Currency: (string?)null, Changed: false);
                throw new AiBudgetException("ai.budget.settlement_conflict");
            }
            if (!complete && row.UsageStatus == "unknown") return (Cost: (decimal?)null, Currency: (string?)null, Changed: false);
            AiModelPrice? price = row.PriceVersionId is { } version ? new(version, row.Currency!, row.InputPerMillion!.Value,
                row.OutputPerMillion!.Value, row.CachedInputPerMillion!.Value) : null;
            var tokens = complete ? checked(usage.InputTokens!.Value + usage.OutputTokens!.Value) : row.ReservedTokens;
            var cost = complete ? AiExecutionBudget.CalculateCost(price, usage) : row.ReservedCost;
            var affected = await commands.ExecuteAsync(AiOperationBudgetSql.Settle, AiSqlParameters.Create(
                ("OperationId", operationId), ("ScopeKey", scope), ("UsageStatus", complete ? "known" : "unknown"), ("Outcome", outcome),
                ("InputTokens", complete ? usage.InputTokens : null), ("OutputTokens", complete ? usage.OutputTokens : null),
                ("CachedInputTokens", complete ? usage.CachedInputTokens : null), ("ChargedTokens", tokens), ("ChargedCost", cost), ("Now", clock.UtcNow)), token).ConfigureAwait(false);
            if (affected != 1) throw new AiBudgetException("ai.budget.persistence_failed");
            if (complete && row.LegacyTracked)
                await legacy.SettleAsync(new(row.Id, row.QuotaMonthKey, row.ReservedTokens, true),
                    checked((int)usage.InputTokens!.Value), checked((int)usage.OutputTokens!.Value), token).ConfigureAwait(false);
            return (Cost: complete ? cost : null, Currency: row.Currency, Changed: true);
        }, cancellationToken).ConfigureAwait(false);
        if (charged.Changed) Calls.Add(1, new KeyValuePair<string, object?>("status", outcome));
        // 币种只允许配置中单一受控值进入指标，其余仍在账本留痕；用户/模型/Run 不作为 label。
        if (charged.Cost is { } amount && charged.Currency == options.Value.Currency)
            Cost.Add((double)amount, new KeyValuePair<string, object?>("currency", charged.Currency));
    }

    internal static string Fingerprint(AiOperationRequest request) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        FormattableString.Invariant($"{request.RunId:N}|{request.ModelConfigId:N}|{request.Kind}|{request.RequestHash}|{request.InputTokenLimit}|{request.OutputTokenLimit}|{request.RequireHardCostLimit}|{request.ProviderKey.Length}:{request.ProviderKey}{request.ModelId.Length}:{request.ModelId}"))));

    private void Validate(AiOperationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderKey) || request.ProviderKey.Length > 32 || string.IsNullOrWhiteSpace(request.ModelId) || request.ModelId.Length > 256
            || request.OperationId == Guid.Empty || request.ModelConfigId == Guid.Empty || request.RunId == Guid.Empty
            || request.Kind is not ("chat" or "agent" or "embedding") || request.RequestHash.Length != 64
            || request.RequestHash.Any(c => !char.IsAsciiHexDigit(c)) || request.InputTokenLimit is < 0 or > AiExecutionBudget.MaximumTokens
            || request.OutputTokenLimit is < 0 or > AiExecutionBudget.MaximumTokens || request.InputTokenLimit + request.OutputTokenLimit == 0)
            throw new AiBudgetException("ai.budget.invalid_request");
        var limits = options.Value;
        if (limits.MonthlyRequestLimit is <= 0 or > 1_000_000 || limits.RunRequestLimit is <= 0 or > 1_000_000
            || limits.MonthlyTokenLimit is <= 0 or > AiExecutionBudget.MaximumTokens || limits.RunTokenLimit is <= 0 or > AiExecutionBudget.MaximumTokens)
            throw new AiBudgetException("ai.budget.invalid_policy");
    }

    private string Scope() => tenant.Id is { } id ? id.ToString("N") : tenant.IsHost ? "host"
        : throw new AiBudgetException("ai.budget.scope_required");

    private async Task LockAsync(string scope, CancellationToken token) =>
        await commands.ExecuteAsync(database.Value.Provider == DatabaseProvider.MySql ? AiOperationBudgetSql.EnsureMySql : AiOperationBudgetSql.EnsureSqlServer,
            AiSqlParameters.Create(("ScopeKey", scope), ("ScopeId", ids.NewId())), token).ConfigureAwait(false);

    private Task<AiOperationBudgetRecord?> FindAsync(Guid id, string scope, CancellationToken token) =>
        queries.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(AiOperationBudgetSql.Find,
            AiSqlParameters.Create(("OperationId", id), ("ScopeKey", scope)), token);
}
