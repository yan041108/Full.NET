using System.Globalization;
using System.Net.Mail;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.ManageHostUsers;

/// <summary>
/// 规范化并校验参与 Host 目录唯一性判断的用户资料，确保应用预检与数据库索引使用同一语义。
/// </summary>
internal static class HostUserProfilePolicy
{
    private static readonly HashSet<string> SupportedIdCardTypes = new(StringComparer.Ordinal)
    {
        "id_card",
        "passport",
        "hk_macau_pass",
        "taiwan_pass",
        "military_id",
        "other",
    };

    private static readonly int[] MainlandIdCardWeights =
        [7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2];

    private const string MainlandIdCardChecks = "10X98765432";

    public static Result<HostUserProfileWriteRequest> NormalizeAndValidate(
        HostUserProfileWriteRequest profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var normalized = profile with
        {
            PhoneNumber = Normalize(profile.PhoneNumber),
            Email = Normalize(profile.Email)?.ToLowerInvariant(),
            EmployeeNumber = Normalize(profile.EmployeeNumber)?.ToUpperInvariant(),
            IdCardType = Normalize(profile.IdCardType)?.ToLowerInvariant(),
            IdCardNumber = Normalize(profile.IdCardNumber)?.ToUpperInvariant(),
        };

        var violations = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (normalized.PhoneNumber is { } phoneNumber && !IsCanonicalPhoneNumber(phoneNumber))
        {
            violations["phoneNumber"] = ["Phone number must use canonical E.164 shape."];
        }

        if (normalized.Email is { } email && !IsValidEmail(email))
        {
            violations["email"] = ["Email address is invalid."];
        }

        if (normalized.EmployeeNumber is { } employeeNumber
            && !IsComparableIdentifier(employeeNumber, minimumLength: 1))
        {
            violations["employeeNumber"] = ["Employee number is invalid."];
        }

        ValidateIdCard(normalized, violations);
        if (violations.Count > 0)
        {
            return Result<HostUserProfileWriteRequest>.Failure(new Error(
                IdentityErrorCodes.UserProfileInvalid,
                "Host user profile contains invalid authoritative fields.",
                ErrorType.Validation,
                violations));
        }

        return Result<HostUserProfileWriteRequest>.Success(normalized);
    }

    private static void ValidateIdCard(
        HostUserProfileWriteRequest profile,
        IDictionary<string, string[]> violations)
    {
        if (profile.IdCardType is null && profile.IdCardNumber is null)
        {
            return;
        }

        if (profile.IdCardType is null)
        {
            violations["idCardType"] = ["Identity document type is required with its number."];
            return;
        }

        if (profile.IdCardNumber is null)
        {
            violations["idCardNumber"] = ["Identity document number is required with its type."];
            return;
        }

        if (!SupportedIdCardTypes.Contains(profile.IdCardType))
        {
            violations["idCardType"] = ["Identity document type is not supported."];
            return;
        }

        var isValid = string.Equals(profile.IdCardType, "id_card", StringComparison.Ordinal)
            ? IsValidMainlandIdCard(profile.IdCardNumber)
            : IsComparableIdentifier(profile.IdCardNumber, minimumLength: 3);
        if (!isValid)
        {
            violations["idCardNumber"] = ["Identity document number is invalid."];
        }
    }

    private static bool IsCanonicalPhoneNumber(string value)
    {
        var digitStart = value[0] == '+' ? 1 : 0;
        var digitCount = value.Length - digitStart;
        if (digitCount is < 8 or > 15 || value[digitStart] == '0')
        {
            return false;
        }

        for (var index = digitStart; index < value.Length; index++)
        {
            if (value[index] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidEmail(string value)
    {
        if (value.Length > 256
            || value.Any(char.IsWhiteSpace)
            || !MailAddress.TryCreate(value, out var address)
            || !string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separator = value.LastIndexOf('@');
        return separator > 0
            && separator < value.Length - 1
            && Uri.CheckHostName(value[(separator + 1)..]) != UriHostNameType.Unknown;
    }

    private static bool IsComparableIdentifier(string value, int minimumLength)
    {
        if (value.Length < minimumLength || value.Length > 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is >= 'A' and <= 'Z'
                || character is >= '0' and <= '9'
                || character is '.' or '_' or '-' or '/')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsValidMainlandIdCard(string value)
    {
        if (value.Length != 18)
        {
            return false;
        }

        var sum = 0;
        for (var index = 0; index < MainlandIdCardWeights.Length; index++)
        {
            var character = value[index];
            if (character is < '0' or > '9')
            {
                return false;
            }

            sum += (character - '0') * MainlandIdCardWeights[index];
        }

        if (value[17] != MainlandIdCardChecks[sum % 11])
        {
            return false;
        }

        return DateOnly.TryParseExact(
            value.AsSpan(6, 8),
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
