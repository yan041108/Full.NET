using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>AI自有不可变发送意图与回执投影；不读写Calendar表。</summary>
internal static class AiScheduleCompletionSql
{
    private const string Scope = "((@ScopeTenantId IS NULL AND TenantId IS NULL) OR TenantId=@ScopeTenantId)";
    private const string Columns = "Id,TenantId,OwnerUserId,RunId,OperationId,RequestJson,RequestDigest,Outcome,ReceiptJson,Version";
    internal static readonly SqlStatement Find = new("ai.find_schedule_completion_intent", $"SELECT {Columns} FROM fn_ai_schedule_completion_intent WHERE OperationId=@OperationId AND {Scope}", SqlDataScope.Global);
    internal static readonly SqlStatement Owned = new("ai.find_owned_schedule_completion_intent", $"SELECT {Columns} FROM fn_ai_schedule_completion_intent WHERE RunId=@RunId AND OwnerUserId=@OwnerUserId AND {Scope}", SqlDataScope.Global);
    internal static readonly SqlStatement FindBoundTarget = new("ai.find_bound_schedule_completion_target",
        "SELECT RunId FROM fn_ai_schedule_completion_intent WHERE ScopeKey=@ScopeKey AND OwnerUserId=@OwnerUserId AND ScheduleId=@ScheduleId AND ExpectedVersion=@ExpectedVersion", SqlDataScope.Global);
    internal static readonly SqlStatement Insert = new("ai.insert_schedule_completion_intent",
        "INSERT INTO fn_ai_schedule_completion_intent (Id,TenantId,ScopeKey,OwnerUserId,RunId,OperationId,ScheduleId,ExpectedVersion,RequestJson,RequestDigest,Outcome,ReceiptJson,Version,CreatedAtUtc,UpdatedAtUtc) VALUES (@Id,@ScopeTenantId,@ScopeKey,@OwnerUserId,@RunId,@OperationId,@ScheduleId,@ExpectedVersion,@RequestJson,@RequestDigest,'accepted',NULL,1,@Now,@Now)", SqlDataScope.Global);
    internal static readonly SqlStatement RecordReceipt = new("ai.record_schedule_completion_receipt",
        $"UPDATE fn_ai_schedule_completion_intent SET Outcome=@Outcome,ReceiptJson=@ReceiptJson,Version=Version+1,UpdatedAtUtc=@Now WHERE OperationId=@OperationId AND RequestDigest=@RequestDigest AND Version=@Version AND Outcome IN ('accepted','unknown') AND {Scope}", SqlDataScope.Global);
    internal static readonly SqlStatement ClaimDispatch = new("ai.claim_schedule_completion_dispatch",
        $"UPDATE fn_ai_schedule_completion_intent SET Outcome='unknown',Version=Version+1,UpdatedAtUtc=@Now WHERE OperationId=@OperationId AND RequestDigest=@RequestDigest AND Version=@Version AND Outcome='accepted' AND {Scope}", SqlDataScope.Global);
    internal static readonly SqlStatement LockRunSqlServer = new("ai.lock_schedule_completion_run.sql_server",
        $"SELECT Id FROM fn_ai_agent_run WITH(UPDLOCK,HOLDLOCK) WHERE Id=@RunId AND ActorUserId=@OwnerUserId AND StatusKey='running' AND DeadlineAtUtc>@Now AND {Scope}", SqlDataScope.Global);
    internal static readonly SqlStatement LockRunMySql = new("ai.lock_schedule_completion_run.mysql",
        $"SELECT Id FROM fn_ai_agent_run WHERE Id=@RunId AND ActorUserId=@OwnerUserId AND StatusKey='running' AND DeadlineAtUtc>@Now AND {Scope} FOR UPDATE", SqlDataScope.Global);
}

/// <summary>内部行包含原会话，禁止直接映射为公开响应。</summary>
internal sealed record AiScheduleCompletionIntentRecord(Guid Id, Guid? TenantId, Guid OwnerUserId, Guid RunId, Guid OperationId,
    string RequestJson, string RequestDigest, string Outcome, string? ReceiptJson, int Version);
