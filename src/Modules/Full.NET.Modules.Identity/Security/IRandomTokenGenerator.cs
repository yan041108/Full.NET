namespace Full.NET.Modules.Identity.Security;

/// <summary>
/// 加密安全随机令牌生成器抽象；由登录/刷新流程用于 Refresh Token 与 CSRF Token，
/// 以及 API Key 生成流程生成 Secret。
/// </summary>
internal interface IRandomTokenGenerator
{
    /// <summary>
    /// 生成指定字节数的随机令牌并以 URL 安全格式返回。
    /// </summary>
    string Generate(int byteCount);
}
