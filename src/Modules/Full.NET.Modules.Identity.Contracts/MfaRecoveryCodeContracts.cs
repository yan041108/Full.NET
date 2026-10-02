namespace Full.NET.Modules.Identity.Contracts;

/// <summary>重新生成 MFA 恢复码结果；恢复码为一次性凭据，仅返回一次。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 RecoveryCodes 仅返回一次，调用方必须立即投递给用户离线保存，禁止落盘或写日志。</remarks>
/// <param name="RecoveryCodes">一次性恢复码集合；每个码只能消费一次，重放将被服务端拒绝。</param>
public sealed record RegenerateMfaRecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

/// <summary>消费 MFA 恢复码请求；用于在丢失主 MFA 因子时凭恢复码完成强认证。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 RecoveryCode 仅用于本次验证，禁止持久化或写日志。</remarks>
/// <param name="RecoveryCode">待消费的一次性恢复码；服务端校验成功后立即标记已使用，重放将被拒绝。</param>
public sealed record ConsumeMfaRecoveryCodeRequest(string RecoveryCode);

/// <summary>消费 MFA 恢复码结果。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 恢复码消费具有一次性语义，已使用恢复码再次提交不会通过校验。</remarks>
/// <param name="Consumed">是否实际消费成功；恢复码不存在、已使用或格式不当时为 <see langword="false"/>。</param>
public sealed record ConsumeMfaRecoveryCodeResponse(bool Consumed);
