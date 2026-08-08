namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 账号类型稳定机器码，对齐 Admin.NET AccountTypeEnum 语义。
/// </summary>
public static class IdentityAccountTypes
{
    /// <summary>超级管理员，平台级最高权限账号。</summary>
    public const string SuperAdmin = "super_admin";

    /// <summary>系统管理员，管理 Host 或租户系统模块。</summary>
    public const string SysAdmin = "sys_admin";

    /// <summary>普通用户，默认账号类型。</summary>
    public const string NormalUser = "normal_user";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        SuperAdmin,
        SysAdmin,
        NormalUser,
    ]);

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && All.Contains(value.Trim(), StringComparer.Ordinal);

    public static string NormalizeOrDefault(string? value) =>
        IsValid(value) ? value!.Trim() : NormalUser;
}