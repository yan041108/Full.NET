namespace Full.NET.Modules.Identity.Security;

internal static class IdentityPasswordPolicy
{
    public const int MinimumLength = 12;

    public static IReadOnlyList<string> Validate(string? password)
    {
        var violations = new List<string>();
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
        {
            violations.Add($"Password must contain at least {MinimumLength} characters.");
        }

        if (string.IsNullOrEmpty(password) || !password.Any(char.IsUpper))
        {
            violations.Add("Password must contain an uppercase letter.");
        }

        if (string.IsNullOrEmpty(password) || !password.Any(char.IsLower))
        {
            violations.Add("Password must contain a lowercase letter.");
        }

        if (string.IsNullOrEmpty(password) || !password.Any(char.IsDigit))
        {
            violations.Add("Password must contain a number.");
        }

        if (string.IsNullOrEmpty(password) || password.All(char.IsLetterOrDigit))
        {
            violations.Add("Password must contain a non-alphanumeric character.");
        }

        return violations;
    }
}
