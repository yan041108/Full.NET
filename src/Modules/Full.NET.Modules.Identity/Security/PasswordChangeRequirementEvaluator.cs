namespace Full.NET.Modules.Identity.Security;

/// <summary>根据账号状态与配置判断当前是否必须完成改密后才能继续访问业务 API。</summary>
internal static class PasswordChangeRequirementEvaluator
{
    /// <summary>
    /// 判定用户是否处于强制改密状态。
    /// </summary>
    /// <param name="mustChangePassword">管理员或首次创建标记。</param>
    /// <param name="passwordChangedAtUtc">最近一次成功改密时间；为空时按到期策略视为需要改密。</param>
    /// <param name="now">当前 UTC 时间。</param>
    /// <param name="passwordExpirationDays">密码到期天数；0 或负数表示不启用到期策略。</param>
    public static bool IsRequired(
        bool mustChangePassword,
        DateTimeOffset? passwordChangedAtUtc,
        DateTimeOffset now,
        int passwordExpirationDays)
    {
        if (mustChangePassword)
        {
            return true;
        }

        if (passwordExpirationDays <= 0)
        {
            return false;
        }

        if (!passwordChangedAtUtc.HasValue)
        {
            return true;
        }

        return passwordChangedAtUtc.Value.AddDays(passwordExpirationDays) <= now;
    }
}
