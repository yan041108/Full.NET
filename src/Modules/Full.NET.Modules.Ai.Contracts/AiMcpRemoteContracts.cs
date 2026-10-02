namespace Full.NET.Modules.Ai.Contracts;

/// <summary>MCP 远端连接列表项；端点与令牌已脱敏。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。ConnectionKey 为稳定机器码，发布后不可改名或删除。</remarks>
/// <param name="Id">连接标识。</param>
/// <param name="ConnectionKey">稳定机器码；发布后不可改名或删除。</param>
/// <param name="DisplayName">展示名称。</param>
/// <param name="MaskedEndpointUrl">已脱敏的 MCP 端点 URL，仅用于列表回显。</param>
/// <param name="HasServiceToken">是否已配置服务令牌；不回显令牌明文。</param>
/// <param name="IsEnabled">连接是否启用。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record AiMcpRemoteConnectionListItem(
    Guid Id,
    string ConnectionKey,
    string DisplayName,
    string MaskedEndpointUrl,
    bool HasServiceToken,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);

/// <summary>MCP 远端连接详情；不回显服务令牌。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">连接标识。</param>
/// <param name="ConnectionKey">稳定机器码；发布后不可改名或删除。</param>
/// <param name="DisplayName">展示名称。</param>
/// <param name="EndpointUrl">MCP 端点 URL；仅在详情接口返回。</param>
/// <param name="HasServiceToken">是否已配置服务令牌；不回显令牌明文。</param>
/// <param name="OAuthScopesJson">OAuth 作用域 JSON；可为空。</param>
/// <param name="IsEnabled">连接是否启用。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record AiMcpRemoteConnectionResponse(
    Guid Id,
    string ConnectionKey,
    string DisplayName,
    string EndpointUrl,
    bool HasServiceToken,
    string? OAuthScopesJson,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);

/// <summary>创建 MCP 远端连接请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。ConnectionKey 创建后不可变。</remarks>
/// <param name="ConnectionKey">稳定机器码；发布后不可改名或删除。</param>
/// <param name="DisplayName">展示名称。</param>
/// <param name="EndpointUrl">MCP 端点 URL。</param>
/// <param name="ServiceToken">调用远端 MCP 服务所需的服务令牌；仅写入不回显。</param>
/// <param name="OAuthScopesJson">OAuth 作用域 JSON；可为空。</param>
public sealed record CreateAiMcpRemoteConnectionRequest(
    string ConnectionKey,
    string DisplayName,
    string EndpointUrl,
    string ServiceToken,
    string? OAuthScopesJson);

/// <summary>更新 MCP 远端连接请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。ServiceToken 与 ClearServiceToken 互斥：清空时仅传 ClearServiceToken=true。</remarks>
/// <param name="DisplayName">展示名称。</param>
/// <param name="EndpointUrl">MCP 端点 URL。</param>
/// <param name="ServiceToken">新的服务令牌；保持原值时传 null。</param>
/// <param name="ClearServiceToken">是否清空已配置的服务令牌。</param>
/// <param name="OAuthScopesJson">OAuth 作用域 JSON；可为空。</param>
/// <param name="IsEnabled">连接是否启用。</param>
/// <param name="Version">乐观锁版本号，用于 CAS 并发控制。</param>
public sealed record UpdateAiMcpRemoteConnectionRequest(
    string DisplayName,
    string EndpointUrl,
    string? ServiceToken,
    bool ClearServiceToken,
    string? OAuthScopesJson,
    bool IsEnabled,
    long Version);

/// <summary>远端 MCP 工具发现项。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="RemoteToolName">远端工具名称；与远端 MCP 工具名一致。</param>
/// <param name="InputSchemaJson">工具输入 Schema JSON，用于本地校验。</param>
/// <param name="IsApproved">是否已被本地批准调用。</param>
/// <param name="ApprovalStatusKey">批准状态稳定机器码；可为空表示尚未提交批准。</param>
public sealed record AiMcpRemoteDiscoveredToolItem(
    string RemoteToolName,
    string InputSchemaJson,
    bool IsApproved,
    string? ApprovalStatusKey);

/// <summary>批准远端 MCP 工具请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。PermissionCode 为稳定权限码字符串，发布后不可改名。</remarks>
/// <param name="RemoteToolName">远端工具名称，定位待批准工具。</param>
/// <param name="SideEffectKey">副作用分类稳定机器码；决定调用是否需特殊审计。</param>
/// <param name="PermissionCode">调用此工具所需的精确权限码。</param>
public sealed record ApproveAiMcpRemoteToolRequest(
    string RemoteToolName,
    string SideEffectKey,
    string PermissionCode);

/// <summary>已登记的远端工具批准项。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。LocalToolName、RemoteToolName、SideEffectKey、PermissionCode、ApprovalStatusKey 均为稳定机器码，发布后不可改名或删除。</remarks>
/// <param name="Id">批准项标识。</param>
/// <param name="LocalToolName">本地登记的工具名称。</param>
/// <param name="RemoteToolName">远端 MCP 工具名称。</param>
/// <param name="ToolVersion">远端工具版本号。</param>
/// <param name="SideEffectKey">副作用分类稳定机器码。</param>
/// <param name="PermissionCode">调用此工具所需的精确权限码。</param>
/// <param name="ApprovalStatusKey">批准状态稳定机器码。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record AiMcpRemoteToolApprovalItem(
    Guid Id,
    string LocalToolName,
    string RemoteToolName,
    int ToolVersion,
    string SideEffectKey,
    string PermissionCode,
    string ApprovalStatusKey,
    long Version);
