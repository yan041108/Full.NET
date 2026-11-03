using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>新传输公开接口实际运行发布的 Linux ELF；模型准备由受控 JIT Worker 完成。</summary>
[TestClass] [DoNotParallelize]
public sealed class NativeKnowledgeFaqTransferE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Native_faq_transfer_preserves_complete_provenance_and_rejects_downgrade(DatabaseProvider provider)=>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider,true,verifyFaqTransfer:true);
}
