using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Streaming;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>租户聊天实际服务路径必须通过真实 SQL 范围守卫。</summary>
[TestClass]
public sealed class AiChatTenantSqlTests
{
    /// <summary>租户创建会话读取可用模型时不能误调用 Host 管理 SQL。</summary>
    [TestMethod]
    public async Task Tenant_chat_model_lookup_passes_guard_async()
    {
        var tenant = Tenant();
        var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                SqlScopeGuard.Validate(call.Arg<SqlStatement>()!, tenant);
                return (AiModelConfigRecord?)null;
            });
        var service = new AiChatSessionManagementService(queries, Substitute.For<ICommandExecutor>(),
            new DapperCommandTransaction(new RecordingDbTransactionCoordinator()),
            new AiChatSessionQueryService(queries, tenant, Options.Create(new DatabaseOptions()), Substitute.For<IClock>()),
            new AiChatGenerationRegistry(), tenant, Substitute.For<IClock>(), Substitute.For<IIdGenerator>());
        var result = await service.CreateAsync(Guid.NewGuid(), new CreateAiChatSessionRequest(Guid.NewGuid(), null));
        Assert.AreEqual(AiErrorCodes.ModelConfigUnavailable, result.Error?.Code);
    }

    /// <summary>租户配额读取必须绑定当前租户，不能通过 HostOnly 读取。</summary>
    [TestMethod]
    public async Task Tenant_chat_quota_lookup_passes_guard_async()
    {
        var tenant = Tenant();
        var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<AiTenantQuotaRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                SqlScopeGuard.Validate(call.Arg<SqlStatement>()!, tenant);
                return (AiTenantQuotaRecord?)null;
            });
        var result = await new AiChatQuotaGuard(queries, Substitute.For<ICommandExecutor>(), new DapperCommandTransaction(new RecordingDbTransactionCoordinator()), Substitute.For<IClock>())
            .ReserveAsync(Guid.NewGuid(), 5000);
        Assert.IsTrue(result.IsSuccess);
    }

    /// <summary>建立真实可用的租户上下文。</summary>
    private static CurrentTenantAccessor Tenant()
    {
        var tenant = new CurrentTenantAccessor();
        tenant.SetTenant(new TenantContext(Guid.NewGuid(), "tenant-a", "租户 A"));
        return tenant;
    }
}
