using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Payments.Domain;

namespace Full.NET.UnitTests.Payments;

[TestClass]
public sealed class WeChatPayResourceDecryptorTests
{
    [TestMethod]
    public void Decrypt_round_trips_wechat_style_aes_gcm_payload()
    {
        const string apiV3Key = "01234567890123456789012345678901";
        const string associatedData = "transaction";
        const string nonce = "593BEC0C930B";
        const string plainText = """{"out_trade_no":"FN2026010100000001","trade_state":"SUCCESS"}""";

        var nonceBytes = Encoding.UTF8.GetBytes(nonce);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var associatedBytes = Encoding.UTF8.GetBytes(associatedData);
        var keyBytes = Encoding.UTF8.GetBytes(apiV3Key);
        var ciphertext = new byte[plainBytes.Length];
        var tag = new byte[16];
        using var aesGcm = new AesGcm(keyBytes, tag.Length);
        aesGcm.Encrypt(nonceBytes, plainBytes, ciphertext, tag, associatedBytes);
        var cipherWithTag = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, cipherWithTag, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, cipherWithTag, ciphertext.Length, tag.Length);
        var ciphertextBase64 = Convert.ToBase64String(cipherWithTag);

        var decrypted = WeChatPayResourceDecryptor.Decrypt(
            apiV3Key,
            associatedData,
            nonce,
            ciphertextBase64);

        Assert.AreEqual(plainText, decrypted);
    }
}
