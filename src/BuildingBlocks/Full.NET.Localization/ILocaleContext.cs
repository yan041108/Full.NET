namespace Full.NET.Localization;

/// <summary>
/// 暴露当前执行上下文已经协商并规范化的语言标签。
/// </summary>
public interface ILocaleContext
{
    /// <summary>
    /// 获取当前执行上下文的规范语言标签。
    /// </summary>
    string CurrentLocale { get; }
}
