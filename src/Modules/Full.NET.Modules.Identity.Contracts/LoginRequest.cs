namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 第一方管理端的账号密码登录输入。
/// </summary>
/// <param name="Username">登录名原文；是否允许邮箱、手机号或用户名由服务端认证策略决定。</param>
/// <param name="Password">待验证的明文密码，只允许存在于当前请求边界，禁止写入日志或缓存。</param>
public sealed record LoginRequest(string Username, string Password);
