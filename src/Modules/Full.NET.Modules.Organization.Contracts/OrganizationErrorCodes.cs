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

    /// <summary>
    /// 获取当前目录中的全部稳定错误码。
    /// </summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        UnitCodeExists,
        UnitNotFound,
    ]);
}
