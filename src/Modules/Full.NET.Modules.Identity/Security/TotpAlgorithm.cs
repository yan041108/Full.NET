using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Identity.Security;

/// <summary>
/// RFC 6238 TOTP（HMAC-SHA1、30 秒步长、6 位）与 Base32 编解码；不依赖第三方 OTP 包。
/// </summary>
internal static class TotpAlgorithm
{
    public const int StepSeconds = 30;

    public const int Digits = 6;

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>生成 20 字节随机共享密钥并编码为 Base32。</summary>
    public static string GenerateSharedSecretBase32()
    {
        Span<byte> bytes = stackalloc byte[20];
        RandomNumberGenerator.Fill(bytes);
        return EncodeBase32(bytes);
    }

    public static byte[] DecodeSharedSecret(string base32)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base32);
        var normalized = base32.Trim().TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(normalized.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var ch in normalized)
        {
            var value = Base32Alphabet.IndexOf(ch);
            if (value < 0)
            {
                throw new FormatException("TOTP shared secret is not valid Base32.");
            }

            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }

        return output.ToArray();
    }

    public static string ComputeCode(byte[] key, DateTimeOffset utcNow)
    {
        var timestep = utcNow.ToUnixTimeSeconds() / StepSeconds;
        return ComputeCode(key, timestep);
    }

    public static string ComputeCode(byte[] key, long timestep)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, timestep);
        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.HashData(key, counter, hash);
        var offset = hash[^1] & 0x0F;
        var binary =
            ((hash[offset] & 0x7F) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];
        var otp = binary % (int)Math.Pow(10, Digits);
        return otp.ToString($"D{Digits}", CultureInfo.InvariantCulture);
    }

    /// <summary>在当前步长及 ±window 步长内校验验证码。</summary>
    public static bool Verify(
        byte[] key,
        string code,
        DateTimeOffset utcNow,
        int window = 1)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != Digits)
        {
            return false;
        }

        var timestep = utcNow.ToUnixTimeSeconds() / StepSeconds;
        for (var delta = -window; delta <= window; delta++)
        {
            var expected = ComputeCode(key, timestep + delta);
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(expected),
                    Encoding.ASCII.GetBytes(code.Trim())))
            {
                return true;
            }
        }

        return false;
    }

    public static string BuildOtpAuthUri(
        string issuer,
        string accountName,
        string sharedSecretBase32)
    {
        var label = Uri.EscapeDataString($"{issuer}:{accountName}");
        var issuerParam = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={sharedSecretBase32}&issuer={issuerParam}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }

    private static string EncodeBase32(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder((data.Length + 4) / 5 * 8);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                builder.Append(Base32Alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }

        if (bitsLeft > 0)
        {
            builder.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        }

        return builder.ToString();
    }
}
