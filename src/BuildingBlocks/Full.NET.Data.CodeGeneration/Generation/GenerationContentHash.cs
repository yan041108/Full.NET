using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 提供与平台和当前文化无关的生成文本内容摘要。
/// </summary>
internal static class GenerationContentHash
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string Compute(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return Convert.ToHexString(
                SHA256.HashData(StrictUtf8.GetBytes(content)))
            .ToLowerInvariant();
    }

    public static bool IsValid(string? sha256)
    {
        return sha256 is { Length: 64 }
            && sha256.All(character =>
                character is >= '0' and <= '9'
                or >= 'a' and <= 'f');
    }
}
