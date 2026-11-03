using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>原生公开接口读取实际 OCR 和人工完整发布中的父锚点，不做模型派发。</summary>
[TestClass] [DoNotParallelize]
public sealed class NativeKnowledgeSearchRelationsE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Native_relations_preserve_embedded_parent_and_complete_authority(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, true, "docx-image", verifyRelations: true);
}
