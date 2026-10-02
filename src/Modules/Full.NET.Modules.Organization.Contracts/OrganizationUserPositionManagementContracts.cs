namespace Full.NET.Modules.Organization.Contracts;

/// <summary>
/// 租户用户-职位隶属管理 API 契约。
/// </summary>
public static class OrganizationUserPositionManagementPermissions
{
    /// <summary>分页查询用户-职位隶属。</summary>
    public const string Read = "organization.user_positions.read";

    /// <summary>分配用户-职位隶属。</summary>
    public const string Create = "organization.user_positions.create";

    /// <summary>设为主职位。</summary>
    public const string Update = "organization.user_positions.update";

    /// <summary>取消用户-职位隶属。</summary>
    public const string Disable = "organization.user_positions.disable";

    /// <summary>迁移 065 前遗留的粗粒度写权限；不再进入可分配目录。</summary>
    public const string Write = "organization.user_positions.write";
}

/// <summary>创建用户-职位隶属请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="UserId">被分配职位的用户标识。</param>
/// <param name="PositionId">目标职位标识。</param>
/// <param name="IsPrimary">是否设为主职位；同一用户同一时刻仅允许一个主职位。</param>
public sealed record CreateOrganizationUserPositionRequest(
    Guid UserId,
    Guid PositionId,
    bool IsPrimary);

/// <summary>更新用户-职位隶属请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="IsPrimary">是否设为主职位；设为 <see langword="true"/> 时会自动取消同用户其他职位的主标记。</param>
/// <param name="Version">当前实体乐观锁版本；与服务端不一致时拒绝更新。</param>
public sealed record UpdateOrganizationUserPositionRequest(
    bool IsPrimary,
    int Version);

/// <summary>用户-职位隶属列表项与详情。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">隶属关系稳定标识。</param>
/// <param name="UserId">用户标识。</param>
/// <param name="Username">用户登录名；用于列表展示，可能随用户改名而变化。</param>
/// <param name="DisplayName">用户显示名称；用于列表展示。</param>
/// <param name="PositionId">职位标识。</param>
/// <param name="PositionCode">职位编码；创建后不可变。</param>
/// <param name="PositionName">职位显示名称。</param>
/// <param name="IsPrimary">是否为该用户的主职位。</param>
/// <param name="IsActive">隶属是否有效；取消隶属后置为 <see langword="false"/>。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）；从未更新时为 <see langword="null"/>。</param>
/// <param name="Version">当前乐观锁版本；用于并发写控制。</param>
public sealed record OrganizationUserPositionResponse(
    Guid Id,
    Guid UserId,
    string Username,
    string DisplayName,
    Guid PositionId,
    string PositionCode,
    string PositionName,
    bool IsPrimary,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);
