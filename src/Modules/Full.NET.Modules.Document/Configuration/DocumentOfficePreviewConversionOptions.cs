using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Providers.OfficePreview;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Configuration;

/// <summary>Office 文档预览转换 Provider 与 Worker 运行参数。</summary>
public sealed class DocumentOfficePreviewConversionOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Document:OfficePreviewConversion";

    /// <summary>是否启用后台转换 Worker；关闭时任务仍可入队但不会自动处理。</summary>
    public bool Enabled { get; set; }

    /// <summary>当前启用的 Provider 稳定键。</summary>
    public string ProviderKey { get; set; } = DocumentOfficePreviewConversionProviderKeys.Disabled;

    /// <summary>允许提交转换的源文件最大字节数。</summary>
    public long MaxInputBytes { get; set; } = 52_428_800;

    /// <summary>转换 PDF 最大字节数；默认与 Files 的 10 MiB 上传预算一致，仍受 Files 自身限制。</summary>
    public long MaxOutputBytes { get; set; } = 10_485_760;

    /// <summary>单次转换超时秒数。</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>领取租约秒数；0 按转换超时加 120 秒生成，显式值须预留至少 30 秒上传及收尾预算。</summary>
    public int LeaseDurationSeconds { get; set; }

    internal int EffectiveLeaseSeconds => LeaseDurationSeconds == 0 ? TimeoutSeconds + 120 : LeaseDurationSeconds;

    /// <summary>Worker 轮询间隔秒数。</summary>
    public int PollSeconds { get; set; } = 15;

    /// <summary>每轮最多领取任务数。</summary>
    public int BatchSize { get; set; } = 5;

    /// <summary>隔离临时目录根路径；为空时使用系统临时目录下的专用子目录。</summary>
    public string? TempRootPath { get; set; }

    /// <summary>外部 HTTP Provider 配置。</summary>
    public ExternalHttpProviderOptions ExternalHttp { get; set; } = new();

    /// <summary>外部进程 Provider 配置。</summary>
    public ExternalProcessProviderOptions ExternalProcess { get; set; } = new();
}

/// <summary>通过 HTTP 网关调用外部 Office→PDF 转换服务的参数。</summary>
public sealed class ExternalHttpProviderOptions
{
    /// <summary>转换服务端点，必须接受 multipart 文件并返回 PDF 字节流。</summary>
    public string? BaseUrl { get; set; }

    /// <summary>可选 API Key，通过 X-Api-Key 请求头传递。</summary>
    public string? ApiKey { get; set; }
}

/// <summary>通过子进程调用运维部署的转换器可执行文件。</summary>
public sealed class ExternalProcessProviderOptions
{
    /// <summary>前台转换器可执行文件绝对路径；退出前须完成输出且回收后代，禁止分离后台任务。</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>
    /// 参数模板，必须包含 {inputPath} 与 {outputPath} 占位符；
    /// 模板先按引号分词，再替换路径并写入 ArgumentList，路径中的空格或引号不会成为额外参数。
    /// 双引号支持反斜杠转义；词首单引号支持分组，单词内部的撇号保持字面值。
    /// </summary>
    public string ArgumentTemplate { get; set; } = "\"{inputPath}\" \"{outputPath}\"";
}

/// <summary>校验 Office 预览转换配置边界，避免 Worker 在无效 Provider 上空转。</summary>
internal sealed class DocumentOfficePreviewConversionOptionsValidator
    : IValidateOptions<DocumentOfficePreviewConversionOptions>
{
    public ValidateOptionsResult Validate(string? name, DocumentOfficePreviewConversionOptions options)
    {
        var failures = new List<string>();
        ValidatePositive("Document:OfficePreviewConversion:MaxInputBytes", options.MaxInputBytes, failures);
        if (options.MaxOutputBytes <= 0 || options.MaxOutputBytes > int.MaxValue)
            failures.Add("Document:OfficePreviewConversion:MaxOutputBytes must be between 1 and Int32.MaxValue.");
        ValidateRange("Document:OfficePreviewConversion:TimeoutSeconds", options.TimeoutSeconds, 5, 3600, failures);
        if (options.LeaseDurationSeconds != 0 && (options.LeaseDurationSeconds < options.TimeoutSeconds + 30 || options.LeaseDurationSeconds > 7200))
            failures.Add("Document:OfficePreviewConversion:LeaseDurationSeconds must be zero or between TimeoutSeconds + 30 and 7200.");
        ValidateRange("Document:OfficePreviewConversion:PollSeconds", options.PollSeconds, 5, 3600, failures);
        ValidateRange("Document:OfficePreviewConversion:BatchSize", options.BatchSize, 1, 50, failures);

        if (!IsKnownProviderKey(options.ProviderKey))
        {
            failures.Add("Document:OfficePreviewConversion:ProviderKey is invalid.");
        }

        if (options.Enabled
            && string.Equals(
                options.ProviderKey,
                DocumentOfficePreviewConversionProviderKeys.ExternalHttp,
                StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(options.ExternalHttp.BaseUrl))
            {
                failures.Add("Document:OfficePreviewConversion:ExternalHttp:BaseUrl is required when provider is external_http.");
            }
        }

        if (options.Enabled
            && string.Equals(
                options.ProviderKey,
                DocumentOfficePreviewConversionProviderKeys.ExternalProcess,
                StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(options.ExternalProcess.ExecutablePath))
            {
                failures.Add("Document:OfficePreviewConversion:ExternalProcess:ExecutablePath is required when provider is external_process.");
            }
            try { DocumentPreviewProcessArguments.Build(options.ExternalProcess.ArgumentTemplate, "input", "output"); }
            catch (InvalidDataException)
            {
                failures.Add("Document:OfficePreviewConversion:ExternalProcess:ArgumentTemplate requires both placeholders and balanced quotes.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsKnownProviderKey(string? providerKey) =>
        string.Equals(providerKey, DocumentOfficePreviewConversionProviderKeys.ExternalHttp, StringComparison.Ordinal)
        || string.Equals(providerKey, DocumentOfficePreviewConversionProviderKeys.ExternalProcess, StringComparison.Ordinal)
        || string.Equals(providerKey, DocumentOfficePreviewConversionProviderKeys.Disabled, StringComparison.Ordinal);

    private static void ValidatePositive(string key, long value, ICollection<string> failures)
    {
        if (value <= 0)
        {
            failures.Add($"{key} must be greater than zero.");
        }
    }

    private static void ValidateRange(string key, int value, int min, int max, ICollection<string> failures)
    {
        if (value < min || value > max)
        {
            failures.Add($"{key} must be between {min} and {max}.");
        }
    }
}
