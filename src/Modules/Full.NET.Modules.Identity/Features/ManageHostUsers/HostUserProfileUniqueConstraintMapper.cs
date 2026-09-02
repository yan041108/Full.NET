using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.ManageHostUsers;

/// <summary>
/// 将 Host 用户资料唯一索引冲突映射为稳定领域冲突错误；用于并发写入竞态时
/// <see cref="HostUserManagementService"/> 无法在同事务窗口内读到冲突行的情况。
/// </summary>
internal static class HostUserProfileUniqueConstraintMapper
{
    private const string EmailIndex = "UX_fn_identity_user_profile_Email";
    private const string PhoneIndex = "UX_fn_identity_user_profile_PhoneNumber";
    private const string EmployeeIndex = "UX_fn_identity_user_profile_EmployeeNumber";
    private const string IdCardIndex = "UX_fn_identity_user_profile_IdCardType_IdCardNumber";

    /// <summary>
    /// 尝试从唯一约束异常推断资料字段冲突；无法识别时返回 <see langword="null"/>。
    /// </summary>
    /// <param name="exception">已分类为唯一约束的数据命令异常。</param>
    /// <param name="profile">本次写入合并后的规范化资料快照。</param>
    /// <returns>可安全返回给客户端的冲突错误；无法映射时为 <see langword="null"/>。</returns>
    public static Error? TryMapConflict(
        DataCommandException exception,
        HostUserProfileWriteRequest profile)
    {
        var diagnosticMessage = CollectDiagnosticMessage(exception);
        if (diagnosticMessage.Contains(EmailIndex, StringComparison.OrdinalIgnoreCase)
            && profile.Email is not null)
        {
            return ProfileConflict(
                IdentityErrorCodes.UserEmailExists,
                "Email is already assigned to another host user.");
        }

        if (diagnosticMessage.Contains(PhoneIndex, StringComparison.OrdinalIgnoreCase)
            && profile.PhoneNumber is not null)
        {
            return ProfileConflict(
                IdentityErrorCodes.UserPhoneNumberExists,
                "Phone number is already assigned to another host user.");
        }

        if (diagnosticMessage.Contains(EmployeeIndex, StringComparison.OrdinalIgnoreCase)
            && profile.EmployeeNumber is not null)
        {
            return ProfileConflict(
                IdentityErrorCodes.UserEmployeeNumberExists,
                "Employee number is already assigned to another host user.");
        }

        if (diagnosticMessage.Contains(IdCardIndex, StringComparison.OrdinalIgnoreCase)
            && profile.IdCardType is not null
            && profile.IdCardNumber is not null)
        {
            return ProfileConflict(
                IdentityErrorCodes.UserIdCardExists,
                "Identity document is already assigned to another host user.");
        }

        return TryMapSingleFieldFallback(profile);
    }

    /// <summary>
    /// 当数据库诊断信息缺失索引名时，仅在本轮写入恰好涉及一个唯一字段时回退映射。
    /// </summary>
    private static Error? TryMapSingleFieldFallback(HostUserProfileWriteRequest profile)
    {
        var candidateCount = 0;
        Error? candidate = null;

        if (profile.Email is not null)
        {
            candidateCount++;
            candidate = ProfileConflict(
                IdentityErrorCodes.UserEmailExists,
                "Email is already assigned to another host user.");
        }

        if (profile.PhoneNumber is not null)
        {
            candidateCount++;
            candidate = ProfileConflict(
                IdentityErrorCodes.UserPhoneNumberExists,
                "Phone number is already assigned to another host user.");
        }

        if (profile.EmployeeNumber is not null)
        {
            candidateCount++;
            candidate = ProfileConflict(
                IdentityErrorCodes.UserEmployeeNumberExists,
                "Employee number is already assigned to another host user.");
        }

        if (profile.IdCardType is not null && profile.IdCardNumber is not null)
        {
            candidateCount++;
            candidate = ProfileConflict(
                IdentityErrorCodes.UserIdCardExists,
                "Identity document is already assigned to another host user.");
        }

        return candidateCount == 1 ? candidate : null;
    }

    private static string CollectDiagnosticMessage(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                parts.Add(current.Message);
            }
        }

        return string.Join(' ', parts);
    }

    private static Error ProfileConflict(string code, string message) =>
        new(code, message, ErrorType.Conflict);
}
