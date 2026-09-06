using Full.NET.Modules.Cryptography.Infrastructure;

namespace Full.NET.UnitTests.Cryptography;

/// <summary>SM2 签名与验签引擎单元测试。</summary>
[TestClass]
public sealed class GmSm2SignatureEngineTests
{
    private const string DefaultUserId = "1234567812345678";

    [TestMethod]
    public void Sign_and_verify_roundtrip_with_generated_key_pair()
    {
        var (privateKeyHex, publicKeyHex, fingerprint) =
            GmSm2SignatureEngine.GenerateKeyPairHex();
        var message = "fullnet-integration-payload"u8.ToArray();
        var userId = System.Text.Encoding.UTF8.GetBytes(DefaultUserId);
        var signatureHex = GmSm2SignatureEngine.SignHex(message, userId, privateKeyHex);
        Assert.IsTrue(GmSm2SignatureEngine.VerifyHex(message, userId, signatureHex, publicKeyHex));
        Assert.AreEqual(64, fingerprint.Length);
    }

    [TestMethod]
    public void Development_seed_key_material_is_stable()
    {
        var first = GmSm2SignatureEngine.GetDevelopmentSeedKeyMaterial();
        var second = GmSm2SignatureEngine.GetDevelopmentSeedKeyMaterial();
        Assert.AreEqual(first.PrivateKeyHex, second.PrivateKeyHex);
        Assert.AreEqual(first.PublicKeyHex, second.PublicKeyHex);
        Assert.AreEqual(first.Fingerprint, second.Fingerprint);
        var message = "fullnet-dev-seed"u8.ToArray();
        var userId = System.Text.Encoding.UTF8.GetBytes(DefaultUserId);
        var signatureHex = GmSm2SignatureEngine.SignHex(message, userId, first.PrivateKeyHex);
        Assert.IsTrue(GmSm2SignatureEngine.VerifyHex(message, userId, signatureHex, first.PublicKeyHex));
    }
}
