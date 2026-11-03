using Full.NET.Abstractions.Results;
using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Printing.Domain;
using System.Text.RegularExpressions;

namespace Full.NET.Modules.Printing.Features.RenderForms;

/// <summary>固定业务布局的有界字段绑定；复用正式 Printing 编码与脚本预处理。</summary>
internal sealed partial class PrintingFormRenderer : IPrintingFormRenderer
{
    [GeneratedRegex(@"\{\{\s*(?<key>[a-zA-Z0-9_]+)\s*\}\}", RegexOptions.CultureInvariant, 100)]
    private static partial Regex BindingPattern();
    public Result<string> Render(string layoutHtml, IReadOnlyDictionary<string, string?> fields,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layoutHtml);
        ArgumentNullException.ThrowIfNull(fields);
        cancellationToken.ThrowIfCancellationRequested();
        if (layoutHtml.Length > 65536 || fields.Count > 128 || fields.Any(p => string.IsNullOrWhiteSpace(p.Key)
            || p.Key.Length > 64 || p.Value?.Length > 32768) || fields.Sum(p => (long)(p.Value?.Length ?? 0)) > 262144)
            return Failure();
        if (BindingPattern().Matches(layoutHtml).Any(match => !fields.ContainsKey(match.Groups["key"].Value))) return Failure();
        var html = PrintingHtmlRenderer.Render(layoutHtml, fields);
        cancellationToken.ThrowIfCancellationRequested();
        return html.Length <= 1048576 ? Result<string>.Success(html) : Failure();
    }
    private static Result<string> Failure() => Result<string>.Failure(new Error(PrintingErrorCodes.BindingFailed,
        "The form exceeds its binding or output budget.", ErrorType.Validation));
}
