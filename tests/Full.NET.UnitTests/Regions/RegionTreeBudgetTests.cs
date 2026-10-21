using Full.NET.Data.Abstractions;
using Full.NET.Modules.Regions.Contracts;
using Full.NET.Modules.Regions.Features.ManageAdministrativeRegions;
using Full.NET.Modules.Regions.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Regions;

/// <summary>树查询将根、深度和节点预算传到数据库；超限不得静默交付残缺树。</summary>
[TestClass]
public sealed class RegionTreeBudgetTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Tree_passes_relative_depth_and_refuses_over_budget_result(DatabaseProvider provider)
    {
        var parent = Guid.CreateVersion7();
        var query = new BudgetQueries(Enumerable.Range(0, AdministrativeRegionQueryService.MaxTreeNodes + 1)
            .Select(_ => new AdministrativeRegionTreeRecord { Id = Guid.CreateVersion7(), ParentId = parent }).ToArray());
        var service = new AdministrativeRegionQueryService(query, Options.Create(new DatabaseOptions { Provider = provider }));
        var result = await service.GetTreeAsync(parent, 99);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(RegionsErrorCodes.TreeTooLarge, result.Error?.Code);
        Assert.AreEqual(provider == DatabaseProvider.MySql ? AdministrativeRegionSql.TreeMySql : AdministrativeRegionSql.TreeSqlServer, query.Statement);
        var parameters = (IReadOnlyDictionary<string, object?>)query.Parameters!;
        Assert.AreEqual(parent, parameters["ParentId"]);
        Assert.AreEqual(5, parameters["MaxDepth"]);
        Assert.AreEqual(10001, parameters["MaxNodes"]);
    }

    private sealed class BudgetQueries(IReadOnlyList<AdministrativeRegionTreeRecord> rows) : IQueryExecutor
    {
        internal SqlStatement? Statement;
        internal object? Parameters;
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        { Statement = statement; Parameters = parameters; return Task.FromResult((IReadOnlyList<T>)rows); }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
