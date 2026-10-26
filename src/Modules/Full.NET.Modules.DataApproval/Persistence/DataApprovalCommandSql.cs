using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.DataApproval.Persistence;

/// <summary>回执与结果Outbox同属审批模块；目标业务效果由所有者端口另行提交。</summary>
internal static class DataApprovalCommandSql
{
    public static readonly SqlStatement Find = new("data_approval.command.find", """
        SELECT Id,RequestId,InstanceId,TenantScopeKey,CallId,PhaseKey,Generation,InputHash,OutcomeKey,ResultJson,CreatedAtUtc
        FROM fn_dataapproval_cmd_receipt WHERE CallId=@CallId AND PhaseKey=@PhaseKey AND Generation=@Generation AND TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement Insert = new("data_approval.command.insert", """
        INSERT INTO fn_dataapproval_cmd_receipt (Id,RequestId,InstanceId,TenantScopeKey,CallId,PhaseKey,Generation,InputHash,OutcomeKey,ResultJson,CreatedAtUtc)
        VALUES (@Id,@RequestId,@InstanceId,@TenantScopeKey,@CallId,@PhaseKey,@Generation,@InputHash,@OutcomeKey,@ResultJson,@Now)
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountApplied = new("data_approval.command.count_applied", """
        SELECT COUNT(*) FROM fn_dataapproval_cmd_receipt WHERE RequestId=@RequestId AND InstanceId=@InstanceId
        AND TenantScopeKey=@TenantScopeKey AND PhaseKey='execute' AND OutcomeKey='applied'
        """, SqlDataScope.Global);
}

/// <summary>消息ID不参与业务去重；节点调用、阶段和代次决定唯一回执。</summary>
internal sealed record DataApprovalCommandReceiptRecord(Guid Id, Guid RequestId, Guid InstanceId, string TenantScopeKey,
    Guid CallId, string PhaseKey, int Generation, string InputHash, string OutcomeKey, string ResultJson, DateTimeOffset CreatedAtUtc);
