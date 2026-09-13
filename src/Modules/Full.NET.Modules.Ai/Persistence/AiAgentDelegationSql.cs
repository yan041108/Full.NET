using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>Agent 委托 SQL；授予方创建，受任方按范围代行审批。</summary>
internal static class AiAgentDelegationSql
{
    private const string Columns = """
        delegation.Id,
                  delegation.ScopeKey,
                  delegation.TenantId,
                  delegation.GrantorUserId,
                  delegation.GranteeUserId,
                  delegation.ToolName,
                  delegation.PermissionCode,
                  delegation.ExpiresAtUtc,
                  delegation.RevokedAtUtc,
                  delegation.Version,
                  delegation.CreatedAtUtc,
                  delegation.UpdatedAtUtc
        """;

    public static readonly SqlStatement Insert = new(
        "ai.insert_agent_delegation",
        """
        INSERT INTO fn_ai_agent_delegation
            (Id, ScopeKey, TenantId, GrantorUserId, GranteeUserId, ToolName, PermissionCode,
             ExpiresAtUtc, Version, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@Id, @ScopeKey, @TenantId, @GrantorUserId, @GranteeUserId, @ToolName, @PermissionCode,
             @ExpiresAtUtc, 1, @Now, @Now)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindOwnedById = new(
        "ai.find_owned_agent_delegation",
        $"""
        SELECT {Columns}
        FROM fn_ai_agent_delegation AS delegation
        WHERE delegation.Id = @Id
          AND delegation.ScopeKey = @ScopeKey
          AND delegation.GrantorUserId = @GrantorUserId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListOwned = new(
        "ai.list_owned_agent_delegations",
        $"""
        SELECT {Columns}
        FROM fn_ai_agent_delegation AS delegation
        WHERE delegation.ScopeKey = @ScopeKey
          AND delegation.GrantorUserId = @GrantorUserId
        ORDER BY delegation.CreatedAtUtc DESC, delegation.Id DESC
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement RevokeOwned = new(
        "ai.revoke_owned_agent_delegation",
        """
        UPDATE fn_ai_agent_delegation
        SET RevokedAtUtc = @Now,
            Version = Version + 1,
            UpdatedAtUtc = @Now
        WHERE Id = @Id
          AND ScopeKey = @ScopeKey
          AND GrantorUserId = @GrantorUserId
          AND RevokedAtUtc IS NULL
          AND Version = @ExpectedVersion
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountActive = new(
        "ai.count_active_agent_delegation",
        """
        SELECT COUNT(1)
        FROM fn_ai_agent_delegation AS delegation
        WHERE delegation.ScopeKey = @ScopeKey
          AND delegation.GrantorUserId = @GrantorUserId
          AND delegation.GranteeUserId = @GranteeUserId
          AND delegation.RevokedAtUtc IS NULL
          AND delegation.ExpiresAtUtc > @Now
          AND (delegation.ToolName IS NULL OR delegation.ToolName = @ToolName)
          AND (delegation.PermissionCode IS NULL OR delegation.PermissionCode = @PermissionCode)
        """,
        SqlDataScope.Global);
}
