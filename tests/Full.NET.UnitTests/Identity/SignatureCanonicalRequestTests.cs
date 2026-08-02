using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Http;

namespace Full.NET.UnitTests.Identity;

/// <summary>签名规范化与 HMAC 计算的 RED/行为测试。</summary>
[TestClass]
public sealed class SignatureCanonicalRequestTests
{
    [TestMethod]
    public void Unix_timestamp_parser_rejects_values_outside_DateTimeOffset_range()
    {
        Assert.IsFalse(SignatureCanonicalRequest.TryParseUnixTimestamp(
            long.MaxValue.ToString(),
            out _));
        Assert.IsTrue(SignatureCanonicalRequest.TryParseUnixTimestamp(
            "0",
            out var epoch));
        Assert.AreEqual(DateTimeOffset.UnixEpoch, epoch);
    }

    private static readonly byte[] SampleBody = "payload"u8.ToArray();

    [TestMethod]
    public void BuildCanonicalQuery_sorts_keys_and_values_independently_of_request_order()
    {
        var first = SignatureCanonicalRequest.BuildCanonicalQuery(
            default(QueryString).Add("z", "2").Add("a", "1").Add("a", "3"));
        var second = SignatureCanonicalRequest.BuildCanonicalQuery(
            default(QueryString).Add("a", "1").Add("a", "3").Add("z", "2"));

        Assert.AreEqual("a=1&a=3&z=2", first);
        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void BuildCanonicalQuery_rejects_non_canonical_percent_encoding()
    {
        Assert.ThrowsExactly<SignatureCanonicalizationException>(() =>
            SignatureCanonicalRequest.BuildCanonicalQuery(
                new QueryString("?name=a%2bb")));
    }

    [TestMethod]
    public void ComputeContentHash_empty_body_matches_sha256_of_empty_byte_array()
    {
        Assert.AreEqual(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            SignatureCanonicalRequest.ComputeContentHash([]));
    }

    [TestMethod]
    public void ComputeContentHash_non_empty_body_is_stable()
    {
        var hash = SignatureCanonicalRequest.ComputeContentHash(SampleBody);
        Assert.AreEqual(64, hash.Length);
        Assert.AreNotEqual(
            SignatureCanonicalRequest.ComputeContentHash([]),
            hash);
    }

    [TestMethod]
    public void ComputeSignature_changes_when_body_digest_changes()
    {
        const string secret = "fnk_test-secret-value-0123456789abcd";
        var signingKey = SignatureCanonicalRequest.ParseSigningKeyBytes(
            TokenHash.Compute(secret));
        var canonical = SignatureCanonicalRequest.BuildCanonicalString(
            "GET",
            "/api/v1/identity/users",
            "page=1&pageSize=1",
            SignatureCanonicalRequest.ComputeContentHash(SampleBody),
            "fnk_testprefix01",
            "1700000000",
            "nonce-abcdefghij");
        var tampered = SignatureCanonicalRequest.BuildCanonicalString(
            "GET",
            "/api/v1/identity/users",
            "page=1&pageSize=1",
            SignatureCanonicalRequest.ComputeContentHash("tampered"u8.ToArray()),
            "fnk_testprefix01",
            "1700000000",
            "nonce-abcdefghij");

        var original = SignatureCanonicalRequest.ComputeSignature(canonical, signingKey);
        var modified = SignatureCanonicalRequest.ComputeSignature(tampered, signingKey);

        Assert.IsFalse(
            SignatureCanonicalRequest.FixedTimeEqualsSignatures(original, modified));
    }

    [TestMethod]
    public void FixedTimeEqualsSignatures_uses_constant_time_semantics_for_equal_values()
    {
        const string signature =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        Assert.IsTrue(
            SignatureCanonicalRequest.FixedTimeEqualsSignatures(signature, signature));
        Assert.IsFalse(
            SignatureCanonicalRequest.FixedTimeEqualsSignatures(
                signature,
                "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"));
    }

    [TestMethod]
    public void NormalizePath_rejects_trailing_slash_outside_root()
    {
        Assert.ThrowsExactly<SignatureCanonicalizationException>(() =>
            SignatureCanonicalRequest.NormalizePath(
                PathString.Empty,
                new PathString("/api/v1/users/")));
    }
}
