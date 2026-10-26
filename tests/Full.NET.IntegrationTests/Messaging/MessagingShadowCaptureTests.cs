using System.Data.Common;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Features.ManageShadowCapture;
using Full.NET.Modules.Messaging.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>双库真实 Legacy 采集：同一事务、配额竞争、代次、缺口及预算均不能产生正式发布副本。</summary>
[TestClass]
[DoNotParallelize]
public sealed class MessagingShadowCaptureTests
{
    [TestMethod]
    public async Task SqlServer_real_shadow_capture_is_atomic_bounded_and_generation_guarded() =>
        await VerifyAsync(new() { Provider = DatabaseProvider.SqlServer, ConnectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() });
    [TestMethod]
    public async Task MySql_real_shadow_capture_is_atomic_bounded_and_generation_guarded() =>
        await VerifyAsync(new() { Provider = DatabaseProvider.MySql, ConnectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16 });

    private static async Task VerifyAsync(DatabaseOptions options)
    {
        var auditFault = new AuditFault();
        await using var provider = await EventDeliveryPilotTestSupport.BuildPilotServicesAsync(options, seedCutoverEvidence: false,
            configureServices: services => { services.AddScoped<MessagingDomainAuditWriter>();
                services.AddScoped<ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>>(sp => new FaultAudit(sp.GetRequiredService<MessagingDomainAuditWriter>(), auditFault)); });
        await using var connection = Open(options); await connection.OpenAsync();
        var type = EventDeliveryPilotTestSupport.PilotEventType;
        await using var scope = EventDeliveryPilotTestSupport.CreateHostScope(provider);
        var capture = scope.ServiceProvider.GetRequiredService<ShadowCaptureService>();
        var request = new Full.NET.Modules.Messaging.Contracts.StartShadowCaptureRequest(Guid.CreateVersion7(), null, type, 1, 2, 60, "real capture test");
        // 095 会预置试点 Legacy 所有权；本场景显式移除新测试库中的该行，才能验证真正缺行。
        await connection.ExecuteAsync("DELETE FROM fn_messaging_stream_ownership WHERE MessageType = @Type AND SchemaVersion = 1 AND CurrentOwner = 0 AND RollbackState = 0", new { Type = type });
        Assert.IsFalse((await capture.StartAsync(request, default)).IsSuccess, "缺少持久所有权不得隐式补行。");
        await connection.ExecuteAsync("""
            INSERT INTO fn_messaging_stream_ownership
            (MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner, CutoffEventId, CutoffOccurredAtUtc, Reason, CreatedAtUtc, UpdatedAtUtc)
            VALUES (@Type, 1, 'organization.unit-changed.v1', 0, 0, @Id, @Now, 'fixture persisted Legacy', @Now, @Now)
            """, new { Type = type, Id = Guid.CreateVersion7(), Now = DateTime.UtcNow });
        var first = await capture.StartAsync(request, default); Assert.IsTrue(first.IsSuccess, first.Error?.Message);
        Assert.AreEqual(first.Value!.Generation, (await capture.StartAsync(request, default)).Value!.Generation);
        Assert.IsFalse((await capture.StartAsync(request with { MaxEvents = 3 }, default)).IsSuccess);
        Assert.IsFalse((await capture.StartAsync(request with { ExpectedGeneration = first.Value.Generation }, default)).IsSuccess);
        var transaction = scope.ServiceProvider.GetRequiredService<ICommandTransaction>();
        var writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        var payload = new Full.NET.Modules.Identity.Contracts.IdentityOrganizationUnitChangedIntegrationEvent(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "same real bytes", true, 1, DateTimeOffset.UtcNow);
        var metadata = IntegrationEventMetadata.Create("capture-key", "fullnet.messaging.capture.tests");
        await connection.ExecuteAsync(options.Provider == DatabaseProvider.SqlServer
            ? "CREATE TABLE fn_messaging_capture_test_effect (Id uniqueidentifier NOT NULL PRIMARY KEY NONCLUSTERED)"
            : "CREATE TABLE fn_messaging_capture_test_effect (Id BINARY(16) NOT NULL PRIMARY KEY)");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => transaction.ExecuteAsync<int>(async token =>
        {
            await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(new SqlStatement("messaging.tests.capture_effect",
                "INSERT INTO fn_messaging_capture_test_effect (Id) VALUES (@Id)", SqlDataScope.Global),
                new Dictionary<string, object?> { ["Id"] = Guid.CreateVersion7() }, token);
            await writer.AddAsync(type, 1, payload, metadata, token); throw new InvalidOperationException("rollback business");
        }, default));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message"));
        Assert.AreEqual(0, (await capture.FindAsync(type, 1, default)).Value!.CapturedCount);
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_capture_test_effect"));
        // 并发生产者使用独立作用域，控制行串行预留，不能越过数量上限。
        await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var producer = EventDeliveryPilotTestSupport.CreateHostScope(provider);
            await producer.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async token =>
            { await producer.ServiceProvider.GetRequiredService<IOutboxWriter>().AddAsync(type, 1, payload, metadata, token); return 0; }, default);
        }));
        var closed = (await capture.FindAsync(type, 1, default)).Value!;
        Assert.AreEqual("closed", closed.State); Assert.AreEqual(2, closed.CapturedCount); Assert.AreEqual("message_budget", closed.EndReason);
        Assert.AreEqual(5, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message"));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_outbox_event"));
        var legacy = (await connection.QueryAsync<(Guid Id, byte[] Payload)>("SELECT Id, Payload FROM fn_outbox_message")).ToDictionary(x => x.Id, x => x.Payload);
        var shadow = (await connection.QueryAsync<(Guid Id, byte[] Payload)>("SELECT Id, Payload FROM fn_messaging_shadow_event")).ToArray();
        Assert.AreEqual(2, shadow.Length); foreach (var item in shadow) CollectionAssert.AreEqual(legacy[item.Id], item.Payload);
        Assert.AreEqual(shadow.Sum(item => item.Payload.LongLength), closed.CapturedBytes);
        var nextRequest = request with { RequestId = Guid.CreateVersion7(), ExpectedGeneration = closed.Generation };
        auditFault.Fail = true;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => capture.StartAsync(nextRequest, default));
        auditFault.Fail = false;
        Assert.AreEqual(closed.Generation, (await capture.FindAsync(type, 1, default)).Value!.Generation);
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_shadow_event"), "审计失败必须回滚旧样本清理及代次替换。");
        var next = (await capture.StartAsync(nextRequest, default)).Value!;
        Assert.AreNotEqual(closed.Generation, next.Generation);
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_shadow_event"));
        Assert.AreEqual(5, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message"));
        Assert.IsFalse((await capture.StartAsync(request, default)).IsSuccess, "旧意图不能覆盖新代次。");
        Assert.IsFalse((await capture.CloseAsync(closed.Generation, new("old generation"), default)).IsSuccess);
        await transaction.ExecuteAsync(async token => { await writer.AddAsync(type, 1, payload, token); return 0; }, default);
        var blocked = (await capture.FindAsync(type, 1, default)).Value!;
        Assert.AreEqual("blocked", blocked.State); Assert.AreEqual(1, blocked.MissingMetadataCount);
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_shadow_event"));
        var third = (await capture.StartAsync(nextRequest with { RequestId = Guid.CreateVersion7(), ExpectedGeneration = next.Generation }, default)).Value!;
        await connection.ExecuteAsync("UPDATE fn_messaging_shadow_capture SET MaxPayloadBytes = 1 WHERE Generation = @Generation", new { third.Generation });
        await transaction.ExecuteAsync(async token => { await writer.AddAsync(type, 1, payload, metadata, token); return 0; }, default);
        Assert.AreEqual("byte_budget", (await capture.FindAsync(type, 1, default)).Value!.EndReason);
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_shadow_event"));
        var fourth = (await capture.StartAsync(nextRequest with { RequestId = Guid.CreateVersion7(), ExpectedGeneration = third.Generation }, default)).Value!;
        await connection.ExecuteAsync("UPDATE fn_messaging_shadow_capture SET ExpiresAtUtc = StartedAtUtc WHERE Generation = @Generation", new { fourth.Generation });
        Assert.AreEqual("expired", (await capture.FindAsync(type, 1, default)).Value!.State);
        Assert.AreEqual("time_budget", (await capture.CloseAsync(fourth.Generation, new("freeze expired source"), default)).Value!.EndReason);
        var fifth = (await capture.StartAsync(nextRequest with { RequestId = Guid.CreateVersion7(), ExpectedGeneration = fourth.Generation }, default)).Value!;
        var actualBytes = shadow[0].Payload.LongLength;
        await connection.ExecuteAsync("UPDATE fn_messaging_shadow_capture SET MaxPayloadBytes = @Budget WHERE Generation = @Generation", new { fifth.Generation, Budget = actualBytes + 1 });
        await transaction.ExecuteAsync(async token => { await writer.AddAsync(type, 1, payload, metadata, token); return 0; }, default);
        Assert.AreEqual(1, (await capture.FindAsync(type, 1, default)).Value!.CapturedCount, "成功采集不能在同一批次重复关闭而返回三行影响。");
        await transaction.ExecuteAsync(async token => { await writer.AddAsync(type, 1, payload, metadata, token); return 0; }, default);
        Assert.AreEqual("byte_budget", (await capture.FindAsync(type, 1, default)).Value!.EndReason);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_shadow_event"));
        var sixth = (await capture.StartAsync(nextRequest with { RequestId = Guid.CreateVersion7(), ExpectedGeneration = fifth.Generation }, default)).Value!;
        await Task.WhenAll(Task.Run(async () =>
        {
            await using var producer = EventDeliveryPilotTestSupport.CreateHostScope(provider);
            await producer.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async token =>
            { await producer.ServiceProvider.GetRequiredService<IOutboxWriter>().AddAsync(type, 1, payload, metadata, token); return 0; }, default);
        }), capture.CloseAsync(sixth.Generation, new("race with producer"), default));
        var final = (await capture.FindAsync(type, 1, default)).Value!;
        Assert.AreEqual("closed", final.State); Assert.IsTrue(final.CapturedCount is 0 or 1);
        await transaction.ExecuteAsync(async token => { await writer.AddAsync(type, 1, payload, metadata, token); return 0; }, default);
        Assert.AreEqual(final.CapturedCount, (await capture.FindAsync(type, 1, default)).Value!.CapturedCount, "持久关闭后不得继续采集。");
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_outbox_event"));
        const string lookup = "SELECT Generation FROM fn_messaging_shadow_capture WHERE MessageType = @Type AND SchemaVersion = 1";
        string plan;
        if (options.Provider == DatabaseProvider.SqlServer)
        {
            await connection.ExecuteAsync("SET STATISTICS XML ON");
            try
            {
                using var result = await connection.QueryMultipleAsync(lookup, new { Type = type });
                Assert.AreEqual(1, (await result.ReadAsync<Guid>()).Count());
                plan = await result.ReadSingleAsync<string>();
            }
            finally { await connection.ExecuteAsync("SET STATISTICS XML OFF"); }
        }
        else plan = await connection.QuerySingleAsync<string>("EXPLAIN FORMAT=JSON " + lookup, new { Type = type });
        StringAssert.Contains(plan, "fn_messaging_shadow_capture");
        var exportRoot = Environment.GetEnvironmentVariable("FULLNET_SHADOW_PLAN_ROOT");
        if (!string.IsNullOrEmpty(exportRoot)) await File.WriteAllTextAsync(Path.Combine(exportRoot, "shadow-capture-" + options.Provider + ".plan.txt"), plan);
        var sql = await File.ReadAllTextAsync(Path.Combine(FullNetApiFactory.FindRepositoryRoot(), "src", "BuildingBlocks", "Full.NET.Migrations.DbUp", "Migrations", options.Provider.ToString(), "296_MessagingShadowCapture.sql"));
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(new(Guid.CreateVersion7(), "capture-test", "租户"));
        await Assert.ThrowsExactlyAsync<HostContextRequiredException>(() => capture.FindAsync(type, 1, default));
        await Assert.ThrowsExactlyAsync<HostContextRequiredException>(() => capture.StartAsync(request, default));
        await Assert.ThrowsExactlyAsync<HostContextRequiredException>(() => capture.CloseAsync(fourth.Generation, new("tenant forbidden"), default));
        await connection.ExecuteAsync(options.Provider == DatabaseProvider.SqlServer
            ? "DROP INDEX IX_fn_messaging_shadow_event_CaptureGeneration_Id ON fn_messaging_shadow_event"
            : "ALTER TABLE fn_messaging_shadow_event DROP INDEX IX_fn_messaging_shadow_event_CaptureGeneration_Id");
        if (options.Provider == DatabaseProvider.SqlServer)
        {
            foreach (var batch in System.Text.RegularExpressions.Regex.Split(sql, @"(?im)^\s*GO\s*$"))
                if (!string.IsNullOrWhiteSpace(batch)) { await connection.ExecuteAsync(batch); await connection.ExecuteAsync(batch); }
        }
        else
        {
            // 使用真实 DbUp 分隔符和 journal 恢复过程，不由测试另外解释迁移 SQL。
            await connection.ExecuteAsync("DELETE FROM schemaversions WHERE ScriptName LIKE '%296_MessagingShadowCapture.sql'");
            await MessagingOutboxTestSupport.MigrateAsync(options);
            await MessagingOutboxTestSupport.MigrateAsync(options);
        }
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(options.Provider == DatabaseProvider.SqlServer
            ? "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('fn_messaging_shadow_event') AND name = 'IX_fn_messaging_shadow_event_CaptureGeneration_Id'"
            : "SELECT COUNT(DISTINCT INDEX_NAME) FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_messaging_shadow_event' AND INDEX_NAME = 'IX_fn_messaging_shadow_event_CaptureGeneration_Id'"));
    }
    private sealed class AuditFault { internal bool Fail; }
    private sealed class FaultAudit(MessagingDomainAuditWriter inner, AuditFault fault) : ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>
    {
        public Task WriteAsync(MessagingDomainAuditWrite write, CancellationToken cancellationToken = default) =>
            fault.Fail ? Task.FromException(new InvalidOperationException("audit rollback fault")) : inner.WriteAsync(write, cancellationToken);
    }
    private static DbConnection Open(DatabaseOptions options) => options.Provider == DatabaseProvider.SqlServer ? new SqlConnection(options.ConnectionString)
        : new MySqlConnection(new MySqlConnectionStringBuilder(options.ConnectionString) { GuidFormat = MySqlGuidFormat.Binary16, AllowUserVariables = true }.ConnectionString);
}
