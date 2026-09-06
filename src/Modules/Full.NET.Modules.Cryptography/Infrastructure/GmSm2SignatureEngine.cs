using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.GM;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.Utilities.Encoders;

namespace Full.NET.Modules.Cryptography.Infrastructure;

/// <summary>SM2/SM3 国密签名与验签引擎；仅暴露签名语义，不提供通用加解密。</summary>
internal static class GmSm2SignatureEngine
{
    private const int RsLength = 32;
    private static readonly Org.BouncyCastle.Asn1.X9.X9ECParameters CurveParameters =
        GMNamedCurves.GetByName("sm2p256v1");
    private static readonly ECDomainParameters DomainParameters = new(
        CurveParameters.Curve,
        CurveParameters.G,
        CurveParameters.N);

    /// <summary>生成 SM2 密钥材料，仅供测试或部署初始化脚本使用。</summary>
    /// <returns>私钥、公钥与指纹三元组。</returns>
    internal static (string PrivateKeyHex, string PublicKeyHex, string Fingerprint) GenerateKeyPairHex()
    {
        var generator = new ECKeyPairGenerator();
        generator.Init(new ECKeyGenerationParameters(DomainParameters, new SecureRandom()));
        var keyPair = generator.GenerateKeyPair();
        return ToKeyMaterial(keyPair);
    }

    /// <summary>返回固定的开发种子密钥，供迁移种子与本地示例配置使用。</summary>
    /// <returns>私钥、公钥与指纹三元组。</returns>
    internal static (string PrivateKeyHex, string PublicKeyHex, string Fingerprint) GetDevelopmentSeedKeyMaterial()
    {
        const string privateKeyHex =
            "74145a99093fa61037ff1e8dd6e1c4820c4d1ffa8666212c75e17a7924e3b5bf";
        var privateKey = CreatePrivateKey(privateKeyHex);
        var publicPoint = DomainParameters.G.Multiply(privateKey.D).Normalize();
        var publicKeyHex = Hex.ToHexString(publicPoint.GetEncoded(false)[1..]).ToLowerInvariant();
        return (privateKeyHex, publicKeyHex, ComputePublicKeyFingerprint(publicKeyHex));
    }

    private static (string PrivateKeyHex, string PublicKeyHex, string Fingerprint) ToKeyMaterial(
        AsymmetricCipherKeyPair keyPair)
    {
        var privateKeyHex = ((ECPrivateKeyParameters)keyPair.Private).D.ToString(16).ToLowerInvariant();
        var encoded = ((ECPublicKeyParameters)keyPair.Public).Q.GetEncoded(false);
        var publicKeyHex = Hex.ToHexString(encoded[1..]).ToLowerInvariant();
        return (privateKeyHex, publicKeyHex, ComputePublicKeyFingerprint(publicKeyHex));
    }

    /// <summary>计算公钥 SM3 指纹（十六进制小写）。</summary>
    /// <param name="publicKeyHex">128 位十六进制公钥坐标（X||Y）。</param>
    /// <returns>指纹十六进制字符串。</returns>
    public static string ComputePublicKeyFingerprint(string publicKeyHex)
    {
        var normalized = NormalizePublicKeyHex(publicKeyHex);
        var digest = new SM3Digest();
        var bytes = Hex.DecodeStrict(normalized);
        digest.BlockUpdate(bytes, 0, bytes.Length);
        var output = new byte[digest.GetDigestSize()];
        digest.DoFinal(output, 0);
        return Hex.ToHexString(output).ToLowerInvariant();
    }

    /// <summary>使用 SM3withSM2 对消息签名，返回 r||s 拼接十六进制。</summary>
    /// <param name="message">待签名消息字节。</param>
    /// <param name="userId">SM2 用户标识字节。</param>
    /// <param name="privateKeyHex">私钥十六进制。</param>
    /// <returns>签名十六进制（r||s）。</returns>
    public static string SignHex(byte[] message, byte[] userId, string privateKeyHex)
    {
        var privateKey = CreatePrivateKey(privateKeyHex);
        var signer = SignerUtilities.GetSigner("SM3withSM2");
        signer.Init(true, new ParametersWithID(privateKey, userId));
        signer.BlockUpdate(message, 0, message.Length);
        var signature = RsAsn1ToPlainByteArray(signer.GenerateSignature());
        return Hex.ToHexString(signature).ToLowerInvariant();
    }

