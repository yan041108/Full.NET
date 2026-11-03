using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Features.DraftMutations;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Microsoft.Extensions.Options;
using NSubstitute;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.UnitTests.EnterpriseRequest;

/// <summary>主单草稿约束及明细父单锁必须覆盖两种 SQL 方言。</summary>
[TestClass]
public sealed class EnterpriseRequestMutationPolicyTests
{
    [TestMethod]
    [DataRow("create")]
    [DataRow("update")]
    [DataRow("delete")]
    public async Task False_organization_authorization_denies_generated_commands_before_policy_or_write(string operation)
    {
        var id = Guid.CreateVersion7();
        var queries = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
        var transaction = Substitute.For<ICommandTransaction>(); var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns(id); tenant.IsAvailable.Returns(true); tenant.IsHost.Returns(false);
        var authorization = Substitute.For<IOrganizationOwnedEntityWriteAuthorizer>();
        authorization.EnsureCanWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(false));
        var policy = new UnexpectedMutationPolicy();
        queries.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new EnterpriseRequestRecord(id, id, id, "REQ", "Title", "Draft", 1m, id, 1, DateTimeOffset.UtcNow, id, null, null, false, null, null));
        transaction.ExecuteResultAsync(Arg.Any<Func<CancellationToken, Task<Result<EnterpriseRequestResponse>>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<Func<CancellationToken, Task<Result<EnterpriseRequestResponse>>>>(0)(default));
        var queryService = new EnterpriseRequestQueryService(queries, Options.Create(new DatabaseOptions()),
            Substitute.For<IUserDataScopeResolver>(), Substitute.For<IDataScopeSqlFilterBuilder>());
        var service = new EnterpriseRequestManagementService(queries, commands, transaction, queryService, tenant,
            Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), policy, authorization);
        var result = operation switch
        {
            "create" => await service.CreateAsync(new("REQ", "Title", "Draft", 1m, id), id, id),
            "update" => await service.UpdateAsync(id, new("REQ", "Title", "Draft", 1m, id, 1), id),
            _ => await service.DeleteAsync(id, new(1), id),
        };
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(ErrorType.Forbidden, result.Error!.Type);
        Assert.IsFalse(commands.ReceivedCalls().Any());
    }

    private sealed class UnexpectedMutationPolicy : IEnterpriseRequestMutationPolicy
    {
        public Task<Error?> ValidateCreateAsync(CreateEnterpriseRequestRequest request, Guid actorUserId, Guid? organizationUnitId, CancellationToken cancellationToken) =>
            throw new AssertFailedException("组织授权拒绝后不得调用业务策略。");
        public Task<Error?> ValidateUpdateAsync(Guid id, UpdateEnterpriseRequestRequest request, Guid actorUserId, CancellationToken cancellationToken) =>
            throw new AssertFailedException("组织授权拒绝后不得调用业务策略。");
        public Task<Error?> ValidateDeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken) =>
            throw new AssertFailedException("组织授权拒绝后不得调用业务策略。");
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Non_draft_create_or_status_override_is_rejected_and_existing_writes_lock_parent(DatabaseProvider provider)
    {
        var queries = Substitute.For<IQueryExecutor>();
        var policy = new EnterpriseRequestMutationPolicy(new(queries, Options.Create(new DatabaseOptions { Provider = provider })));
        var id = Guid.CreateVersion7();
        Assert.IsNotNull(await policy.ValidateCreateAsync(new("REQ", "Title", "Approved", 1m, id), id, id, default));
        Assert.IsNotNull(await policy.ValidateUpdateAsync(id, new("REQ", "Title", "Approved", 1m, id, 1), id, default));
        Assert.IsFalse(queries.ReceivedCalls().Any());
        SqlStatement? captured = null;
        queries.QuerySingleOrDefaultAsync<string>(Arg.Do<SqlStatement>(statement => captured = statement), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns("Submitted");
        Assert.IsNotNull(await policy.ValidateDeleteAsync(id, id, default));
        StringAssert.Contains(captured!.Text, provider == DatabaseProvider.MySql ? "FOR UPDATE" : "UPDLOCK, HOLDLOCK");
        Assert.AreEqual(SqlDataScope.TenantRequired, captured.Scope);
        queries.QuerySingleOrDefaultAsync<string>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns("Draft");
        Assert.IsNull(await policy.ValidateUpdateAsync(id, new("REQ", "Title", "Draft", 1m, id, 1), id, default));
    }

    [TestMethod]
    public async Task Detail_create_on_submitted_parent_is_rejected()
    {
        var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<string>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns("Submitted");
        var policy = new EnterpriseRequestDetailMutationPolicy(queries, new(queries, Options.Create(new DatabaseOptions())));
        var id = Guid.CreateVersion7();
        Assert.IsNotNull(await policy.ValidateCreateAsync(new(id, 1, "Item", 1m, 1m, 1m), id, id, default));
    }

    [TestMethod]
    public async Task Detail_reparent_is_rejected_and_delete_checks_current_parent_status()
    {
        var queries = Substitute.For<IQueryExecutor>();
        var id = Guid.CreateVersion7(); var parentId = Guid.CreateVersion7();
        var row = new EnterpriseRequestDetailRecord(id, id, id, parentId, 1, "Item", 1m, 1m, 1m, 1,
            DateTimeOffset.UtcNow, id, null, null, false, null, null);
        queries.QuerySingleOrDefaultAsync<EnterpriseRequestDetailRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(row);
        queries.QuerySingleOrDefaultAsync<string>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns("Submitted");
        var policy = new EnterpriseRequestDetailMutationPolicy(queries, new(queries, Options.Create(new DatabaseOptions())));
        var error = await policy.ValidateUpdateAsync(id, new(Guid.CreateVersion7(), 1, "Item", 1m, 1m, 1m, 1), id, default);
        Assert.AreEqual("enterprise_request.enterprise_request_lines.parent_immutable", error!.Code);
        Assert.IsNotNull(await policy.ValidateDeleteAsync(id, id, default));
    }
}
