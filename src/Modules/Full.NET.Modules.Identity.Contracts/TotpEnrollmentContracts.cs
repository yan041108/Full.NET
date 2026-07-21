namespace Full.NET.Modules.Identity.Contracts;

/// <summary>开始为本账号登记 TOTP，返回一次性明文密钥与 otpauth URI。</summary>
public sealed record BeginTotpEnrollmentResponse(
    string SharedSecretBase32,
    string OtpAuthUri);

/// <summary>确认 TOTP 登记，提交当前步长验证码以启用。</summary>
public sealed record ConfirmTotpEnrollmentRequest(string TotpCode);

/// <summary>当前账号 TOTP 登记状态。</summary>
public sealed record TotpEnrollmentStatusResponse(
    bool IsEnrolled,
    bool IsEnabled);
