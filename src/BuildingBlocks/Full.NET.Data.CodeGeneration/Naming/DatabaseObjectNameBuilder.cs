using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Data.CodeGeneration.Naming;

/// <summary>
/// 为索引和约束生成跨 SQL Server/MySQL 稳定的数据库对象名。
/// </summary>
public static class DatabaseObjectNameBuilder
{
    private static readonly NamingProfile Profile = NamingProfile.LoadDefault();
    private static readonly HashSet<string> AllowedPrefixes =
        new(["PK", "FK", "UX", "IX", "CK", "DF"], StringComparer.Ordinal);

    /// <summary>
    /// 保留短名称；长名称使用完整输入的 SHA-256 固定前缀摘要压缩。
    /// </summary>
    /// <remarks>
    /// 此方法只适用于索引和约束。表名与列名超长时必须重新设计，禁止静默截断。
    /// </remarks>
    /// <param name="fullName">包含 PK/FK/UX/IX/CK/DF 前缀的完整对象名。</param>
    /// <returns>不超过共同长度上限的确定性名称。</returns>
    public static string Build(string fullName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        if (fullName.Any(character => character > 127)
            || fullName.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character != '_'))
        {
            throw new ArgumentException(
                "数据库对象名只能包含 ASCII 字母、数字和下划线。",
                nameof(fullName));
        }

        var separator = fullName.IndexOf('_', StringComparison.Ordinal);
        if (separator <= 0
            || !AllowedPrefixes.Contains(fullName[..separator]))
        {
            throw new ArgumentException("数据库对象名使用了未知对象前缀。", nameof(fullName));
        }

        if (fullName.Length <= Profile.Database.MaxIdentifierLength)
        {
            return fullName;
        }

        if (!string.Equals(
            Profile.Database.ConstraintDigest.Algorithm,
            "sha256",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("仅支持 SHA-256 数据库对象名摘要。");
        }

        var digest = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(fullName)));
        return string.Concat(
            fullName.AsSpan(0, Profile.Database.ConstraintDigest.PrefixLength),
            Profile.Database.ConstraintDigest.Separator,
            digest.AsSpan(0, Profile.Database.ConstraintDigest.HexLength));
    }
}
