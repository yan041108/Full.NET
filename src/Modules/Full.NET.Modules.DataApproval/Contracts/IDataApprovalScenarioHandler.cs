using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.DataApproval.Contracts;

/// <summary>审批编排拥有的最小场景端口；业务所有者提供快照和幂等应用，不暴露业务 DTO。</summary>
public interface IDataApprovalScenarioHandler
{
    /// <summary>判断本适配器拥有的稳定场景键。</summary>
    bool Supports(string scenarioKey);

    /// <summary>读取业务所有者的审批前快照；调用方不得持有本地写事务。</summary>
    Task<Result<string>> GetSnapshotAsync(string scenarioKey, Guid targetEntityId, CancellationToken cancellationToken);

    /// <summary>在业务所有者的事务中幂等应用已批准变更。</summary>
    Task<Result<bool>> ApplyAsync(string scenarioKey, Guid targetEntityId, string snapshotJson,
        Guid actorUserId, string idempotencyKey, CancellationToken cancellationToken);
}
