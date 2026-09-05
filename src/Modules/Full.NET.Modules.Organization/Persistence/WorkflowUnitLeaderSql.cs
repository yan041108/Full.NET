using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Organization.Persistence;

/// <summary>Workflow 机构负责人解析使用的只读 SQL 集合。</summary>
internal static class WorkflowUnitLeaderSql
{
    public static readonly SqlStatement CountActiveUnits = new(
        "organization.workflow.count_active_units",
        """
        SELECT COUNT(1)
        FROM fn_organization_unit
        WHERE TenantId = @TenantId
          AND IsActive = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListActiveUnitsSqlServer = new(
        "organization.workflow.list_active_units.sql_server",
        """
        SELECT Id, Code, Name
        FROM fn_organization_unit
        WHERE TenantId = @TenantId
          AND IsActive = 1
        ORDER BY DisplayOrder, Code
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListActiveUnitsMySql = new(
        "organization.workflow.list_active_units.mysql",
        """
        SELECT Id, Code, Name
        FROM fn_organization_unit
        WHERE TenantId = @TenantId
          AND IsActive = 1
        ORDER BY DisplayOrder, Code
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindActiveUnitsByIds = new(
        "organization.workflow.find_active_units_by_ids",
        """
        SELECT Id, Code, Name
        FROM fn_organization_unit
        WHERE TenantId = @TenantId
          AND Id IN @UnitIds
          AND IsActive = 1
        ORDER BY DisplayOrder, Code
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListUnitLeaderCandidatesSqlServer = new(
        "organization.workflow.list_unit_leader_candidates.sql_server",
        """
        SELECT ranked.UnitId, ranked.UserId
        FROM
        (
            SELECT userUnit.UnitId,
                   userUnit.UserId,
                   ROW_NUMBER() OVER (
                       PARTITION BY userUnit.UnitId
                       ORDER BY COALESCE(positionLevel.DisplayOrder, -1) DESC,
                                userUnit.UserId) AS RowNumber
            FROM fn_organization_user_unit AS userUnit
            LEFT JOIN fn_organization_user_position AS userPosition
                ON userPosition.TenantId = userUnit.TenantId
               AND userPosition.UserId = userUnit.UserId
               AND userPosition.IsActive = 1
            LEFT JOIN fn_organization_position AS positionObject
                ON positionObject.Id = userPosition.PositionId
               AND positionObject.TenantId = userUnit.TenantId
               AND positionObject.UnitId = userUnit.UnitId
               AND positionObject.IsActive = 1
            LEFT JOIN fn_organization_position_level AS positionLevel
                ON positionLevel.Id = positionObject.PositionLevelId
               AND positionLevel.TenantId = userUnit.TenantId
               AND positionLevel.IsActive = 1
            WHERE userUnit.TenantId = @TenantId
              AND userUnit.UnitId IN @UnitIds
              AND userUnit.IsActive = 1
        ) AS ranked
        WHERE ranked.RowNumber = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListUnitLeaderCandidatesMySql = new(
        "organization.workflow.list_unit_leader_candidates.mysql",
        """
        SELECT ranked.UnitId, ranked.UserId
        FROM
        (
            SELECT userUnit.UnitId,
                   userUnit.UserId,
                   ROW_NUMBER() OVER (
                       PARTITION BY userUnit.UnitId
                       ORDER BY COALESCE(positionLevel.DisplayOrder, -1) DESC,
                                userUnit.UserId) AS RowNumber
            FROM fn_organization_user_unit AS userUnit
            LEFT JOIN fn_organization_user_position AS userPosition
                ON userPosition.TenantId = userUnit.TenantId
               AND userPosition.UserId = userUnit.UserId
               AND userPosition.IsActive = 1
            LEFT JOIN fn_organization_position AS positionObject
                ON positionObject.Id = userPosition.PositionId
               AND positionObject.TenantId = userUnit.TenantId
               AND positionObject.UnitId = userUnit.UnitId
               AND positionObject.IsActive = 1
            LEFT JOIN fn_organization_position_level AS positionLevel
                ON positionLevel.Id = positionObject.PositionLevelId
               AND positionLevel.TenantId = userUnit.TenantId
               AND positionLevel.IsActive = 1
            WHERE userUnit.TenantId = @TenantId
              AND userUnit.UnitId IN @UnitIds
              AND userUnit.IsActive = 1
        ) AS ranked
        WHERE ranked.RowNumber = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindInitiatorPrimaryUnitId = new(
        "organization.workflow.find_initiator_primary_unit_id",
        """
        SELECT userUnit.UnitId
        FROM fn_organization_user_unit AS userUnit
        WHERE userUnit.TenantId = @TenantId
          AND userUnit.UserId = @UserId
          AND userUnit.IsActive = 1
          AND userUnit.IsPrimary = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}

/// <summary>Workflow 机构单元目录列表行投影。</summary>
internal sealed record WorkflowOrganizationUnitListRow(Guid Id, string Code, string Name);

/// <summary>Workflow 机构负责人解析行投影。</summary>
internal sealed record WorkflowUnitLeaderRow(Guid UnitId, Guid UserId);
