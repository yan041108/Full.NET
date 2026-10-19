using Full.NET.Modules.Ai.Contracts;
namespace Full.NET.UnitTests.Ai;
/// <summary>单元测试分片：AiKnowledgeIndexEnqueue。</summary>
public sealed partial class AiKnowledgeIndexEnqueueTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Explicit_pdf_runtime_accepts_original_session(bool tenant)
    {
        var f = new Fixture(tenant); f.Pdf.Enabled = true;
        f.Source = f.Source! with { ContentType = "application/pdf" };
        var result = await f.Run(); Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(2, f.Writes);
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Disabled_pdf_runtime_refuses_before_write(bool tenant)
    {
        var f = new Fixture(tenant); f.Source = f.Source! with { ContentType = "application/pdf" };
        var result = await f.Run(); Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(AiKnowledgeErrorCodes.IndexFormatUnsupported, result.Error!.Code); Assert.AreEqual(0, f.Writes);
    }
}
