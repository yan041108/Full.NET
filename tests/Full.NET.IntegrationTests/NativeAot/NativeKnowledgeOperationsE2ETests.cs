using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Ai;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>新运营接口在真实 Native API 上的静态闭包与双库隔离；不执行模型或 Worker。</summary>
[TestClass] [DoNotParallelize]
public sealed class NativeKnowledgeOperationsE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Native_owned_operations_metadata_is_bounded_and_has_no_private_payload(DatabaseProvider provider) => KnowledgeOperationsScenario.VerifyAsync(provider,true);
}
