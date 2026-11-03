using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>仅覆盖 K03 新搜索可达路径与双库发布证明；可选连接正式 Vue 验收，不重跑 K02 格式矩阵。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeKnowledgeSearchE2ETests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Native_published_search_preserves_scope_source_versions_and_no_dispatch(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, true, verifySearch: true);
}
