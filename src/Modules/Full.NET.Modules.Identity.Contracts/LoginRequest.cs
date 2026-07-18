namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 第一方管理端的账号密码登录输入。
/// </summary>
public sealed record LoginRequest(string Username, string Password);
