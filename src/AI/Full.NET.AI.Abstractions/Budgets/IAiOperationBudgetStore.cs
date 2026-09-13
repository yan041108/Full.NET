namespace Full.NET.AI.Abstractions.Budgets;

/// <summary>模型派发前原子预留，派发后独立结算；调用方负责模型/会话授权与恢复时重验。</summary>
public interface IAiOperationBudgetStore
{
    Task<AiOperationReservation> ReserveAsync(AiOperationRequest request, CancellationToken cancellationToken = default);
    /// <summary>未知计量保留预留，允许稍后完整计量补录；不同的重复结算拒绝，不覆盖原事实。</summary>
    Task SettleAsync(Guid operationId, AiOperationUsage usage, string outcome, CancellationToken cancellationToken = default);
}
