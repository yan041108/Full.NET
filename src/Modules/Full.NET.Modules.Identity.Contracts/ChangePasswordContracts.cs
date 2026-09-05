namespace Full.NET.Modules.Identity.Contracts;

/// <summary>当前用户自助改密请求。</summary>
/// <param name="CurrentPassword">当前密码明文。</param>
/// <param name="NewPassword">新密码明文。</param>
public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);
