using System.Diagnostics.Metrics;
using System.Globalization;
using Full.NET.Abstractions.Results;

namespace Full.NET.Hosting.Api;

/// <summary>
/// 聚合 Hosting 与模块资源，并按最长错误码前缀选择唯一资源来源。
/// </summary>
public sealed class ResourceErrorMessageLocalizer : IErrorMessageLocalizer
{
    /// <summary>
    /// OpenTelemetry 注册与监听必须共同使用的稳定 Meter 名称。
    /// </summary>
    public const string MeterName = "Full.NET.Hosting.Localization";

    private static readonly Meter LocalizationMeter =
        new(MeterName);
    private static readonly Counter<long> FallbackCounter =
        LocalizationMeter.CreateCounter<long>("fullnet.localization.error.fallbacks");

    private readonly IErrorResourceSource[] _sources;
    private readonly NamedMessageFormatter _formatter;

    /// <summary>
    /// 初始化资源错误消息本地化器。
    /// </summary>
    /// <param name="sources">Hosting 与模块注册的资源来源。</param>
    /// <param name="formatter">只处理精确命名参数的安全格式器。</param>
    public ResourceErrorMessageLocalizer(
        IEnumerable<IErrorResourceSource> sources,
        NamedMessageFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(formatter);
        _sources = sources
            .Select(source => ValidateSource(source))
            .OrderByDescending(source => source.Prefix.Length)
            .ThenBy(source => source.Prefix, StringComparer.Ordinal)
            .ToArray();
        _formatter = formatter;
    }

    /// <inheritdoc />
    public string Localize(Error error, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(culture);

        var source = _sources.FirstOrDefault(candidate =>
            error.Code.StartsWith(candidate.Prefix, StringComparison.Ordinal));
        if (source is null
            || !source.TryGetTemplate(error.Code, culture, out var template)
            || !_formatter.TryFormat(
                template,
                error.Arguments,
                culture,
                out var message))
        {
            RecordFallback(error.Code, culture.Name);
            return error.DefaultMessage;
        }

        return message;
    }

    private static void RecordFallback(string code, string locale) =>
        FallbackCounter.Add(
            1,
            new KeyValuePair<string, object?>("code", code),
            new KeyValuePair<string, object?>("locale", locale));

    private static IErrorResourceSource ValidateSource(IErrorResourceSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(source.Prefix)
            || !source.Prefix.EndsWith(".", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "错误资源前缀必须以点号结束，确保按完整代码段匹配。",
                nameof(source));
        }

        return source;
    }
}
