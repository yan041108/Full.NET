using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ObservabilityAdmin.Configuration;

internal sealed class ObservabilityAdminOptionsValidator
    : IValidateOptions<ObservabilityAdminOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        ObservabilityAdminOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.LogRootPath))
        {
            return ValidateOptionsResult.Fail("日志根目录不能为空。");
        }

        if (options.MaximumListFiles is < 1 or > 100)
        {
            return ValidateOptionsResult.Fail("日志文件列表上限必须在 1 到 100 之间。");
        }

        if (options.DefaultTailLines < 1
            || options.MaximumTailLines < options.DefaultTailLines
            || options.MaximumTailLines > 5_000)
        {
            return ValidateOptionsResult.Fail("日志尾读行数边界无效。");
        }

        if (options.DefaultTailBytes < 1
            || options.MaximumTailBytes < options.DefaultTailBytes
            || options.MaximumTailBytes > 1024 * 1024)
        {
            return ValidateOptionsResult.Fail("日志尾读字节边界无效。");
        }

        return ValidateOptionsResult.Success;
    }
}
