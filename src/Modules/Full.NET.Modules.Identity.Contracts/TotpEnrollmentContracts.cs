namespace Full.NET.Modules.Identity.Contracts;

/// <summary>开始为本账号登记 TOTP，返回一次性明文密钥与 otpauth URI。</summary>
/// <param name="SharedSecretBase32">仅在本次登记开始时返回的 Base32 明文密钥，调用方不得长期缓存或再次回显。</param>
/// <param name="OtpAuthUri">供认证器应用扫码导入的 otpauth URI。</param>
public sealed record BeginTotpEnrollmentResponse(
    string SharedSecretBase32,
    string OtpAuthUri);

/// <summary>确认 TOTP 登记，提交当前步长验证码以启用。</summary>
/// <param name="TotpCode">当前时间步长内生成的验证码；服务端负责窗口容忍和重放保护。</param>
public sealed record ConfirmTotpEnrollmentRequest(string TotpCode);

/// <summary>当前账号 TOTP 登记状态。</summary>
/// <param name="IsEnrolled">是否已经存在登记记录。</param>
/// <param name="IsEnabled">是否已经完成确认并进入强制校验状态。</param>
public sealed record TotpEnrollmentStatusResponse(
    bool IsEnrolled,
    bool IsEnabled);
