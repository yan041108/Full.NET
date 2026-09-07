using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ImportExport.Configuration;

/// <summary>校验 ImportExport 模块配置边界，防止运行时不安全默认值进入生产。</summary>
internal sealed class ImportExportOptionsValidator : IValidateOptions<ImportExportOptions>
{
    public ValidateOptionsResult Validate(string? name, ImportExportOptions options)
    {
        if (options.MaxUploadBytes is < 1 or > 1024 * 1024)
        {
            return ValidateOptionsResult.Fail("导入上传大小上限必须在 1 字节到 1 MiB 之间。");
        }

        if (options.MaxPreviewRows is < 1 or > 1_000)
        {
            return ValidateOptionsResult.Fail("导入预校验行数上限必须在 1 到 1000 之间。");
        }

        if (options.PollSeconds is < 5 or > 300)
        {
            return ValidateOptionsResult.Fail("导入执行轮询间隔必须在 5 到 300 秒之间。");
        }

        if (options.BatchSize is < 1 or > 200)
        {
            return ValidateOptionsResult.Fail("导入执行批大小必须在 1 到 200 之间。");
        }

        if (options.LeaseSeconds is < 30 or > 3600)
        {
            return ValidateOptionsResult.Fail("导入执行租约必须在 30 到 3600 秒之间。");
        }

        return ValidateOptionsResult.Success;
    }
}
