using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Storage;

/// <summary>
/// Files:Oss 配置。AccessKey 不得出现在普通 appsettings，只从环境变量解析。
/// </summary>
public sealed class OssFileStorageOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Files:Oss";

    /// <summary>AccessKeyId 环境变量。</summary>
    public const string AccessKeyIdEnvironmentVariable = "Files__Oss__AccessKeyId";

    /// <summary>AccessKeySecret 环境变量。</summary>
    public const string AccessKeySecretEnvironmentVariable = "Files__Oss__AccessKeySecret";

    /// <summary>OSS 区域端点主机名，例如 oss-cn-hangzhou.aliyuncs.com。</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>存放文件的 Bucket 名称。</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>单对象上传/下载/删除请求的总超时。</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>是否允许非 HTTPS 端点（仅非 Production 或显式受信内网场景）。</summary>
    public bool AllowInsecureEndpoint { get; set; }
}

/// <summary>
/// 校验 OSS 配置；Production 或默认 Provider 为 oss 时必须完整可用。
/// </summary>
internal sealed class OssFileStorageOptionsValidator(
    IHostEnvironment environment,
    string defaultProviderKey) : IValidateOptions<OssFileStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, OssFileStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var requireComplete = environment.IsProduction()
            || string.Equals(
                defaultProviderKey,
                OssHostFileBlobStorage.Key,
                StringComparison.Ordinal);
        if (!requireComplete)
        {
            if (string.IsNullOrWhiteSpace(options.BucketName)
                && string.IsNullOrWhiteSpace(options.Endpoint))
            {
                return ValidateOptionsResult.Success;
            }
        }

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.BucketName))
        {
            failures.Add($"{OssFileStorageOptions.SectionName}:BucketName is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            failures.Add($"{OssFileStorageOptions.SectionName}:Endpoint is required.");
        }
        else if (!IsValidEndpoint(options.Endpoint, options.AllowInsecureEndpoint, environment))
        {
            failures.Add(
                $"{OssFileStorageOptions.SectionName}:Endpoint must be a valid HTTPS host "
                + "(or AllowInsecureEndpoint in non-Production).");
        }

        if (options.RequestTimeout <= TimeSpan.Zero
            || options.RequestTimeout > TimeSpan.FromMinutes(30))
        {
            failures.Add(
                $"{OssFileStorageOptions.SectionName}:RequestTimeout must be in (0, 30 minutes].");
        }

        if (requireComplete
            && string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(OssFileStorageOptions.AccessKeyIdEnvironmentVariable))
            && string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(OssFileStorageOptions.AccessKeySecretEnvironmentVariable)))
        {
            failures.Add(
                $"{OssFileStorageOptions.SectionName}:credentials must come from environment "
                + $"({OssFileStorageOptions.AccessKeyIdEnvironmentVariable}/"
                + $"{OssFileStorageOptions.AccessKeySecretEnvironmentVariable}); "
                + "do not store secrets in appsettings.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsValidEndpoint(
        string endpoint,
        bool allowInsecure,
        IHostEnvironment environment)
    {
        var trimmed = endpoint.Trim();
        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
            {
                return false;
            }

            return absolute.Scheme == Uri.UriSchemeHttps
                || (allowInsecure && !environment.IsProduction() && absolute.Scheme == Uri.UriSchemeHttp);
        }

        return !trimmed.Contains('/', StringComparison.Ordinal)
            && !trimmed.Contains(' ', StringComparison.Ordinal);
    }
}
