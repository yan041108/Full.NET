using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>后台恢复使用可验证的系统来源，不把业务发起人伪装为实际操作者。</summary>
internal static class WorkflowRecoveryAuditDetail
{
    internal static string Create(WorkflowRecoveryTaskRecord task, int attempt, string? error)
    {
        if (task.Id == Guid.Empty || task.InstanceId == Guid.Empty || task.LeaseGeneration < 1 || attempt < 1 ||
            task.KindKey is not (WorkflowRecoveryKinds.ExpiredLease or WorkflowRecoveryKinds.StuckInstance or WorkflowRecoveryKinds.IncompleteStep))
            throw new InvalidOperationException("A recovery audit requires a real leased task and a closed recovery kind.");
        return WorkflowJsonCanonicalizer.Compile(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("sourceKey", "recovery_worker"); writer.WriteString("taskId", task.Id);
            writer.WriteString("instanceId", task.InstanceId); writer.WriteString("kindKey", task.KindKey);
            writer.WriteNumber("leaseGeneration", task.LeaseGeneration); writer.WriteNumber("attempt", attempt);
            if (error is not null) writer.WriteString("error", error);
            writer.WriteEndObject();
        }).CanonicalJson;
    }
}
