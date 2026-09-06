namespace Full.NET.Modules.Identity.Features.SelfServiceProfile;

/// <summary>自助头像与签名媒体策略；服务端强制类型与体积边界，禁止任意 URL/路径绑定。</summary>
internal static class SelfServiceProfileMediaPolicy
{
    public const long AvatarMaxBytes = 2 * 1024 * 1024;
    public const long SignatureMaxBytes = 1 * 1024 * 1024;

    private static readonly HashSet<string> AvatarContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
    };

    private static readonly HashSet<string> SignatureContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
    };

    /// <summary>判断内容类型是否允许作为头像上传。</summary>
    public static bool IsAllowedAvatarContentType(string contentType) =>
        AvatarContentTypes.Contains(NormalizeContentType(contentType));

    /// <summary>判断内容类型是否允许作为签名上传。</summary>
    public static bool IsAllowedSignatureContentType(string contentType) =>
        SignatureContentTypes.Contains(NormalizeContentType(contentType));

    private static string NormalizeContentType(string contentType) =>
        contentType.Split(';', 2)[0].Trim();
}

/// <summary>自助资料媒体种类。</summary>
internal enum SelfServiceProfileMediaKind
{
    /// <summary>用户头像。</summary>
    Avatar = 0,

    /// <summary>用户签名图。</summary>
    Signature = 1,
}
