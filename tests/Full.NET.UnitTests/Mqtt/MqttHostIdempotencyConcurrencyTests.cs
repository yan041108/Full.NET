using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Mqtt.Configuration;
using Full.NET.Modules.Mqtt.Contracts;
using Full.NET.Modules.Mqtt.Features.ManageControlPlane;
using Full.NET.Modules.Mqtt.Infrastructure;
using Full.NET.Modules.Mqtt.Persistence;
using Full.NET.Modules.Mqtt.Security;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Mqtt;

/// <summary>Host 空租户并发插入必须收敛到唯一记录，并按完整摘要决定回放或冲突。</summary>
[TestClass]
public sealed class MqttHostIdempotencyConcurrencyTests
{
    /// <summary>查找幂等记录的 SQL 必须把 Host 空租户与租户标识等值匹配区分开。</summary>
    [TestMethod]
    public void Find_message_by_idempotency_matches_host_null_tenant()
    {
        StringAssert.Contains(
            MqttSql.FindMessageByIdempotency.Text,
            "message.TenantId IS NULL AND @TenantId IS NULL",
            StringComparison.Ordinal);
    }

    /// <summary>并发插入撞到唯一约束且正文相同，应回放已有记录且不再次发布。</summary>
    [TestMethod]
    public async Task Unique_constraint_replays_matching_host_payload_without_second_publish_async()
    {
        var existing = CreateHostRecord("on");
        var fixture = CreateHostFixture(uniqueConstraintOnInsert: true, replayRecord: existing);
        var result = await fixture.Service.PublishAsync(
            Guid.CreateVersion7(),
            new PublishMqttMessageRequest("fullnet/control/ping", "on", 1, null, "host-key"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Value);
        Assert.AreEqual(existing.Id, result.Value.Id);
        await fixture.Commands.DidNotReceive().ExecuteAsync(
            Arg.Is<SqlStatement>(statement =>
                statement != null && statement.Name == "mqtt.update_message_status"),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>并发插入撞到唯一约束但正文不同，必须冲突关闭，不能覆盖首次发布。</summary>
    [TestMethod]
    public async Task Unique_constraint_rejects_different_host_payload_async()
    {
        var existing = CreateHostRecord("on");
        var fixture = CreateHostFixture(uniqueConstraintOnInsert: true, replayRecord: existing);
        var result = await fixture.Service.PublishAsync(
            Guid.CreateVersion7(),
            new PublishMqttMessageRequest("fullnet/control/ping", "no", 1, null, "host-key"),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(MqttErrorCodes.IdempotencyConflict, result.Error.Code);
        await fixture.Commands.DidNotReceive().ExecuteAsync(
            Arg.Is<SqlStatement>(statement =>
                statement != null && statement.Name == "mqtt.update_message_status"),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>唯一约束后读不到已有行时失败关闭，避免再发一条 Broker 消息。</summary>
    [TestMethod]
    public async Task Unique_constraint_without_existing_row_fails_closed_async()
    {
        var fixture = CreateHostFixture(uniqueConstraintOnInsert: true, replayRecord: null);
        var result = await fixture.Service.PublishAsync(
            Guid.CreateVersion7(),
            new PublishMqttMessageRequest("fullnet/control/ping", "on", 1, null, "host-key"),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(MqttErrorCodes.IdempotencyConflict, result.Error.Code);
    }

    /// <summary>MySQL 必须用生成列哨兵闭合 Host 空租户唯一索引窗口。</summary>
    [TestMethod]
    public void Mysql_forward_migration_uses_scope_tenant_sentinel_unique_index()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "BuildingBlocks",
            "Full.NET.Migrations.DbUp",
            "Migrations",
            "MySql",
            "208_MqttHostIdempotencyScope.sql"));
        StringAssert.Contains(sql, "ScopeTenantKey", StringComparison.Ordinal);
        StringAssert.Contains(
            sql,
            "COALESCE(TenantId, 0x00000000000000000000000000000000)",
            StringComparison.Ordinal);
        StringAssert.Contains(
            sql,
            "UX_fn_mqtt_message_ScopeTenantKey_IdempotencyKey",
            StringComparison.Ordinal);
        StringAssert.Contains(
            sql,
            "UX_fn_mqtt_message_TenantId_IdempotencyKey",
            StringComparison.Ordinal);
    }

    /// <summary>SQL Server 必须以持久化作用域键和过滤唯一索引覆盖 Host 空租户。</summary>
    [TestMethod]
    public void SqlServer_forward_migration_persists_scope_tenant_key_unique_index()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "BuildingBlocks",
            "Full.NET.Migrations.DbUp",
            "Migrations",
            "SqlServer",
            "208_MqttHostIdempotencyScope.sql"));
        StringAssert.Contains(sql, "ScopeTenantKey", StringComparison.Ordinal);
        StringAssert.Contains(sql, "ISNULL(TenantId", StringComparison.Ordinal);
        StringAssert.Contains(
            sql,
            "UX_fn_mqtt_message_ScopeTenantKey_IdempotencyKey",
            StringComparison.Ordinal);
        StringAssert.Contains(sql, "IdempotencyKey IS NOT NULL", StringComparison.Ordinal);
    }

    /// <summary>构造 Host 发布夹具；插入唯一冲突后按指定记录回放。</summary>
    /// <param name="uniqueConstraintOnInsert">插入是否模拟唯一约束冲突。</param>
    /// <param name="replayRecord">冲突后再次查询得到的已有记录；空表示读不到行。</param>
    private static HostPublishFixture CreateHostFixture(
        bool uniqueConstraintOnInsert,
        MqttMessageRecord? replayRecord)
    {
        var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<MqttMessageRecord>(
                Arg.Is<SqlStatement>(statement =>
                    statement != null && statement.Name == "mqtt.find_message_by_idempotency"),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<MqttMessageRecord?>(null),
                Task.FromResult(replayRecord));

        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        if (uniqueConstraintOnInsert)
        {
            commands.ExecuteAsync(
                    Arg.Is<SqlStatement>(statement =>
                        statement != null && statement.Name == "mqtt.insert_message"),
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns<int>(_ => throw new DataCommandException(
                    DataCommandFailureKind.UniqueConstraint,
                    new InvalidOperationException("duplicate host idempotency key")));
        }

        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(true);
        tenant.IsAvailable.Returns(false);
        tenant.Id.Returns((Guid?)null);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.Parse("2026-09-07T02:00:00Z"));
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7());

        var options = Options.Create(new MqttBrokerOptions
        {
            Enabled = true,
            MaximumPayloadBytes = 1024,
            MaximumPublishRatePerMinute = 60,
            AllowedPublishTopicPrefixes = ["fullnet/"],
        });
        var service = new MqttMessagePublishService(
            queries,
            commands,
            tenant,
            clock,
            ids,
            options,
            new MqttPublishRateLimiter(),
            new MqttBrokerPublisher(options));
        return new HostPublishFixture(service, commands);
    }

    /// <summary>创建带完整摘要的 Host 消息记录。</summary>
    /// <param name="payload">首次发布正文。</param>
    private static MqttMessageRecord CreateHostRecord(string payload) => new()
    {
        Id = Guid.Parse("01956000-0001-7000-8000-000000000010"),
        Topic = "fullnet/control/ping",
        PayloadSizeBytes = Encoding.UTF8.GetByteCount(payload),
        PayloadDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),
        Qos = 1,
        Status = MqttMessageStatuses.Pending,
        IdempotencyKey = "host-key",
        SummaryMessage = "Pending publish.",
        CreatedAtUtc = DateTimeOffset.Parse("2026-09-07T02:00:00Z"),
        CreatedByUserId = Guid.Parse("01956000-0001-7000-8000-000000000011"),
    };

    /// <summary>定位仓库根目录，供读取尚未编译进程序集的迁移脚本。</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 Full.NET 仓库根目录。");
    }

    /// <summary>Host 发布测试夹具。</summary>
    /// <param name="Service">被测发布服务。</param>
    /// <param name="Commands">命令执行替身，用于确认未二次发布。</param>
    private sealed record HostPublishFixture(
        MqttMessagePublishService Service,
        ICommandExecutor Commands);
}
