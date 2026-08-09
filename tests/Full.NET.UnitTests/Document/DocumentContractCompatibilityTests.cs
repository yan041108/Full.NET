using System.Text.Json;
using Full.NET.Modules.Document.Features.ManageHostDocumentShares;
using Full.NET.Modules.Document.Contracts;

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
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
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
            null!);

        var result = await service.CreateAsync(
            new CreateHostDocumentShareRequest(Guid.CreateVersion7(), 7, "secret"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(DocumentErrorCodes.ShareInvalid, result.Error!.Code);
    }
}
