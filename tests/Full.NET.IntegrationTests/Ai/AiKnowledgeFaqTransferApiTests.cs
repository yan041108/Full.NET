using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>导入导出真实双库，包含 Host/Tenant、完整来源及部分结果，不重复验收旧草稿能力。</summary>
[TestClass] [DoNotParallelize]
public sealed class AiKnowledgeFaqTransferApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Faq_transfer_rechecks_authority_scope_provenance_and_per_item_idempotency(DatabaseProvider provider)=>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider,false,verifyFaqTransfer:true);
}
