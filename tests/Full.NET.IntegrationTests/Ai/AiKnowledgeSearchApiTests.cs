using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>复用真实完整人工计划与发布夹具，分别核对 Host/Tenant 双库公开搜索，不扩展为全格式重跑。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AiKnowledgeSearchApiTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Published_keyword_search_preserves_scope_versions_provenance_and_no_dispatch(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, false, verifySearch: true);

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Search_host_policy_denial_returns_problem_without_budget_or_payload(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, false, verifySearch: true, searchPolicyOnly: true);
}
