using Full.NET.Abstractions.Results;
using Full.NET.Modules.DataApproval.Contracts;

namespace Full.NET.Modules.SerialNumbers.Features.WorkflowCommands;

/// <summary>将DataApproval的受信场景命令路由到SerialNumbers自己的事务与持久凭据。</summary>
internal sealed class SerialRuleScenarioCommandHandler(SerialRuleApprovedCommandExecutor commands) : IDataApprovalScenarioCommandHandler
{
    public bool Supports(string scenarioKey) => scenarioKey is DataApprovalScenarioKeys.SerialRuleHostUpdate or DataApprovalScenarioKeys.SerialRuleHostDisable;
    public Task<Result<DataApprovalCommandEffect>> ApplyAsync(DataApprovalScenarioCommand command, CancellationToken token) => commands.ApplyAsync(command, token);
    public Task<Result<DataApprovalCommandEffect>> CompensateAsync(DataApprovalScenarioCommand command, CancellationToken token) => commands.CompensateAsync(command, token);
}
