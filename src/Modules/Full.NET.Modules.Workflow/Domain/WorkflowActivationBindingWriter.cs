using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Ids;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>调用方持有根锁并与步骤共用事务；仅刚刚创建的真实步骤能够替换有效绑定，历史事实保持不变。</summary>
internal static class WorkflowActivationBindingWriter
{
    public static async Task BindAsync(ICommandExecutor command,Guid instanceId,string scope,Guid epochId,Guid stepId,
        DateTimeOffset now,CancellationToken token,WorkflowActivationTrace? trace=null,IIdGenerator? ids=null)
    {
        if (instanceId==Guid.Empty || epochId==Guid.Empty || stepId==Guid.Empty || string.IsNullOrWhiteSpace(scope))
            throw new InvalidOperationException("workflow.activation_binding.invalid");
        var parameters=WorkflowSqlParameters.Create(("InstanceId",instanceId),("TenantScopeKey",scope),
            ("ExecutionEpochId",epochId),("StepId",stepId),("Now",now));
        await command.ExecuteAsync(WorkflowActivationBindingSql.DeleteForStep,parameters,token).ConfigureAwait(false);
        if (await command.ExecuteAsync(WorkflowActivationBindingSql.BindStep,parameters,token).ConfigureAwait(false)!=1)
            throw new InvalidOperationException("workflow.activation_binding.step_missing");
        if (trace is not null)
            await trace.RecordAsync(command,ids ?? throw new InvalidOperationException("workflow.causality.generator_missing"),
                instanceId,scope,stepId,now,token).ConfigureAwait(false);
    }
}
