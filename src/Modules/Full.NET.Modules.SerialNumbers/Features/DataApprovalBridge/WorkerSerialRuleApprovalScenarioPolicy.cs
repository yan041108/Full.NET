using Full.NET.Modules.DataApproval.Contracts;

namespace Full.NET.Modules.SerialNumbers.Features.DataApprovalBridge;

/// <summary>
/// Worker 不执行 HTTP 直接写入；DataApproval 已批准的应用路径走 ApplyApproved*，策略门禁在 API 侧生效。
/// </summary>
internal sealed class WorkerSerialRuleApprovalScenarioPolicy : IDataApprovalScenarioPolicyPort
{
    public Task<bool> BlocksDirectWriteAsync(string scenarioKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}