using System.Globalization;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Streaming;

/// <summary>内部预留凭据，不能从客户端输入构造；数据库结算仍核对月份与额度。</summary>
/// <param name="Id">请求预留标识。</param>
/// <param name="MonthKey">预留所属 UTC 月份。</param>
/// <param name="ReservedTokens">预留 Token 数。</param>
/// <param name="IsTracked">当前租户是否启用了配额。</param>
internal sealed record AiQuotaReservation(Guid Id, string MonthKey, long ReservedTokens, bool IsTracked);

/// <summary>通过短事务预留租户配额，并对明确用量作一次性结算。</summary>
/// <param name="queryExecutor">当前租户只读执行器。</param>
/// <param name="commandExecutor">当前租户命令执行器。</param>
/// <param name="transaction">预留与记账使用的本地短事务。</param>
/// <param name="clock">UTC 时钟。</param>
internal sealed class AiChatQuotaGuard(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock)
{
    /// <summary>调用外部模型前同时占用一次请求与 Token；进程中断后预留保持有效。</summary>
    /// <param name="reservationId">当前生成唯一标识。</param>
    /// <param name="reservedTokens">根据完整提示和最大输出估计的保守预算。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<Result<AiQuotaReservation>> ReserveAsync(Guid reservationId, long reservedTokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reservedTokens);
        return transaction.ExecuteAsync(async token =>
        {
            var quota = await queryExecutor.QuerySingleOrDefaultAsync<AiTenantQuotaRecord>(
                AiTenantQuotaSql.FindCurrentTenantQuota, null, token).ConfigureAwait(false);
            var now = clock.UtcNow;
            var month = now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            // 保留原有配置语义：未配置或关闭配额表示不限额，不把关闭开关解释为禁用 AI。
            if (quota is null || !quota.IsEnabled)
                return Result<AiQuotaReservation>.Success(new(reservationId, month, reservedTokens, false));
            var parameters = AiSqlParameters.Create(("Id", reservationId), ("QuotaMonthKey", month),
                ("ReservedTokens", reservedTokens), ("UpdatedAtUtc", now));
            var affected = await commandExecutor.ExecuteAsync(AiQuotaReservationSql.Reserve, parameters, token).ConfigureAwait(false);
            if (affected != 1)
                return Result<AiQuotaReservation>.Failure(new Error(AiErrorCodes.TenantQuotaExceeded,
                    "The monthly AI request or token quota cannot cover this request.", ErrorType.Forbidden));
            await commandExecutor.ExecuteAsync(AiQuotaReservationSql.Insert, parameters, token).ConfigureAwait(false);
            return Result<AiQuotaReservation>.Success(new(reservationId, month, reservedTokens, true));
        }, cancellationToken);
    }

    /// <summary>成功且提供程序返回完整用量时调整预留；失败、取消或缺失计量时保守保留预算。</summary>
    /// <param name="reservation">调用前持久化的内部凭据。</param>
    /// <param name="promptTokens">已确认提示 Token 数。</param>
    /// <param name="completionTokens">已确认补全 Token 数。</param>
    /// <param name="cancellationToken">结算取消令牌，与断开的 HTTP 请求分离。</param>
    public async Task SettleAsync(AiQuotaReservation reservation, int? promptTokens, int? completionTokens,
        CancellationToken cancellationToken = default)
    {
        if (!reservation.IsTracked) return;
        long? actual = promptTokens is >= 0 && completionTokens is >= 0
            ? (long)promptTokens.Value + completionTokens.Value : null;
        var parameters = AiSqlParameters.Create(("Id", reservation.Id), ("QuotaMonthKey", reservation.MonthKey),
            ("ReservedTokens", reservation.ReservedTokens), ("ActualTokens", actual),
            ("TokenDelta", (actual ?? reservation.ReservedTokens) - reservation.ReservedTokens), ("UpdatedAtUtc", clock.UtcNow));
        await transaction.ExecuteAsync(async token =>
        {
            var affected = await commandExecutor.ExecuteAsync(AiQuotaReservationSql.Settle, parameters, token).ConfigureAwait(false);
            if (affected == 1)
                await commandExecutor.ExecuteAsync(AiQuotaReservationSql.Adjust, parameters, token).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }
}
