using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Organization.Persistence;

/// <summary>Workflow 机构负责人解析使用的只读 SQL 集合。</summary>
internal static class WorkflowOrganizationAssigneeSql
{
    public static readonly SqlStatement IsPositionMember = new("organization.workflow.is_position_member", """
        SELECT COUNT(1) FROM fn_organization_user_position AS membership
        INNER JOIN fn_organization_position AS positionObject ON positionObject.Id=membership.PositionId AND positionObject.TenantId=membership.TenantId AND positionObject.IsActive=1
        LEFT JOIN fn_organization_unit AS unit ON unit.Id=positionObject.UnitId AND unit.TenantId=membership.TenantId AND unit.IsActive=1
        WHERE membership.TenantId=@TenantId AND membership.UserId=@UserId AND membership.PositionId IN @PositionIds AND membership.IsActive=1
          AND (positionObject.UnitId IS NULL OR unit.Id IS NOT NULL)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement IsUnitMember = new("organization.workflow.is_unit_member", """
        SELECT COUNT(1) FROM fn_organization_user_unit AS membership
        INNER JOIN fn_organization_unit AS unit ON unit.Id=membership.UnitId AND unit.TenantId=membership.TenantId AND unit.IsActive=1
        WHERE membership.TenantId=@TenantId AND membership.UserId=@UserId AND membership.UnitId IN @UnitIds AND membership.IsActive=1
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string ActivePositions = """
        FROM fn_organization_position AS positionObject
        LEFT JOIN fn_organization_unit AS unit ON unit.Id=positionObject.UnitId AND unit.TenantId=positionObject.TenantId AND unit.IsActive=1
        WHERE positionObject.TenantId=@TenantId AND positionObject.IsActive=1 AND (positionObject.UnitId IS NULL OR unit.Id IS NOT NULL)
        """;
    public static readonly SqlStatement CountActivePositions = new("organization.workflow.count_active_positions",
        "SELECT COUNT(1) " + ActivePositions, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement ListActivePositionsSqlServer = new("organization.workflow.list_active_positions.sqlserver",
        "SELECT positionObject.Id, positionObject.Code, positionObject.Name " + ActivePositions + " ORDER BY positionObject.DisplayOrder, positionObject.Code, positionObject.Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement ListActivePositionsMySql = new("organization.workflow.list_active_positions.mysql",
        "SELECT positionObject.Id, positionObject.Code, positionObject.Name " + ActivePositions + " ORDER BY positionObject.DisplayOrder, positionObject.Code, positionObject.Id LIMIT @PageSize OFFSET @Offset",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement FindActivePositionsByIds = new("organization.workflow.find_active_positions_by_ids",
        "SELECT positionObject.Id, positionObject.Code, positionObject.Name " + ActivePositions + " AND positionObject.Id IN @PositionIds ORDER BY positionObject.Id",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    /// <summary>每个来源最多返回上限加一名不同成员，让消费方识别超限，不能截断为合法小群体。</summary>
    public static readonly SqlStatement FindPositionMembers = new("organization.workflow.position_members", """
        SELECT ranked.SourceId, ranked.UserId FROM (
            SELECT members.SourceId, members.UserId, ROW_NUMBER() OVER (PARTITION BY members.SourceId ORDER BY members.UserId) AS RowNumber
            FROM (
                SELECT DISTINCT positionObject.Id AS SourceId, membership.UserId
                FROM fn_organization_position AS positionObject
                INNER JOIN fn_organization_user_position AS membership ON membership.PositionId=positionObject.Id AND membership.TenantId=positionObject.TenantId AND membership.IsActive=1
                LEFT JOIN fn_organization_unit AS unit ON unit.Id=positionObject.UnitId AND unit.TenantId=positionObject.TenantId AND unit.IsActive=1
                WHERE positionObject.TenantId=@TenantId AND positionObject.Id IN @PositionIds AND positionObject.IsActive=1
                    AND (positionObject.UnitId IS NULL OR unit.Id IS NOT NULL)
            ) AS members
        ) AS ranked WHERE ranked.RowNumber<=@Take ORDER BY ranked.SourceId, ranked.UserId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement FindUnitMembers = new("organization.workflow.unit_members", """
        SELECT ranked.SourceId, ranked.UserId FROM (
            SELECT members.SourceId, members.UserId, ROW_NUMBER() OVER (PARTITION BY members.SourceId ORDER BY members.UserId) AS RowNumber
            FROM (
                SELECT DISTINCT unit.Id AS SourceId, membership.UserId
                FROM fn_organization_unit AS unit
                INNER JOIN fn_organization_user_unit AS membership ON membership.UnitId=unit.Id AND membership.TenantId=unit.TenantId AND membership.IsActive=1
                WHERE unit.TenantId=@TenantId AND unit.Id IN @UnitIds AND unit.IsActive=1
            ) AS members
        ) AS ranked WHERE ranked.RowNumber<=@Take ORDER BY ranked.SourceId, ranked.UserId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

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


/// <summary>可信岗位候选行；不包含组织模块内部管理属性。</summary>
internal sealed record WorkflowOrganizationPositionListRow(Guid Id, string Code, string Name);

/// <summary>组织来源与成员的有界关联事实。</summary>
internal sealed record WorkflowOrganizationAssigneeMemberRow(Guid SourceId, Guid UserId);
