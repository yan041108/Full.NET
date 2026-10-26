using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.DataApproval.Contracts;

/// <summary>实际审批请求固定的业务动作输入；仅经受信端口调用，节点不能自行指定规则或快照。</summary>
public sealed record DataApprovalScenarioCommand(Guid CommandId, Guid ApprovalRequestId, string ScenarioKey,
    Guid TargetEntityId, string SnapshotJson, Guid RequestedByUserId);

/// <summary>所有者持久凭据的最小结果；补偿修订只有在确认恢复成功后才出现，不回传业务快照。</summary>
public sealed record DataApprovalCommandEffect(Guid CommandId, string InputHash, string OutcomeKey,
    long? AppliedVersion, long? CompensatedVersion);

/// <summary>审批模块拥有的实际节点命令端口，业务所有者在自己的事务内提交动作及稳定凭据。</summary>
public interface IDataApprovalScenarioCommandHandler
{
    /// <summary>声明实际拥有的场景，未知场景不能退化到默认业务动作。</summary>
    bool Supports(string scenarioKey);
    /// <summary>在所有者事务中应用固定输入及持久凭据；调用方不得持有本地事务。</summary>
    Task<Result<DataApprovalCommandEffect>> ApplyAsync(DataApprovalScenarioCommand command, CancellationToken cancellationToken);
    /// <summary>仅凭同一命令的原快照及版本补偿，取消先到则持久保存无效果墓碑。</summary>
    Task<Result<DataApprovalCommandEffect>> CompensateAsync(DataApprovalScenarioCommand command, CancellationToken cancellationToken);
}
