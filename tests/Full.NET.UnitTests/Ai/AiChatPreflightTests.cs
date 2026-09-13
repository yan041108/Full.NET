using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Streaming;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>预检拒绝不得启动成功流或调用模型。</summary>
[TestClass]
public sealed class AiChatPreflightTests
{
    [TestMethod]
    [DataRow("input", 422, AiErrorCodes.ChatMessageInvalid)]
    [DataRow("session", 404, AiErrorCodes.ChatSessionNotFound)]
    [DataRow("model", 422, AiErrorCodes.ModelConfigUnavailable)]
    [DataRow("conflict", 409, AiErrorCodes.ChatGenerationInProgress)]
    public async Task Rejected_request_does_not_start_stream_async(string scenario, int status, string code)
    {
        var tenant = new CurrentTenantAccessor();
        tenant.SetHost();
        var queries = Substitute.For<IQueryExecutor>();
        if (scenario != "session")
            queries.QuerySingleOrDefaultAsync<AiChatSessionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new AiChatSessionRecord { Id = Guid.NewGuid(), ModelConfigId = Guid.NewGuid() });
        queries.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiModelConfigRecord { IsEnabled = scenario != "model", ProviderKey = "ollama" });
        var commands = Substitute.For<ICommandExecutor>();
        var transaction = new DapperCommandTransaction(new RecordingDbTransactionCoordinator());
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.NewGuid());
        var http = Substitute.For<IHttpClientFactory>();
        await using var services = new ServiceCollection()
            .AddScoped<ICurrentTenantContextWriter, CurrentTenantAccessor>()
            .AddScoped<ICommandExecutor>(_ => commands).BuildServiceProvider();
        var monitor = new AiChatGenerationLeaseMonitor(services.GetRequiredService<IServiceScopeFactory>(), clock,
            NullLogger<AiChatGenerationLeaseMonitor>.Instance);
        var options = Options.Create(new DatabaseOptions());
        var service = new AiChatStreamService(queries, commands, transaction,
            new AiChatSessionQueryService(queries, tenant, options, clock), TestAiProviders.Streamer(http),
            new AiChatGenerationRegistry(), monitor, Substitute.For<IAiOperationBudgetStore>(),
            new AiChatCleanupScope(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiChatCleanupScope>.Instance),
            tenant, options, clock, ids);
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;

        var result = await service.StreamAsync(Guid.NewGuid(), Guid.NewGuid(),
            new StreamAiChatMessageRequest(scenario == "input" ? " " : "hello"), new AiChatHttpOutput(context));

        Assert.AreNotEqual("text/event-stream", context.Response.ContentType);
        Assert.AreEqual(0L, body.Length);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(code, result.Error!.Code);
        Assert.AreEqual(status, StandardApiResultMapper.ToStatusCode(result.Error.Type));
        http.DidNotReceive().CreateClient(Arg.Any<string>());
    }
}
