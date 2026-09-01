using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Storage;

/// <summary>S3 兼容对象存储端点模式。</summary>
public enum S3EndpointMode
{
    /// <summary>AWS 原生区域端点；必须提供 Region。</summary>
    Aws = 0,

    /// <summary>自定义 S3 兼容端点（MinIO 等）；必须提供 ServiceUrl、签名 Region 与 ForcePathStyle。</summary>
    Custom = 1,
}

/// <summary>
/// Files:S3 配置。AccessKey/SecretKey 不得出现在普通 appsettings，只从环境变量或工作负载身份解析。
/// </summary>
public sealed class S3FileStorageOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Files:S3";

    /// <summary>凭据环境变量：优先 Files 专用键，其次标准 AWS 键。</summary>
    public const string AccessKeyEnvironmentVariable = "Files__S3__AccessKeyId";

    /// <summary>Secret Access Key 环境变量；同 AccessKey 解析优先级。</summary>
    public const string SecretKeyEnvironmentVariable = "Files__S3__SecretAccessKey";

    /// <summary>临时会话令牌环境变量；仅 Assume Role 等场景需要。</summary>
    public const string SessionTokenEnvironmentVariable = "Files__S3__SessionToken";

    /// <summary>端点模式；AWS 原生或自定义兼容端点（MinIO 等）。</summary>
    public S3EndpointMode EndpointMode { get; set; } = S3EndpointMode.Aws;

    /// <summary>自定义端点基址（仅 Custom）；Production 要求 HTTPS 或明确受信内网。</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>AWS 区域或自定义端点的签名 Region。</summary>
    public string? Region { get; set; }

    /// <summary>存放文件的 Bucket 名称；生产环境应开启版本控制与服务端加密。</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>自定义端点通常需要 path-style；Aws 模式默认 false。</summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>是否允许非 HTTPS 的 Custom ServiceUrl（仅非 Production 或显式受信内网场景）。</summary>
    public bool AllowInsecureServiceUrl { get; set; }

    /// <summary>单对象上传/下载/删除请求的总超时；大文件分片场景应相应延长。</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(100);
}

/// <summary>
/// 校验 S3 配置；Production 或默认 Provider 为 s3 时必须完整可用。
/// defaultProviderKey 在模块注册时从配置快照注入，避免 Options 校验与 Provider 解析形成 DI 环。
/// </summary>
internal sealed class S3FileStorageOptionsValidator(
    IHostEnvironment environment,
    string defaultProviderKey) : IValidateOptions<S3FileStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, S3FileStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var requireComplete = environment.IsProduction()
            || string.Equals(
                defaultProviderKey,
                S3HostFileBlobStorage.Key,
                StringComparison.Ordinal);
        if (!requireComplete)
        {
            // 开发默认仍用 local：允许未配置 S3，避免本机无凭据时启动失败。
            if (string.IsNullOrWhiteSpace(options.BucketName)
                && string.IsNullOrWhiteSpace(options.ServiceUrl)
                && string.IsNullOrWhiteSpace(options.Region))
            {
                return ValidateOptionsResult.Success;
            }
        }

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.BucketName))
        {
            failures.Add($"{S3FileStorageOptions.SectionName}:BucketName is required.");
        }

        if (options.RequestTimeout <= TimeSpan.Zero
            || options.RequestTimeout > TimeSpan.FromMinutes(30))
        {
            failures.Add(
                $"{S3FileStorageOptions.SectionName}:RequestTimeout must be in (0, 30 minutes].");
        }

        switch (options.EndpointMode)
        {
            case S3EndpointMode.Aws:
                if (string.IsNullOrWhiteSpace(options.Region))
                {
                    failures.Add(
                        $"{S3FileStorageOptions.SectionName}:Region is required when EndpointMode=Aws.");
                }

                break;
            case S3EndpointMode.Custom:
                if (string.IsNullOrWhiteSpace(options.ServiceUrl))
                {
                    failures.Add(
                        $"{S3FileStorageOptions.SectionName}:ServiceUrl is required when EndpointMode=Custom.");
                }
                else if (!Uri.TryCreate(options.ServiceUrl, UriKind.Absolute, out var serviceUri)
                    || (serviceUri.Scheme != Uri.UriSchemeHttps
                        && !(options.AllowInsecureServiceUrl && !environment.IsProduction())))
                {
                    failures.Add(
                        $"{S3FileStorageOptions.SectionName}:ServiceUrl must be HTTPS "
                        + "(or AllowInsecureServiceUrl in non-Production).");
                }

                if (string.IsNullOrWhiteSpace(options.Region))
                {
                    failures.Add(
                        $"{S3FileStorageOptions.SectionName}:Region (signing region) is required when EndpointMode=Custom.");
                }

                if (!options.ForcePathStyle)
                {
                    failures.Add(
                        $"{S3FileStorageOptions.SectionName}:ForcePathStyle must be true when EndpointMode=Custom.");
                }

                break;
            default:
                failures.Add($"{S3FileStorageOptions.SectionName}:EndpointMode is invalid.");
                break;
        }

        if (requireComplete
            && string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(S3FileStorageOptions.AccessKeyEnvironmentVariable))
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID"))
            && string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI"))
            && string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("AWS_WEB_IDENTITY_TOKEN_FILE")))
        {
            // 工作负载身份或挂载 Secret 通常注入上述环境变量之一；均缺失则启动失败。
            failures.Add(
                $"{S3FileStorageOptions.SectionName}:credentials must come from environment "
                + $"({S3FileStorageOptions.AccessKeyEnvironmentVariable}/AWS_ACCESS_KEY_ID) "
                + "or workload identity; do not store secrets in appsettings.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
