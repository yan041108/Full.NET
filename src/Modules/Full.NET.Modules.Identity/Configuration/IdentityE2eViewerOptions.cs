namespace Full.NET.Modules.Identity.Configuration;

/// <summary>
/// Development 环境 E2E 受限 Host 查看者；密码为空时跳过播种。
/// </summary>
internal sealed class IdentityE2eViewerOptions
{
    public string Username { get; set; } = "e2e-viewer";

    public string Password { get; set; } = string.Empty;

    public string DisplayName { get; set; } = "E2E 受限查看者";
}
