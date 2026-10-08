using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiKnowledgeDocxUploadTests
{
    private const string Mime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    [TestMethod] [DataRow(1L)] [DataRow(5242880L)]
    public void Matching_docx_name_accepts_declared_bytes_within_five_mib(long bytes)
        => Assert.IsNull(AiKnowledgeFilePolicy.Validate("source.docx", Mime, 1, Guid.CreateVersion7(), bytes));

    [TestMethod] [DataRow(5242881L)] [DataRow(8388608L)]
    public void Docx_rejects_declared_size_above_five_mib(long bytes)
        => Assert.IsNotNull(AiKnowledgeFilePolicy.Validate("source.docx", Mime, 1, Guid.CreateVersion7(), bytes));

    [TestMethod] [DataRow("source.docx", "application/pdf")] [DataRow("source.txt", Mime)]
    public void Docx_extension_and_mime_must_match(string name, string mime)
        => Assert.IsNotNull(AiKnowledgeFilePolicy.Validate(name, mime, 1, Guid.CreateVersion7(), 10));

    [TestMethod] [DataRow(5242880, true)] [DataRow(5242881, false)]
    public async Task Actual_docx_stream_is_bounded_even_without_content_length(int bytes, bool accepted)
    {
        // 上传只验证有界 ZIP 外壳；受隔离解析器随后验证完整 OOXML 结构。
        var data = new byte[bytes]; "PK\u0003\u0004"u8.CopyTo(data);
        using var source = new MemoryStream(data);
        var result = await AiKnowledgeUploadBuffer.ReadAsync(source, Mime, default);
        using var buffer = result.Value;
        Assert.AreEqual(accepted, result.IsSuccess);
        if (accepted) Assert.AreEqual(bytes, buffer!.Size);
    }

    [TestMethod]
    public async Task Docx_non_zip_bytes_fail_before_import_intent_or_file_write()
    {
        using var source = new MemoryStream("plain text"u8.ToArray());
        var result = await AiKnowledgeUploadBuffer.ReadAsync(source, Mime, default);
        using var buffer = result.Value;
        Assert.IsFalse(result.IsSuccess);
    }
}
