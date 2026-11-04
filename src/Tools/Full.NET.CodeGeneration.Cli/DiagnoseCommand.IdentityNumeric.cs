using System.Text.Json;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    private static void CheckIdentityNumericOptions(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        List<DiagnoseFinding> findings)
    {
        // 这些限制由宿主无条件验证，关闭令牌端点或使用临时签名都不能跳过。
        (string Key, int Minimum, int Maximum)[] limits =
        [
            ("AccessTokenMinutes", 1, 60),
            ("RefreshTokenDays", 1, 90),
            ("LockoutThreshold", 1, 20),
            ("LockoutMinutes", 1, 1440),
            ("LoginRateLimitPermitLimitPerMinute", 1, int.MaxValue),
            ("SessionMutationRateLimitPermitLimitPerMinute", 1, int.MaxValue),
            ("PasswordExpirationDays", 0, int.MaxValue),
        ];
        var invalid = limits.Any(limit =>
        {
            // 缺键保持 Options 默认值；显式 JSON null/空节点绑定为 Int32 的零值。
            if (!TryReadIdentitySigningValue(root, profileSettings, workspacePath, profile,
                    "Identity:" + limit.Key, out var raw)) return false;
            var value = 0;
            return (raw is not null && !TryParseConfigurationInt32(raw, out value))
                || value < limit.Minimum || value > limit.Maximum;
        });
        findings.Add(invalid
            ? DiagnoseFinding.Error("DIAG_IDENTITY_NUMERIC_OPTIONS_INVALID",
                "Identity 数值配置不能绑定为整数或不符合宿主范围要求。",
                "AccessTokenMinutes 为 1–60 分钟，RefreshTokenDays 为 1–90 天，LockoutThreshold 为 1–20 次，LockoutMinutes 为 1–1440 分钟；两个每分钟限流值至少为 1，PasswordExpirationDays 至少为 0。诊断不会输出配置值。")
            : DiagnoseFinding.Ok("DIAG_IDENTITY_NUMERIC_OPTIONS_CONFIGURED",
                "Identity 七项令牌时长、锁定、限流与密码到期数值配置符合宿主范围；未认证完整 Identity Options 或宿主启动。"));
    }
}