    /// <summary>验证 SM3withSM2 签名。</summary>
    /// <param name="message">原始消息字节。</param>
    /// <param name="userId">SM2 用户标识字节。</param>
    /// <param name="signatureHex">r||s 签名十六进制。</param>
    /// <param name="publicKeyHex">128 位公钥坐标十六进制。</param>
    /// <returns>验签是否通过。</returns>
    public static bool VerifyHex(
        byte[] message,
        byte[] userId,
        string signatureHex,
        string publicKeyHex)
    {
        var signature = Hex.DecodeStrict(signatureHex);
        if (signature.Length != RsLength * 2)
        {
            return false;
        }

        var publicKey = CreatePublicKey(publicKeyHex);
        var signer = SignerUtilities.GetSigner("SM3withSM2");
        signer.Init(false, new ParametersWithID(publicKey, userId));
        signer.BlockUpdate(message, 0, message.Length);
        return signer.VerifySignature(RsPlainByteArrayToAsn1(signature));
    }

    internal static string NormalizePublicKeyHex(string publicKeyHex)
    {
        var normalized = publicKeyHex.Trim();
        if (normalized.StartsWith("04", StringComparison.OrdinalIgnoreCase)
            && normalized.Length == 130)
        {
            normalized = normalized[2..];
        }

        if (normalized.Length != 128)
        {
            throw new ArgumentException("SM2 public key hex must be 128 characters.", nameof(publicKeyHex));
        }

        return normalized.ToLowerInvariant();
    }

    private static ECPrivateKeyParameters CreatePrivateKey(string privateKeyHex) =>
        new(new BigInteger(privateKeyHex.Trim(), 16), DomainParameters);

    private static ECPublicKeyParameters CreatePublicKey(string publicKeyHex)
    {
        var normalized = NormalizePublicKeyHex(publicKeyHex);
        var x = new BigInteger(normalized[..64], 16);
        var y = new BigInteger(normalized[64..], 16);
        return new ECPublicKeyParameters(
            CurveParameters.Curve.CreatePoint(x, y),
            DomainParameters);
    }

    private static byte[] RsAsn1ToPlainByteArray(byte[] rsDer)
    {
        var sequence = Asn1Sequence.GetInstance(rsDer);
        var r = BigIntToFixedLengthBytes(DerInteger.GetInstance(sequence[0]).Value);
        var s = BigIntToFixedLengthBytes(DerInteger.GetInstance(sequence[1]).Value);
        var result = new byte[RsLength * 2];
        Buffer.BlockCopy(r, 0, result, 0, r.Length);
        Buffer.BlockCopy(s, 0, result, RsLength, s.Length);
        return result;
    }

    private static byte[] RsPlainByteArrayToAsn1(byte[] sign)
    {
        if (sign.Length != RsLength * 2)
        {
            throw new ArgumentException("Invalid SM2 signature length.", nameof(sign));
        }

        var r = new BigInteger(1, Arrays.CopyOfRange(sign, 0, RsLength));
        var s = new BigInteger(1, Arrays.CopyOfRange(sign, RsLength, RsLength * 2));
        return new DerSequence(new DerInteger(r), new DerInteger(s)).GetEncoded("DER");
    }

    private static byte[] BigIntToFixedLengthBytes(BigInteger value)
    {
        var bytes = value.ToByteArray();
        if (bytes.Length == RsLength)
        {
            return bytes;
        }

        if (bytes.Length == RsLength + 1 && bytes[0] == 0)
        {
            return Arrays.CopyOfRange(bytes, 1, RsLength + 1);
        }

        if (bytes.Length < RsLength)
        {
            var result = new byte[RsLength];
            Buffer.BlockCopy(bytes, 0, result, RsLength - bytes.Length, bytes.Length);
            return result;
        }

        throw new ArgumentException("Invalid SM2 signature component length.");
    }
}
