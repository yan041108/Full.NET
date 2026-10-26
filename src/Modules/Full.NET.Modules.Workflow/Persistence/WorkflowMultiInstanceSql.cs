using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>集合与选择关系均由父根锁保护；项身份、作用域和修订必须同时匹配。</summary>
internal static class WorkflowMultiInstanceSql
{
    public static readonly SqlStatement Find = new("workflow.subprocess_set.find", """
        SELECT CallId,InstanceId,TenantScopeKey,ModeKey,SourceKey,MaximumCount,RequiredPercentage,ItemCount,RequiredSuccesses,
            SourceJson,SourceHash,SelectionHash,OutputJson,OutputHash,StatusKey,Revision,CreatedAtUtc,UpdatedAtUtc
        FROM fn_workflow_subprocess_set WHERE CallId=@CallId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement Insert = new("workflow.subprocess_set.insert", """
        INSERT INTO fn_workflow_subprocess_set (CallId,InstanceId,TenantScopeKey,ModeKey,SourceKey,MaximumCount,RequiredPercentage,
            ItemCount,RequiredSuccesses,SourceJson,SourceHash,SelectionHash,OutputJson,OutputHash,StatusKey,Revision,CreatedAtUtc,UpdatedAtUtc)
        SELECT Id,InstanceId,TenantScopeKey,@ModeKey,@SourceKey,@MaximumCount,@RequiredPercentage,
            NULL,NULL,NULL,NULL,NULL,NULL,NULL,'preparing',1,@Now,@Now FROM fn_workflow_subprocess_call
        WHERE Id=@CallId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement Update = new("workflow.subprocess_set.update", """
        UPDATE fn_workflow_subprocess_set SET ItemCount=@ItemCount,RequiredSuccesses=@RequiredSuccesses,
            SourceJson=@SourceJson,SourceHash=@SourceHash,SelectionHash=@SelectionHash,OutputJson=@OutputJson,OutputHash=@OutputHash,
            StatusKey=@StatusKey,Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE CallId=@CallId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision
        """, SqlDataScope.Global);
    public static readonly SqlStatement Choices = new("workflow.subprocess_choice.list", """
        SELECT choice.ItemId FROM fn_workflow_subprocess_choice AS choice
        INNER JOIN fn_workflow_subprocess_item AS item ON item.Id=choice.ItemId AND item.CallId=choice.CallId
            AND item.InstanceId=choice.InstanceId AND item.TenantScopeKey=choice.TenantScopeKey
        WHERE choice.CallId=@CallId AND choice.InstanceId=@InstanceId AND choice.TenantScopeKey=@TenantScopeKey ORDER BY item.ItemKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertChoice = new("workflow.subprocess_choice.insert", """
        INSERT INTO fn_workflow_subprocess_choice (CallId,ItemId,InstanceId,TenantScopeKey,CreatedAtUtc)
        SELECT item.CallId,item.Id,item.InstanceId,item.TenantScopeKey,@Now FROM fn_workflow_subprocess_item AS item
        INNER JOIN fn_workflow_subprocess_set AS source ON source.CallId=item.CallId AND source.InstanceId=item.InstanceId
            AND source.TenantScopeKey=item.TenantScopeKey
        WHERE item.Id=@ItemId AND item.CallId=@CallId AND item.InstanceId=@InstanceId AND item.TenantScopeKey=@TenantScopeKey
            AND item.StatusKey='succeeded' AND source.StatusKey='running' AND source.Revision=@Revision
        """, SqlDataScope.Global);
}

/// <summary>来源分母和选择摘要只在父锁下固化一次，后续结果不能改变原选择集合。</summary>
internal sealed record WorkflowMultiInstanceSetRecord(Guid CallId, Guid InstanceId, string TenantScopeKey, string ModeKey, string SourceKey,
    int MaximumCount, int RequiredPercentage, int? ItemCount, int? RequiredSuccesses, string? SourceJson, string? SourceHash,
    string? SelectionHash, string? OutputJson, string? OutputHash, string StatusKey, long Revision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
