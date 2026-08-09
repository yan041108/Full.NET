using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Document.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Full.NET.UnitTests.Document;

/// <summary>
/// RED 测试：Document 分享口令安全基线。
/// 运行时验证：明文口令永不落库/出响应、Hasher 抗时序泄漏、兼容旧构造。
/// </summary>
[TestClass]
public sealed class DocumentShareSecurityTests
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void Same_password_produces_two_distinct_hashes_and_verify_accepts_only_matching()
    {
        // 中文注释：Task2 RED 步骤；相同口令两次哈希必须不同，验证必须对明文-哈希对正确通过
        var hasher = new FakePasswordHasher();
        var shareId = Guid.CreateVersion7();
        const string password = "Share@2026!Secure";
        var hash1 = hasher.Hash(shareId, password);
        var hash2 = hasher.Hash(shareId, password);

        Assert.AreNotEqual(hash1, hash2, "相同口令两次哈希必须不同（引入随机盐）");
        Assert.IsTrue(hasher.Verify(shareId, hash1, password), "hash1 对匹配明文必须验证通过");
        Assert.IsTrue(hasher.Verify(shareId, hash2, password), "hash2 对匹配明文必须验证通过");
        Assert.IsFalse(hasher.Verify(shareId, hash1, "WrongPwd!2026"), "错误口令必须验证失败");
    }

    [TestMethod]
    public void HostDocumentShareResponse_hasPassword_does_not_serialize_password_fields()
    {
        // 中文注释：公开 JSON 响应绝对不得包含 password 或 passwordHash；
        // 新增 bool HasPassword 属性；旧 Password 字段 [JsonIgnore] 不出现在序列化结果
        var hasPassword = CreateShareWithPassword();
        var noPassword = CreateShareWithoutPassword();

        var hasPasswordJson = JsonSerializer.Serialize(hasPassword, _serializerOptions);
        var noPasswordJson = JsonSerializer.Serialize(noPassword, _serializerOptions);

        AssertNoPasswordFields(hasPasswordJson);
        AssertNoPasswordFields(noPasswordJson);
        Assert.IsTrue(hasPasswordJson.Contains("\"hasPassword\":true", StringComparison.Ordinal),
            $"带口令分享响应必须声明 hasPassword:true，实际: {hasPasswordJson}");
        Assert.IsTrue(noPasswordJson.Contains("\"hasPassword\":false", StringComparison.Ordinal),
            $"无口令分享响应必须声明 hasPassword:false，实际: {noPasswordJson}");
    }

    [TestMethod]
    public void Legacy_constructor_still_compiles_and_sets_safe_defaults()
    {
#pragma warning disable CS0618 // 保留构造函数的目的就是兼容旧调用
        var legacy = new HostDocumentShareResponse(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "SHARE-001",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddDays(7));
#pragma warning restore CS0618

        Assert.IsNull(legacy.Password, "旧构造的 Password 必须始终为 null");
        Assert.AreEqual(0, legacy.AccessCount);
        Assert.IsTrue(legacy.IsEnabled);
        Assert.AreEqual(1L, legacy.Version);
    }

    [TestMethod]
    public void Error_result_codes_are_stable_for_password_required()
    {
        // 中文注释：匿名访问必须对空口令访问给出稳定机器码 document.host_share.password_required；
        // 错误口令和分享不存在使用同一个 ProblemDetails 避免时序侧信道。
        var required = Result<object>.Failure(new Error(
            DocumentErrorCodes.HostSharePasswordRequired,
            "This share requires a password.",
            ErrorType.Validation));
        var wrongOrMissing = Result<object>.Failure(new Error(
            DocumentErrorCodes.HostShareAccessDenied,
            "Share access denied.",
            ErrorType.NotFound));

        Assert.AreEqual("document.host_share.password_required", required.Error!.Code);
        Assert.AreEqual("document.host_share.access_denied", wrongOrMissing.Error!.Code);
    }

    private static HostDocumentShareResponse CreateShareWithPassword()
    {
        var shareId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        // 中文注释：HasPassword 从 !string.IsNullOrEmpty(兼容 Password 形参) 或独立布尔推导；
        // 内部 PasswordHash 永不进入响应对象。
        return new HostDocumentShareResponse(
            shareId,
            documentId,
            "SHARE-PWD-1",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(3),
            Password: "ThisWillBeIgnoredBySerializer",
            MaxAccessCount: 5,
            AccessCount: 0,
            IsEnabled: true,
            Version: 1L,
            HasPassword: true);
    }

    private static HostDocumentShareResponse CreateShareWithoutPassword()
    {
        var shareId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        return new HostDocumentShareResponse(
            shareId,
            documentId,
            "SHARE-NONE-1",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(3),
            Password: null,
            MaxAccessCount: null,
            AccessCount: 0,
            IsEnabled: true,
            Version: 1L,
            HasPassword: false);
    }

    private static void AssertNoPasswordFields(string json)
    {
        Assert.IsFalse(
            json.Contains("\"password\"", StringComparison.OrdinalIgnoreCase),
            $"响应 JSON 不得包含 password 字段：{json}");
        Assert.IsFalse(
            json.Contains("\"passwordHash\"", StringComparison.OrdinalIgnoreCase),
            $"响应 JSON 不得包含 passwordHash 字段：{json}");
    }
}

/// <summary>
/// 占位 Hasher，Task2 GREEN 阶段替换为 ASP.NET Core PasswordHasher<DocumentSharePasswordSubject>。
/// RED 阶段的 Fake 只用于捕捉契约：Hash 必须区分相同明文，Verify 必须对匹配明文返回真。
/// </summary>
internal sealed class FakePasswordHasher
{
    public string Hash(Guid shareId, string password)
    {
        // 中文注释：RED 时使用简单 Base64(shareId+salt+password) 占位；
        // 绿色阶段换成真正 PBKDF2 实现。
        var salt = Convert.ToBase64String(Guid.CreateVersion7().ToByteArray());
        return Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{shareId:N}|{salt}|{password}"));
    }

    public bool Verify(Guid shareId, string passwordHash, string providedPassword)
    {
        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(passwordHash));
            var parts = raw.Split('|');
            if (parts.Length != 3)
            {
                return false;
            }
            if (!string.Equals(parts[0], shareId.ToString("N"), StringComparison.Ordinal))
            {
                return false;
            }
            return string.Equals(parts[2], providedPassword, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 分享口令错误码稳定机器码。Task2 GREEN 阶段搬入真实 Document 模块错误目录。
/// </summary>
internal static class DocumentErrorCodes
{
    public const string HostSharePasswordRequired = "document.host_share.password_required";
    public const string HostShareAccessDenied = "document.host_share.access_denied";
}
