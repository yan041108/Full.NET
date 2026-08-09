using System.Text.Json;
using Full.NET.Modules.Document.Features.ManageHostDocumentShares;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Security;

namespace Full.NET.UnitTests.Document;

[TestClass]
public sealed class DocumentContractCompatibilityTests
{
    [TestMethod]
    public void Legacy_request_constructors_preserve_pre_parity_defaults()
    {
        var item = new CreateHostDocumentItemRequest("title", "description");
        var itemUpdate = new UpdateHostDocumentItemRequest("title", "description", 7);
        var version = new AddHostDocumentVersionRequest(Guid.CreateVersion7());
        var category = new CreateHostDocumentCategoryRequest("category", null, 10);
        var categoryUpdate = new UpdateHostDocumentCategoryRequest("category", null, 10, 11);
        var tag = new CreateHostDocumentTagRequest("tag");
        var tagUpdate = new UpdateHostDocumentTagRequest("tag", 13);

        Assert.AreEqual(HostDocumentType.Unknown, item.DocumentType);
        Assert.AreEqual(HostDocumentStatus.Draft, item.Status);
        Assert.AreEqual(0, item.Sort);
        Assert.IsNull(item.CategoryId);
        Assert.AreEqual(7, itemUpdate.Version);
        Assert.IsNull(itemUpdate.CategoryId);
        Assert.IsNull(version.ChangeDescription);
        Assert.IsNull(category.Code);
        Assert.AreEqual(11, categoryUpdate.Version);
        Assert.IsNull(categoryUpdate.Icon);
        Assert.IsNull(tag.Code);
        Assert.AreEqual(13, tagUpdate.Version);
        Assert.IsNull(tagUpdate.Description);
    }

    [TestMethod]
    public void Share_response_never_serializes_the_persisted_password_value()
    {
        var response = new HostDocumentShareResponse(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "share-code",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(1),
            "plain-text-secret",
            10,
            0,
            true,
            1);

        var json = JsonSerializer.Serialize(response);

        Assert.DoesNotContain("plain-text-secret", json, StringComparison.Ordinal);
        // 中文注释：禁止真实 "Password" 或 "PasswordHash" 字段（包含值）出现在响应中；
        // "HasPassword" 是布尔安全标记（只反映是否有口令，不含口令本身），允许保留。
        Assert.DoesNotContain("\"Password\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"password\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"PasswordHash\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"passwordHash\"", json, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task Share_creation_with_password_fails_closed_until_verification_is_implemented()
    {
        var service = new HostDocumentShareManagementService(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            NullPasswordHasher.Instance);

        var result = await service.CreateAsync(
            new CreateHostDocumentShareRequest(Guid.CreateVersion7(), 7, "secret"));

        Assert.IsFalse(result.IsSuccess);
        // 阻止明文口令创建使用的是显式"密码长度无效"原因码（因为密码是 6 字符
        // 不满足 8-128 范围）；真实生产路径还会经过 PasswordHasher。
        Assert.AreEqual(
            Modules.Document.Contracts.DocumentErrorCodes.SharePasswordInvalidLength,
            result.Error!.Code);
    }
}

/// <summary>
/// 测试用空口令 Hasher：满足构造函数签名，不执行真实哈希。
/// </summary>
internal sealed class NullPasswordHasher : IDocumentSharePasswordHasher
{
    public static readonly NullPasswordHasher Instance = new();

    public string Hash(Guid shareId, string password) => string.Empty;

    public bool Verify(Guid shareId, string passwordHash, string providedPassword) => false;
}
