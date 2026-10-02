namespace Full.NET.AI.Abstractions.Budgets;

/// <summary>模型派发前原子预留，派发后独立结算；调用方负责模型/会话授权与恢复时重验。</summary>
public interface IAiOperationBudgetStore
{
    /// <summary>为一次 AI 操作原子预留预算配额。</summary>
    /// <param name="request">包含操作类型、预估消耗与租户标识的预留请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>预留凭证；配额不足时通过异常拒绝，不返回部分预留。</returns>
    /// <remarks>预留必须在派发前完成；预留成功后即使派发失败也需调用 <see cref="SettleAsync"/> 释放或结算。</remarks>
    Task<AiOperationReservation> ReserveAsync(AiOperationRequest request, CancellationToken cancellationToken = default);
    /// <summary>未知计量保留预留，允许稍后完整计量补录；不同的重复结算拒绝，不覆盖原事实。</summary>
    /// <param name="operationId">预留时返回的操作标识。</param>
    /// <param name="usage">实际计量消耗。</param>
    /// <param name="outcome">操作结果描述。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示结算完成的 Task；对同一 operationId 的不同结算会被拒绝并抛出异常。</returns>
    Task SettleAsync(Guid operationId, AiOperationUsage usage, string outcome, CancellationToken cancellationToken = default);
}
