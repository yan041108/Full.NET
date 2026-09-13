using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>Agent 审批 SQL；OperationId 唯一，消费使用版本条件更新。</summary>
internal static class AiAgentApprovalSql
{
    private const string Columns = """
        approval.Id,
                  approval.ScopeKey,
                  approval.TenantId,
                  approval.RunId,
                  approval.OperationId,
                  approval.SessionId,
                  approval.ToolName,
                  approval.ToolVersion,
                  approval.ArgumentsHash,
                  approval.ArgumentsProtected,
                  approval.PolicyVersion,
                  approval.PresentationJson,
                  approval.RequestedBy,
                  approval.ApproverId,
                  approval.DecisionKey,
                  approval.ExpiresAtUtc,
                  approval.ConsumedAtUtc,
                  approval.Version,
                  approval.CreatedAtUtc,
                  approval.UpdatedAtUtc
        """;

    public static readonly SqlStatement Insert = new(
        "ai.insert_agent_approval",
        """
        INSERT INTO fn_ai_agent_approval
            (Id, ScopeKey, TenantId, RunId, OperationId, SessionId, ToolName, ToolVersion,
             ArgumentsHash, ArgumentsProtected, PolicyVersion, PresentationJson, RequestedBy,
             DecisionKey, ExpiresAtUtc, Version, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@Id, @ScopeKey, @TenantId, @RunId, @OperationId, @SessionId, @ToolName, @ToolVersion,
             @ArgumentsHash, @ArgumentsProtected, @PolicyVersion, @PresentationJson, @RequestedBy,
             @DecisionKey, @ExpiresAtUtc, 1, @Now, @Now)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindByOperation = new(
        "ai.find_agent_approval_by_operation",
        $"""
        SELECT {Columns}
        FROM fn_ai_agent_approval AS approval
        WHERE approval.OperationId = @OperationId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindById = new(
        "ai.find_agent_approval",
        $"""
        SELECT {Columns}
        FROM fn_ai_agent_approval AS approval
        WHERE approval.Id = @Id
          AND approval.ScopeKey = @ScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindOwnedById = new(
        "ai.find_owned_agent_approval",
        $"""
        SELECT {Columns}
        FROM fn_ai_agent_approval AS approval
        WHERE approval.Id = @Id
          AND approval.ScopeKey = @ScopeKey
          AND approval.RequestedBy = @ActorUserId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement Decide = new(
        "ai.decide_agent_approval",
        """
        UPDATE fn_ai_agent_approval
        SET DecisionKey = @DecisionKey,
            ApproverId = @ApproverId,
            Version = Version + 1,
            UpdatedAtUtc = @Now
        WHERE Id = @Id
          AND ScopeKey = @ScopeKey
          AND DecisionKey = 'pending'
          AND ConsumedAtUtc IS NULL
          AND ExpiresAtUtc > @Now
          AND Version = @ExpectedVersion
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountApprovedUnconsumedForRun = new(
        "ai.count_approved_unconsumed_for_run",
        """
        SELECT COUNT(1)
        FROM fn_ai_agent_approval AS approval
        WHERE approval.RunId = @RunId
          AND approval.ScopeKey = @ScopeKey
          AND approval.RequestedBy = @ActorUserId
          AND approval.DecisionKey = 'approved'
          AND approval.ConsumedAtUtc IS NULL
          AND approval.ExpiresAtUtc > @Now
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement Consume = new(
        "ai.consume_agent_approval",
        """
        UPDATE fn_ai_agent_approval
        SET ConsumedAtUtc = @Now,
            Version = Version + 1,
            UpdatedAtUtc = @Now
        WHERE Id = @Id
          AND DecisionKey = 'approved'
          AND ConsumedAtUtc IS NULL
          AND ExpiresAtUtc > @Now
          AND Version = @ExpectedVersion
        """,
        SqlDataScope.Global);
}
