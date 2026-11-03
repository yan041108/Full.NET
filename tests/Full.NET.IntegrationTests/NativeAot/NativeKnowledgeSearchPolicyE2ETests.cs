using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>只覆盖宿主策略拒绝的实际原生公开错误路径，不重跑已验收的搜索工作台矩阵。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeKnowledgeSearchPolicyE2ETests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Native_search_host_policy_denial_returns_problem_without_budget_or_payload(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, true, verifySearch: true, searchPolicyOnly: true);
}
