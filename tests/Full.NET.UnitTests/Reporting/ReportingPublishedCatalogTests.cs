using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Features.ManageDefinitions;
using Full.NET.Modules.Reporting.Features.PublishedDefinitions;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Reporting;

/// <summary>发布目录消费同一 camelCase 参数快照，不能把默认上下文的 PascalCase 当作持久化协议。</summary>
[TestClass]
public sealed class ReportingPublishedCatalogTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Published_catalog_preserves_parameter_schema(bool host)
    {
        var tenant = new CurrentTenantAccessor();
        if (host) tenant.SetHost(); else tenant.SetTenant(new TenantContext(Guid.NewGuid(), "acme", "Acme"));
        var query = Substitute.For<IQueryExecutor>(); var database = Options.Create(new DatabaseOptions());
        var statement = host ? ReportingTenantGrantSql.ListPublishedHost : ReportingTenantGrantSql.ListPublished;
        query.QueryAsync<ReportingPublishedDefinitionRecord>(statement, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new ReportingPublishedDefinitionRecord {
                DefinitionId = Guid.NewGuid(), VersionNumber = 1,
                ParameterSchemaJson = """[{"parameterKey":"topN","displayName":"返回条数","dataTypeKey":"integer","isRequired":true,"defaultValue":"20"}]"""
            }});
        var resolver = new ReportingPublishedDefinitionResolver(query, new ReportingDefinitionQueryService(query, database), tenant, database);
        var result = await resolver.ListAsync(CancellationToken.None);
        Assert.IsTrue(result.IsSuccess); var parameter = result.Value!.Single().ParameterSchema.Single();
        Assert.AreEqual("topN", parameter.ParameterKey); Assert.AreEqual("返回条数", parameter.DisplayName);
        Assert.AreEqual("integer", parameter.DataTypeKey); Assert.IsTrue(parameter.IsRequired);
        Assert.AreEqual("20", parameter.DefaultValue);
    }
}
