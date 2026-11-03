using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

[TestClass] [DoNotParallelize]
public sealed class AiKnowledgeFaqParentApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Faq_parent_graph_survives_publication_search_history_and_transfer(DatabaseProvider provider)=>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider,false,verifyFaqLifecycle:true,verifyFaqSearch:true,verifyFaqParents:true);
}
