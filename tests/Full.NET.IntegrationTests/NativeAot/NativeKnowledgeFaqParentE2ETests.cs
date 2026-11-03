using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

[TestClass] [DoNotParallelize]
public sealed class NativeKnowledgeFaqParentE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Native_faq_parent_graph_rechecks_every_ancestor_before_dispatch_and_history(DatabaseProvider provider)=>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider,true,verifyFaqLifecycle:true,verifyFaqSearch:true,verifyFaqParents:true);
}
