using Acme.Remediation.Generated;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Tests.CodeGeneration;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>执行真实生成的组织归属服务，保护查询作用域和写入授权边界。</summary>
[TestClass]
public sealed class GeneratedOrganizationScopeRuntimeTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Scoped_count_and_list_preserve_tenant_and_organization_filters(DatabaseProvider provider)
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<OrganizationChildRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OrganizationChildRecord>());
        var resolver = Substitute.For<IUserDataScopeResolver>();
        var builder = Substitute.For<IDataScopeSqlFilterBuilder>();
        var actorId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var scope = new EffectiveUserDataScope(false, []);
        resolver.ResolveAsync(actorId, false, Arg.Any<CancellationToken>()).Returns(scope);
        builder.BuildOrganizationUnitFilter(scope, "OrganizationUnitId", actorId)
            .Returns(new DataScopeSqlFilter("OrganizationUnitId = @ScopeUnitId",
                new Dictionary<string, object?> { ["ScopeUnitId"] = unitId }));
        var service = new OrganizationChildQueryService(query,
            Options.Create(new DatabaseOptions { Provider = provider }), resolver, builder);

        var result = await service.ListAsync(actorId, false, 2, 10);

        Assert.IsTrue(result.IsSuccess);
        var calls = query.ReceivedCalls().ToArray();
        Assert.HasCount(2, calls, "总数和分页查询都必须实际执行作用域拼接。");
        foreach (var call in calls)
        {
            var statement = (SqlStatement)call.GetArguments()[0]!;
            var parameters = (IReadOnlyDictionary<string, object?>)call.GetArguments()[1]!;
            StringAssert.Contains(statement.Text, "AND TenantId = @TenantId AND (OrganizationUnitId = @ScopeUnitId)");
            Assert.AreEqual(SqlDataScope.TenantRequired, statement.Scope);
            Assert.AreEqual(SqlTenantBinding.CurrentTenantId, statement.TenantBinding);
            Assert.AreEqual(unitId, parameters["ScopeUnitId"]);
        }
    }

    [TestMethod]
    public async Task Successful_false_write_authorization_rejects_before_writes_and_rolls_back()
    {
        var tenant = Substitute.For<ICurrentTenant>();
        var tenantId = Guid.NewGuid();
        tenant.IsAvailable.Returns(true);
        tenant.IsHost.Returns(false);
        tenant.Id.Returns(tenantId);
        var authorizer = Substitute.For<IOrganizationOwnedEntityWriteAuthorizer>();
        var actorId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        authorizer.EnsureCanWriteAsync(tenantId, unitId, actorId, Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(false));
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var coordinator = Substitute.For<IDbTransactionCoordinator>();
        var queries = new OrganizationChildQueryService(query, Options.Create(new DatabaseOptions()),
            Substitute.For<IUserDataScopeResolver>(), Substitute.For<IDataScopeSqlFilterBuilder>());
        var service = new OrganizationChildManagementService(query, command, new DapperCommandTransaction(coordinator),
            queries, tenant, Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), authorizer);

        var result = await service.CreateAsync(new CreateOrganizationChildRequest("child", Guid.NewGuid()), actorId, unitId);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(OrganizationErrorCodes.WriteAccessDenied, result.Error!.Code);
        Assert.AreEqual(ErrorType.Forbidden, result.Error.Type);
        await authorizer.Received(1).EnsureCanWriteAsync(tenantId, unitId, actorId, Arg.Any<CancellationToken>());
        await command.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
        await coordinator.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await coordinator.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Generated_relation_lookup_has_no_whitespace_only_lines(bool organizationOwned)
    {
        var schema = organizationOwned ? CodeGenerationRemediationSchemas.OrganizationLink() : CodeGenerationRemediationSchemas.ProductTag();
        var artifact = CrudArtifactGenerator.Generate(schema)
            .Single(item => item.RelativePath == $"backend/{schema.ClrTypeName}RelationLookup.g.cs");
        Assert.IsFalse(artifact.Content.Split('\n').Any(line => line.Length > 0 && string.IsNullOrWhiteSpace(line)),
            "可选搜索保护为空时不能产生带尾随空格的行。");
    }
}
