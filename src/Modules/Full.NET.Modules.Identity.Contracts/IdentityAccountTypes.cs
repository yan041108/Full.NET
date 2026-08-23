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

    /// <summary>已发布的全部账号类型稳定机器码集合。</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        SuperAdmin,
        SysAdmin,
        NormalUser,
    ]);

    /// <summary>
    /// 判断给定值是否为已发布账号类型；忽略首尾空白并使用序号比较。
    /// </summary>
    /// <param name="value">待校验的账号类型字符串。</param>
    /// <returns>值为已发布账号类型时返回 <see langword="true"/>。</returns>
    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && All.Contains(value.Trim(), StringComparer.Ordinal);

    /// <summary>
    /// 将给定值规范化为已发布账号类型；非法或空值回退为 <see cref="NormalUser"/>。
    /// </summary>
    /// <param name="value">待规范化的账号类型字符串。</param>
    /// <returns>规范化后的账号类型机器码。</returns>
    public static string NormalizeOrDefault(string? value) =>
        IsValid(value) ? value!.Trim() : NormalUser;
}