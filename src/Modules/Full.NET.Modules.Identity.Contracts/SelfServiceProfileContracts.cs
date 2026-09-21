namespace Full.NET.Modules.Identity.Contracts;

/// <summary>当前用户自助档案读取响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="UserId">当前用户标识。</param>
/// <param name="Username">登录名；只读。</param>
/// <param name="DisplayName">展示名称。</param>
/// <param name="AccountType">账号类型机器码；只读。</param>
/// <param name="UserVersion">账号快照并发版本，用于展示名称更新。</param>
/// <param name="ReadableFieldKeys">按角色字段投影裁剪后可读的档案字段键。</param>
/// <param name="WritableFieldKeys">自助场景允许写入的档案字段键；敏感揭示字段与 HR 内部字段已排除。</param>
/// <param name="AvatarFileId">头像文件标识；未设置时为 <see langword="null"/>。</param>
/// <param name="SignatureFileId">签名文件标识；未设置时为 <see langword="null"/>。</param>
/// <param name="Profile">扩展档案；无可读档案字段时为 <see langword="null"/>。</param>
public sealed record SelfServiceProfileResponse(
    Guid UserId,
    string Username,
    string DisplayName,
    string AccountType,
    int UserVersion,
    IReadOnlyList<string> ReadableFieldKeys,
    IReadOnlyList<string> WritableFieldKeys,
    Guid? AvatarFileId,
    Guid? SignatureFileId,
    HostUserProfileResponse? Profile);

/// <summary>当前用户自助档案更新请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="DisplayName">新的展示名称；省略表示不修改。</param>
/// <param name="UserVersion">调用方看到的账号版本；修改展示名称时必填。</param>
/// <param name="Profile">扩展档案补丁；省略表示不修改档案表。</param>
public sealed record UpdateSelfServiceProfileRequest(
    string? DisplayName,
    int? UserVersion,
    HostUserProfileWriteRequest? Profile);
