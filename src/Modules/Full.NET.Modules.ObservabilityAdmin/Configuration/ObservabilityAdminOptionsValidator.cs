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

        if (options.Instances.Count > 50)
        {
            return ValidateOptionsResult.Fail("实例目录登记项不能超过 50 条。");
        }

        foreach (var instance in options.Instances)
        {
            if (string.IsNullOrWhiteSpace(instance.InstanceKey))
            {
                return ValidateOptionsResult.Fail("实例目录登记项的 InstanceKey 不能为空。");
            }

            if (instance.InstanceKey.Length > 128)
            {
                return ValidateOptionsResult.Fail("实例目录登记项的 InstanceKey 过长。");
            }
        }

        if (options.InstanceKey.Length > 128)
        {
            return ValidateOptionsResult.Fail("当前实例 InstanceKey 过长。");
        }

        if (options.InstanceDisplayName.Length > 256)
        {
            return ValidateOptionsResult.Fail("当前实例展示名称过长。");
        }

        if (options.HostRole.Length > 64)
        {
            return ValidateOptionsResult.Fail("宿主角色过长。");
        }

        return ValidateOptionsResult.Success;
    }
}
