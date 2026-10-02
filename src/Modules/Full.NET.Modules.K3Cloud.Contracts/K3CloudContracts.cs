namespace Full.NET.Modules.K3Cloud.Contracts;

/// <summary>K3Cloud 连接配置响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 LastTestStatusKey 为稳定机器码；密码字段不出现在响应中。</remarks>
/// <param name="Id">连接配置标识（UUID v7）。</param>
/// <param name="Name">连接配置展示名。</param>
/// <param name="BaseUrl">K3Cloud 服务基础 URL。</param>
/// <param name="AcctId">K3Cloud 账套标识。</param>
/// <param name="Username">登录用户名。</param>
/// <param name="Lcid">K3Cloud 语言区域标识，决定返回的本地化文本与错误描述。</param>
/// <param name="HasPassword">是否已配置密码；不暴露密码本身。</param>
/// <param name="IsDefault">是否作为本租户缺省连接。</param>
/// <param name="IsEnabled">连接是否启用。</param>
/// <param name="LastTestedAtUtc">最近连接测试时间（UTC）；未测试为 <see langword="null"/>。</param>
/// <param name="LastTestStatusKey">最近测试结果稳定状态键；未测试为 <see langword="null"/>。</param>
/// <param name="LastTestMessage">最近测试附加说明；未测试为 <see langword="null"/>。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）；未更新为 <see langword="null"/>。</param>
/// <param name="Version">乐观并发版本号，用于 CAS 守卫。</param>
public sealed record K3CloudConnectionConfigResponse(
    Guid Id,
    string Name,
    string BaseUrl,
    string AcctId,
    string Username,
    int Lcid,
    bool HasPassword,
    bool IsDefault,
    bool IsEnabled,
    DateTimeOffset? LastTestedAtUtc,
    string? LastTestStatusKey,
    string? LastTestMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>创建 K3Cloud 连接配置请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 Password 仅在此请求中明文传输，不回显。</remarks>
/// <param name="Name">连接配置展示名。</param>
/// <param name="BaseUrl">K3Cloud 服务基础 URL。</param>
/// <param name="AcctId">K3Cloud 账套标识。</param>
/// <param name="Username">登录用户名。</param>
/// <param name="Password">登录密码；服务端将加密持久化，不回显。</param>
/// <param name="Lcid">K3Cloud 语言区域标识。</param>
/// <param name="IsDefault">是否作为本租户缺省连接。</param>
/// <param name="IsEnabled">连接是否启用。</param>
public sealed record CreateK3CloudConnectionConfigRequest(
    string Name,
    string BaseUrl,
    string AcctId,
    string Username,
    string Password,
    int Lcid,
    bool IsDefault,
    bool IsEnabled);

/// <summary>更新 K3Cloud 连接配置请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 Password 为 <see langword="null"/> 时表示保持原值。</remarks>
/// <param name="Name">连接配置展示名。</param>
/// <param name="BaseUrl">K3Cloud 服务基础 URL。</param>
/// <param name="AcctId">K3Cloud 账套标识。</param>
/// <param name="Username">登录用户名。</param>
/// <param name="Password">新密码；为 <see langword="null"/> 时保持原密码。</param>
/// <param name="Lcid">K3Cloud 语言区域标识。</param>
/// <param name="IsDefault">是否作为本租户缺省连接。</param>
/// <param name="IsEnabled">连接是否启用。</param>
/// <param name="Version">期望的当前版本号，不匹配时返回 CAS 冲突错误。</param>
public sealed record UpdateK3CloudConnectionConfigRequest(
    string Name,
    string BaseUrl,
    string AcctId,
    string Username,
    string? Password,
    int Lcid,
    bool IsDefault,
    bool IsEnabled,
    int Version);

/// <summary>K3Cloud 连接测试结果。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Succeeded">连接测试是否成功。</param>
/// <param name="Message">测试结果附加说明；成功时可能为空字符串。</param>
public sealed record TestK3CloudConnectionConfigResult(
    bool Succeeded,
    string Message);

/// <summary>K3Cloud 单据同步响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 StatusKey、LastStepKey、LastErrorCode 为稳定机器码。</remarks>
/// <param name="Id">单据同步记录标识（UUID v7）。</param>
/// <param name="ConnectionConfigId">关联的 K3Cloud 连接配置标识。</param>
/// <param name="DocumentTypeKey">单据类型稳定机器码，决定后续提交路径与步骤。</param>
/// <param name="BusinessKey">业务键，用于跨模块溯源与幂等。</param>
/// <param name="StatusKey">同步状态稳定机器码。</param>
/// <param name="LastStepKey">最近执行步骤稳定键；尚未提交为 <see langword="null"/>。</param>
/// <param name="ExternalBillId">K3Cloud 返回的单据内部标识；未提交为 <see langword="null"/>。</param>
/// <param name="ExternalBillNo">K3Cloud 返回的单据编号；未提交为 <see langword="null"/>。</param>
/// <param name="LastErrorCode">最近失败稳定错误码；无错误为 <see langword="null"/>。</param>
/// <param name="LastErrorMessage">最近失败可读说明；无错误为 <see langword="null"/>。</param>
/// <param name="SubmittedAtUtc">提交到 K3Cloud 的时间（UTC）；未提交为 <see langword="null"/>。</param>
/// <param name="CreatedAtUtc">记录创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）；未更新为 <see langword="null"/>。</param>
/// <param name="CreatedByUserId">发起同步的用户标识。</param>
/// <param name="Version">乐观并发版本号，用于 CAS 守卫。</param>
public sealed record K3CloudDocumentSyncResponse(
    Guid Id,
    Guid ConnectionConfigId,
    string DocumentTypeKey,
    string BusinessKey,
    string StatusKey,
    string? LastStepKey,
    string? ExternalBillId,
    string? ExternalBillNo,
    string? LastErrorCode,
    string? LastErrorMessage,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    Guid CreatedByUserId,
    int Version);

/// <summary>创建 K3Cloud 单据同步请求；仅允许首种固定单据类型。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 DocumentTypeKey 必须为已发布清单内的稳定机器码。</remarks>
/// <param name="ConnectionConfigId">关联的 K3Cloud 连接配置标识。</param>
/// <param name="DocumentTypeKey">单据类型稳定机器码，必须为已发布值。</param>
/// <param name="BusinessKey">业务键，用于跨模块溯源与幂等。</param>
/// <param name="PayloadJson">单据 JSON 载荷，由 K3Cloud 适配层解释。</param>
public sealed record CreateK3CloudDocumentSyncRequest(
    Guid ConnectionConfigId,
    string DocumentTypeKey,
    string BusinessKey,
    string PayloadJson);
