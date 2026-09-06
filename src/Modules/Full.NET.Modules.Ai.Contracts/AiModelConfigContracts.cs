namespace Full.NET.Modules.Ai.Contracts;

/// <summary>受支持的 AI 模型提供程序键。</summary>
public static class AiProviderKeys
{
    /// <summary>OpenAI 兼容 HTTP API（含官方 OpenAI 与兼容网关）。</summary>
    public const string OpenAiCompatible = "openai_compatible";

    /// <summary>Ollama 本地推理服务。</summary>
    public const string Ollama = "ollama";
}

/// <summary>模型连通性测试状态键。</summary>
public static class AiModelTestStatusKeys
{
    /// <summary>最近一次测试成功。</summary>
    public const string Succeeded = "succeeded";

    /// <summary>最近一次测试失败。</summary>
    public const string Failed = "failed";
}

/// <summary>AI 模型配置列表项；端点与密钥已脱敏。</summary>
/// <param name="Id">配置稳定标识。</param>
/// <param name="TenantId">所属租户标识；为空表示 Host 级配置。</param>
/// <param name="Name">显示名称。</param>
/// <param name="ProviderKey">提供程序键。</param>
/// <param name="MaskedEndpointBaseUrl">脱敏后的端点基址。</param>
/// <param name="ModelId">模型标识。</param>
/// <param name="HasApiKey">是否已配置 API 密钥。</param>
/// <param name="IsDefault">是否为当前作用域默认模型。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="LastTestedAtUtc">最近一次连通性测试时间（UTC）。</param>
/// <param name="LastTestStatusKey">最近一次测试状态键。</param>
/// <param name="LastTestMessage">最近一次测试摘要。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record AiModelConfigListItem(
    Guid Id,
    Guid? TenantId,
    string Name,
    string ProviderKey,
    string MaskedEndpointBaseUrl,
    string ModelId,
    bool HasApiKey,
    bool IsDefault,
    bool IsEnabled,
    DateTimeOffset? LastTestedAtUtc,
    string? LastTestStatusKey,
    string? LastTestMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>AI 模型配置详情；不回显 API 密钥。</summary>
/// <param name="Id">配置稳定标识。</param>
/// <param name="TenantId">所属租户标识；为空表示 Host 级配置。</param>
/// <param name="Name">显示名称。</param>
/// <param name="ProviderKey">提供程序键。</param>
/// <param name="EndpointBaseUrl">端点基址。</param>
/// <param name="ModelId">模型标识。</param>
/// <param name="OrganizationId">OpenAI 组织标识（可选）。</param>
/// <param name="HasApiKey">是否已配置 API 密钥。</param>
/// <param name="IsDefault">是否为当前作用域默认模型。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="LastTestedAtUtc">最近一次连通性测试时间（UTC）。</param>
/// <param name="LastTestStatusKey">最近一次测试状态键。</param>
/// <param name="LastTestMessage">最近一次测试摘要。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record AiModelConfigResponse(
    Guid Id,
    Guid? TenantId,
    string Name,
    string ProviderKey,
    string EndpointBaseUrl,
    string ModelId,
    string? OrganizationId,
    bool HasApiKey,
    bool IsDefault,
    bool IsEnabled,
    DateTimeOffset? LastTestedAtUtc,
    string? LastTestStatusKey,
    string? LastTestMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>创建 AI 模型配置请求。</summary>
/// <param name="TenantId">所属租户标识；为空表示 Host 级配置。</param>
/// <param name="Name">显示名称。</param>
/// <param name="ProviderKey">提供程序键。</param>
/// <param name="EndpointBaseUrl">端点基址。</param>
/// <param name="ModelId">模型标识。</param>
/// <param name="ApiKey">API 密钥；仅写入时接受，响应不回显。</param>
/// <param name="OrganizationId">OpenAI 组织标识（可选）。</param>
/// <param name="IsDefault">是否为当前作用域默认模型。</param>
/// <param name="IsEnabled">是否启用。</param>
public sealed record CreateAiModelConfigRequest(
    Guid? TenantId,
    string Name,
    string ProviderKey,
    string EndpointBaseUrl,
    string ModelId,
    string? ApiKey,
    string? OrganizationId,
    bool IsDefault,
    bool IsEnabled);

/// <summary>更新 AI 模型配置请求。</summary>
/// <param name="Name">显示名称。</param>
/// <param name="ProviderKey">提供程序键。</param>
/// <param name="EndpointBaseUrl">端点基址。</param>
/// <param name="ModelId">模型标识。</param>
/// <param name="ApiKey">API 密钥；为空表示不修改，非空则覆盖。</param>
/// <param name="ClearApiKey">是否清除已保存的 API 密钥。</param>
/// <param name="OrganizationId">OpenAI 组织标识（可选）。</param>
/// <param name="IsDefault">是否为当前作用域默认模型。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record UpdateAiModelConfigRequest(
    string Name,
    string ProviderKey,
    string EndpointBaseUrl,
    string ModelId,
    string? ApiKey,
    bool ClearApiKey,
    string? OrganizationId,
    bool IsDefault,
    bool IsEnabled,
    int Version);

/// <summary>模型连通性测试结果。</summary>
/// <param name="Succeeded">是否成功。</param>
/// <param name="Message">结果摘要。</param>
public sealed record TestAiModelConfigResult(bool Succeeded, string Message);
