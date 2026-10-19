using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features.ManagePromptVersions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试 AI：Prompt Publication。</summary>
[TestClass]
public sealed class AiPromptPublicationTests
{
    [TestMethod]
    public async Task Publish_permission_cannot_select_previously_published_version()
    {
        var id = Guid.CreateVersion7(); var versionId = Guid.CreateVersion7(); var actor = Guid.CreateVersion7(); var modelId = Guid.CreateVersion7();
        var queries = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
        var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(true);
        var authorization = Substitute.For<ICurrentSessionAuthorization>();
        authorization.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new AuthorizedSessionActor(actor, null, Guid.CreateVersion7()));
        var transaction = Substitute.For<ICommandTransaction>();
        transaction.ExecuteResultAsync(Arg.Any<Func<CancellationToken, Task<Result<AiPromptResponse>>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<Result<AiPromptResponse>>>>()!(call.Arg<CancellationToken>()));
        queries.QuerySingleOrDefaultAsync<Guid>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(id);
        queries.QuerySingleOrDefaultAsync<AiPromptRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiPromptRecord { Id = id, ScopeKey = "host", Revision = 4, LastVersion = 2, ActiveVersionId = Guid.CreateVersion7() });
        var json = AiPromptVersionIntegrity.Serialize(new(modelId, "zh-CN", "{{input}}", 100, 100));
        queries.QuerySingleOrDefaultAsync<AiPromptVersionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiPromptVersionRecord { Id = versionId, PromptId = id, ScopeKey = "host", Version = 1, FormatVersion = 1, BodyJson = json, BodyHash = AiPromptVersionIntegrity.Hash(json, 1, "ollama", "model"), ModelConfigVersion = 1, ProviderKey = "ollama", ModelId = "model", WasPublished = true });
        queries.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiModelConfigRecord { Id = modelId, IsEnabled = true, Version = 1, ProviderKey = "ollama", ModelId = "model" });
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var service = new AiPromptManagementService(new(queries), queries, commands, transaction, Substitute.For<IDataTransactionState>(), tenant, authorization, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }), Substitute.For<IIdGenerator>(), Substitute.For<IClock>());
        var result = await service.SelectAsync(actor, id, new(4, versionId), false, default);
        Assert.IsFalse(result.IsSuccess);
        await commands.DidNotReceive().ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }
}
