using Microsoft.AspNetCore.Builder;

namespace Full.NET.Localization;

/// <summary>
/// 将 Full.NET 请求语言协商加入 ASP.NET Core 管道。
/// </summary>
public static class LocalizationApplicationBuilderExtensions
{
    /// <summary>
    /// 在后续异常、认证、租户与授权边界之前建立请求 Culture。
    /// </summary>
    /// <param name="app">应用管道构建器。</param>
    /// <returns>原应用管道构建器。</returns>
    public static IApplicationBuilder UseFullNetLocalization(
        this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseRequestLocalization();
    }
}
