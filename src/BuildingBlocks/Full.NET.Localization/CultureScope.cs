using System.Globalization;

namespace Full.NET.Localization;

/// <summary>
/// 在当前异步执行流内临时切换格式化文化与 UI 文化。
/// </summary>
/// <remarks>
/// 该作用域依赖 .NET Culture 的异步上下文隔离；调用方必须在创建作用域的执行流内释放它。
/// </remarks>
public static class CultureScope
{
    /// <summary>
    /// 切换当前执行流的 Culture，并在释放返回值时恢复调用方状态。
    /// </summary>
    /// <param name="locale">调用方经 <see cref="ILocaleNormalizer"/> 处理后的规范语言标签。</param>
    /// <returns>负责恢复调用方 Culture 的作用域。</returns>
    /// <exception cref="ArgumentException">语言标签为空或无效时抛出。</exception>
    /// <exception cref="CultureNotFoundException">运行时无法识别语言标签时抛出。</exception>
    public static IDisposable Push(string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);

        var culture = CultureInfo.GetCultureInfo(locale);
        var scope = new Scope(
            CultureInfo.CurrentCulture,
            CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return scope;
    }

    private sealed class Scope(
        CultureInfo previousCulture,
        CultureInfo previousUiCulture) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            _disposed = true;
        }
    }
}
