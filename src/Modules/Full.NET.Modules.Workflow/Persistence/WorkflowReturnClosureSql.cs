using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>只关闭有界实际身份集合；不以序号、同名节点或整个实例推断退回影响范围。</summary>
internal static class WorkflowReturnClosureSql
{
    public static readonly SqlStatement CancelTodos = new("workflow.return_closure.todos", """
        UPDATE fn_workflow_todo SET StatusKey='cancelled',ResultActionKey='cancelled',CompletedAtUtc=@Now,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND StepId IN @StepIds AND StatusKey IN ('active','before_countersign','suspended')
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelSlots = new("workflow.return_closure.slots", """
        UPDATE fn_workflow_approval_slot SET DecisionKey='cancelled',DecidedAtUtc=@Now,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND StepId IN @StepIds AND DecisionKey IS NULL
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelGroups = new("workflow.return_closure.groups", """
        UPDATE fn_workflow_approval_group SET StatusKey='cancelled',CompletedAtUtc=@Now,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND StepId IN @StepIds AND StatusKey='active'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelTaskMembers = new("workflow.return_closure.task_members", """
        UPDATE fn_workflow_task_member SET StatusKey='cancelled',CompletedAtUtc=@Now,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND TaskId IN @StepIds AND StatusKey='pending'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelTasks = new("workflow.return_closure.tasks", """
        UPDATE fn_workflow_task SET StatusKey='cancelled',CompletedAtUtc=@Now,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND Id IN @StepIds AND StatusKey='active'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelClaims = new("workflow.return_closure.claims", """
        UPDATE fn_workflow_claim SET StatusKey='cancelled',CompletedAtUtc=@Now,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND Id IN @StepIds AND StatusKey IN ('available','claimed')
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelClaimAttempts = new("workflow.return_closure.claim_attempts", """
        UPDATE fn_workflow_claim_attempt SET StatusKey='cancelled',CompletedAtUtc=@Now,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND PoolId IN @StepIds AND StatusKey='active'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelVotes = new("workflow.return_closure.votes", """
        UPDATE fn_workflow_vote SET StatusKey='cancelled',CompletedAtUtc=@Now,Revision=Revision+1,FinalYesCount=(SELECT COUNT(*) FROM fn_workflow_vote_ballot AS ballot WHERE ballot.VoteId=fn_workflow_vote.Id AND ballot.InstanceId=@InstanceId AND ballot.ChoiceKey='yes'),FinalNoCount=(SELECT COUNT(*) FROM fn_workflow_vote_ballot AS ballot WHERE ballot.VoteId=fn_workflow_vote.Id AND ballot.InstanceId=@InstanceId AND ballot.ChoiceKey='no'),FinalAbstainCount=(SELECT COUNT(*) FROM fn_workflow_vote_ballot AS ballot WHERE ballot.VoteId=fn_workflow_vote.Id AND ballot.InstanceId=@InstanceId AND ballot.ChoiceKey='abstain'),FinalUnsubmittedCount=(SELECT COUNT(*) FROM fn_workflow_vote_ballot AS ballot WHERE ballot.VoteId=fn_workflow_vote.Id AND ballot.InstanceId=@InstanceId AND ballot.ChoiceKey IS NULL)
        WHERE InstanceId=@InstanceId AND Id IN @StepIds AND StatusKey='active'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelBallots = new("workflow.return_closure.ballots", """
        UPDATE fn_workflow_vote_ballot SET StatusKey='cancelled',CancelledAtUtc=@Now,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND VoteId IN @StepIds AND StatusKey='pending'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelDelays = new("workflow.return_closure.delays", """
        UPDATE fn_workflow_delay SET StatusKey='cancelled',CompletedAtUtc=@Now,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Id IN @StepIds AND StatusKey='waiting'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelSlas = new("workflow.return_closure.slas", """
        UPDATE fn_workflow_sla SET StatusKey='cancelled',CompletedAtUtc=@Now,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Id IN @StepIds AND StatusKey='waiting'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelCountersignItems = new("workflow.return_closure.countersign_items", """
        UPDATE fn_workflow_countersign_item SET StatusKey='cancelled'
        WHERE StatusKey IN ('pending','active') AND ChainId IN (SELECT chain.Id FROM fn_workflow_countersign_chain AS chain WHERE chain.InstanceId=@InstanceId AND chain.StepId IN @StepIds)
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelCountersignChains = new("workflow.return_closure.countersign_chains", """
        UPDATE fn_workflow_countersign_chain SET StatusKey='cancelled'
        WHERE InstanceId=@InstanceId AND StepId IN @StepIds AND StatusKey='active'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelSteps = new("workflow.return_closure.steps", """
        UPDATE fn_workflow_step SET StatusKey='cancelled',CompletedAtUtc=@Now,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND Id IN @StepIds AND StatusKey='active'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement InvalidateSteps = new("workflow.return_closure.invalidate_steps", """
        UPDATE fn_workflow_step SET StatusKey='rolled_back',Revision=Revision+1
        WHERE InstanceId=@InstanceId AND Id IN @InvalidatedStepIds AND StatusKey='completed'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelForks = new("workflow.return_closure.forks", """
        UPDATE fn_workflow_parallel_join SET StatusKey='cancelled',CompletedAtUtc=@Now,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND Id IN @ForkIds AND StatusKey='waiting'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelWork = new("workflow.return_closure.work", """
        UPDATE fn_workflow_activity_work SET StatusKey='cancelled',CompletedAtUtc=@Now,LeaseGeneration=LeaseGeneration+1,LeaseOwnerKey=NULL,LeaseExpiresAtUtc=NULL
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Id IN @WorkIds AND StatusKey IN ('pending','leased','faulted')
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CloseEpochs = new("workflow.return_closure.epochs", """
        UPDATE fn_workflow_execution_epoch SET StatusKey='closed',ClosedAtUtc=@Now,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Id<>InstanceId AND StatusKey='active' AND Id IN (SELECT frame.ExecutionEpochId FROM fn_workflow_region_frame AS frame WHERE frame.InstanceId=@InstanceId AND frame.TenantScopeKey=@TenantScopeKey AND frame.ParentForkActivationId IN @ForkIds)
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement DeleteBindings = new("workflow.return_closure.bindings", """
        DELETE FROM fn_workflow_activation_binding WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StepId IN @InvalidatedStepIds
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
}
