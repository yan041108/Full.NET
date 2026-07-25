namespace Full.NET.Modules.Organization.Contracts;

/// <summary>
/// Organization 模块对外返回的稳定错误码。
/// </summary>
public static class OrganizationErrorCodes
{
    /// <summary>机构编码在租户内已存在。</summary>
    public const string UnitCodeExists = "organization.units.code_exists";

    /// <summary>目标机构不存在或不属于当前租户。</summary>
    public const string UnitNotFound = "organization.units.not_found";

    /// <summary>用户-机构隶属已存在。</summary>
    public const string UserUnitAlreadyAssigned = "organization.user_units.already_assigned";

    /// <summary>目标隶属记录不存在。</summary>
    public const string UserUnitNotFound = "organization.user_units.not_found";

    /// <summary>目标 Host 用户不存在或已禁用。</summary>
    public const string UserUnitUserNotFound = "organization.user_units.user_not_found";

    /// <summary>职位编码在租户内已存在。</summary>
    public const string PositionCodeExists = "organization.positions.code_exists";

    /// <summary>目标职位不存在或不属于当前租户。</summary>
    public const string PositionNotFound = "organization.positions.not_found";

    /// <summary>用户-职位隶属已存在。</summary>
    public const string UserPositionAlreadyAssigned = "organization.user_positions.already_assigned";

    /// <summary>目标隶属记录不存在。</summary>
    public const string UserPositionNotFound = "organization.user_positions.not_found";

    /// <summary>目标 Host 用户不存在或已禁用。</summary>
    public const string UserPositionUserNotFound = "organization.user_positions.user_not_found";

    /// <summary>
    /// 获取当前目录中的全部稳定错误码。
    /// </summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        UnitCodeExists,
        UnitNotFound,
        UserUnitAlreadyAssigned,
        UserUnitNotFound,
        UserUnitUserNotFound,
        PositionCodeExists,
        PositionNotFound,
        UserPositionAlreadyAssigned,
        UserPositionNotFound,
        UserPositionUserNotFound,
    ]);
}
