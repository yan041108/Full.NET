using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>本地权威撤销与可靠外部通知意图同事务；重复通知以中心会话/客户端双键收敛。</summary>
internal sealed class IdentityOidcLogoutPublisher(IQueryExecutor queries, IOutboxWriter outbox)
{
    public async Task PublishAsync(SqlStatement targets, Dictionary<string, object?> parameters, CancellationToken token)
    {
        foreach (var row in await queries.QueryAsync<IdentityOidcLogoutTargetRow>(targets, parameters, token).ConfigureAwait(false))
        {
            var config = IdentityOidcClientMetadata.Read(IdentityOidcStoreSupport.DeserializeProperties(row.PropertiesJson));
            if (string.IsNullOrEmpty(config.BackchannelLogoutUri)) continue;
            await outbox.AddAsync(IdentityOidcLogoutRequested.EventType, 1,
                new IdentityOidcLogoutRequested(Guid.CreateVersion7(), row.UserId, row.CenterSessionId,
                    row.ClientId, config.BackchannelLogoutUri), token).ConfigureAwait(false);
        }
    }
}

/// <summary>Outbox 消费只落本模块投递意图；HTTP 由独立轮询器在事务外执行。</summary>
internal sealed class IdentityOidcLogoutRequestedHandler(IIntegrationEventSerializer serializer,
    ICommandExecutor commands, IClock clock, IOptions<DatabaseOptions> database) : IIntegrationEventHandler
{
    public string EventType => IdentityOidcLogoutRequested.EventType;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;
    public async Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var request = serializer.Deserialize<IdentityOidcLogoutRequested>(payload);
        if (request.Id == Guid.Empty || request.UserId == Guid.Empty || request.CenterSessionId == Guid.Empty
            || string.IsNullOrEmpty(request.ClientId)) throw new InvalidOperationException("Invalid logout delivery intent.");
        await commands.ExecuteAsync(database.Value.Provider == DatabaseProvider.MySql
                ? IdentityOidcLogoutSql.InsertMySql : IdentityOidcLogoutSql.InsertSqlServer,
            IdentitySqlParameters.Create(("Id", request.Id), ("CenterSessionId", request.CenterSessionId),
                ("UserId", request.UserId), ("ClientId", request.ClientId), ("TargetUri", request.TargetUri),
                ("Now", clock.UtcNow)), cancellationToken).ConfigureAwait(false);
    }
}
