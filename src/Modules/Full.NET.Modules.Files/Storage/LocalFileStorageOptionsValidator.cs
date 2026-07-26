using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Storage;

/// <summary>在宿主启动时拒绝不可用的本地文件存储配置，避免首个上传请求才暴露配置错误。</summary>
internal sealed class LocalFileStorageOptionsValidator : IValidateOptions<LocalFileStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, LocalFileStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            return ValidateOptionsResult.Fail(
                "Files:Local:RootPath must be configured.");
        }

        if (options.MaxUploadBytes <= 0)
        {
            return ValidateOptionsResult.Fail(
                "Files:Local:MaxUploadBytes must be greater than zero.");
        }

        try
        {
            var fullPath = Path.GetFullPath(options.RootPath);
            if (File.Exists(fullPath))
            {
                return ValidateOptionsResult.Fail(
                    "Files:Local:RootPath must identify a directory, not a file.");
            }
        }
        catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ValidateOptionsResult.Fail(
                "Files:Local:RootPath must be a valid file-system path.");
        }

        return ValidateOptionsResult.Success;
    }
}
