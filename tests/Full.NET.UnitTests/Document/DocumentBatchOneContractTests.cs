using System.Text.Json;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features.ManageHostDocumentPermissions;
using Full.NET.Modules.Document.Serialization;

namespace Full.NET.UnitTests.Document;

/// <summary>第一批公开契约的存在性与最小授权语义；不以编译通过代替缺字段行为验证。</summary>
[TestClass]
public sealed class DocumentBatchOneContractTests
{
    [TestMethod]
    public void Metadata_put_rejects_missing_nullable_field_in_source_generated_json()
    {
        const string body = """{"title":"Title","description":null,"categoryId":null,"sort":0,"tagIds":[],"version":1}""";
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize(body,
            DocumentJsonSerializerContext.Default.UpdateHostDocumentItemRequest));
    }

    [TestMethod]
    public void Metadata_put_accepts_explicit_null_and_empty_tags()
    {
        const string body = """{"title":"Title","description":null,"categoryId":null,"thumbnail":null,"sort":0,"tagIds":[],"version":1}""";
        var request = JsonSerializer.Deserialize(body, DocumentJsonSerializerContext.Default.UpdateHostDocumentItemRequest)!;
        Assert.IsNull(request.Description);
        Assert.IsNull(request.CategoryId);
        Assert.IsNull(request.Thumbnail);
        Assert.AreEqual(0, request.TagIds.Count);
    }

    [TestMethod]
    public void Metadata_put_cannot_smuggle_lifecycle_state()
    {
        const string body = """{"title":"Title","description":null,"categoryId":null,"thumbnail":null,"sort":0,"tagIds":[],"version":1,"status":1}""";
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize(body,
            DocumentJsonSerializerContext.Default.UpdateHostDocumentItemRequest));
    }

    [TestMethod]
    public void Write_and_share_do_not_grant_delete_or_acl_governance()
    {
        var actions = DocumentPermissionActions.Parse(["write", "share"]);
        CollectionAssert.AreEquivalent(new[] { "read", "write", "share" }, DocumentPermissionActions.ToNames(actions));
        Assert.AreEqual(0, actions & 4);
    }

    [TestMethod]
    public void Unknown_or_duplicate_actions_fail_closed()
    {
        Assert.AreEqual(0, DocumentPermissionActions.Parse(["manage"]));
        Assert.AreEqual(0, DocumentPermissionActions.Parse(["read", "read"]));
        Assert.AreEqual(0, DocumentPermissionActions.Parse([]));
    }

    [TestMethod]
    public void Version_append_requires_explicit_request_identity()
    {
        var body = $$"""{"fileId":"{{Guid.CreateVersion7():D}}","changeDescription":null}""";
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize(body,
            DocumentJsonSerializerContext.Default.AddHostDocumentVersionRequest));
    }
}
