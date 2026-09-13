using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Tools;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentDelegations;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>委托范围与期限校验失败关闭，不能创建无范围或自委托。</summary>
[TestClass]
public sealed class AiAgentDelegationValidationTests
{
    [TestMethod]
    public async Task Create_rejects_self_grant_and_unscoped_delegation_async()
    {
        var grantorId = Guid.CreateVersion7();
        var service = CreateService();
        var expires = DateTimeOffset.UtcNow.AddHours(1);

        var selfGrant = await service.CreateAsync(
            new CreateAiAgentDelegationRequest(grantorId, "ai.chat.sessions.rename", AiAgentApprovalPermissions.Decide, expires),
            grantorId);
        Assert.IsFalse(selfGrant.IsSuccess);
        Assert.AreEqual(AiErrorCodes.AgentDelegationInvalid, selfGrant.Error!.Code);

        var unscoped = await service.CreateAsync(
            new CreateAiAgentDelegationRequest(Guid.CreateVersion7(), null, null, expires),
            grantorId);
        Assert.IsFalse(unscoped.IsSuccess);
        Assert.AreEqual(AiErrorCodes.AgentDelegationInvalid, unscoped.Error!.Code);
    }

    [TestMethod]
    public async Task HasActive_requires_matching_tool_and_permission_scope_async()
    {
        var grantorId = Guid.CreateVersion7();
        var granteeId = Guid.CreateVersion7();
        var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<int>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var service = CreateService(queries);
        Assert.IsTrue(await service.HasActiveAsync(
            grantorId,
            granteeId,
            "ai.chat.sessions.rename",
            AiAgentApprovalPermissions.Decide));
    }

    private static AiAgentDelegationService CreateService(IQueryExecutor? queries = null)
    {
        var tenant = new CurrentTenantAccessor();
        tenant.SetHost();
        return new AiAgentDelegationService(
            queries ?? Substitute.For<IQueryExecutor>(),
            Substitute.For<ICommandExecutor>(),
            new FixedAgentToolRegistrySource(new AgentToolRegistry([
                new("ai.chat.sessions.rename", 1, AiChatPermissions.Update, "write", true, Substitute.For<IAgentToolHandler>()),
            ])),
            tenant,
            CreateClock(),
            Substitute.For<IIdGenerator>(),
            Options.Create(new AiAgentRuntimeOptions { MaxRunDurationSeconds = 3600 }));
    }

    private static IClock CreateClock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        return clock;
    }
}
