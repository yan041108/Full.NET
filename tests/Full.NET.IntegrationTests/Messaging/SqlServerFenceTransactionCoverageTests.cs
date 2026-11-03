using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Messaging.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>以真实 SQL Server CDC 验证同一事务的中间位置不能解除生产者保护。</summary>
[TestClass, DoNotParallelize]
public sealed class SqlServerFenceTransactionCoverageTests
{
    [TestMethod]
    public async Task Same_transaction_requires_the_last_captured_stream_event()
    {
        const string type = "fullnet.organization.unit.changed";
        await using var server = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword(SharedDatabaseFixture.MySqlRootPassword).WithEnvironment("MSSQL_AGENT_ENABLED", "true")
            .WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "2048").WithCreateParameterModifier(p =>
            {
                var limits = p.HostConfig ??= new(); limits.Memory = 3L << 30;
                limits.MemorySwap = limits.Memory; limits.NanoCPUs = 2_000_000_000;
            }).Build();
        await server.StartAsync();
        var builder = new SqlConnectionStringBuilder(server.GetConnectionString());
        await using (var admin = new SqlConnection(builder.ConnectionString))
            await admin.ExecuteAsync("CREATE DATABASE fullnet_fence_transaction");
        builder.InitialCatalog = "fullnet_fence_transaction";
        using var bootstrap = new FullNetApiFactory(DatabaseProvider.SqlServer, builder.ConnectionString);
        await bootstrap.InitializeAsync(useSchemaTemplate: false);
        var enabled = await SqlServerCdcTestSupport.TryEnableCdcAsync(builder.ConnectionString);
        Assert.IsTrue(enabled.Succeeded, enabled.FailureReason);
        await using var db = new SqlConnection(builder.ConnectionString);
        await db.OpenAsync();
        await db.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FullNetApiFactory.FindRepositoryRoot(), "deploy/messaging/sqlserver/enable-shadow-cdc.sql")));
        var first = Guid.CreateVersion7(); var last = Guid.CreateVersion7(); var generation = Guid.CreateVersion7();
        await using (var transaction = await db.BeginTransactionAsync())
        {
            const string insert = """
                INSERT INTO fn_messaging_outbox_event (Id,MessageType,SchemaVersion,ContentType,PartitionKey,Producer,Payload,OccurredAtUtc)
                VALUES (@Id,@type,1,'application/x-memorypack','test','test',0x01,SYSDATETIMEOFFSET())
                """;
            await db.ExecuteAsync(insert, new { Id = first, type }, transaction);
            await db.ExecuteAsync(insert, new { Id = last, type }, transaction);
            await transaction.CommitAsync();
        }
        await db.ExecuteAsync("UPDATE fn_messaging_stream_ownership SET RollbackState=1, RollbackGeneration=@generation WHERE MessageType=@type AND SchemaVersion=1", new { generation, type });
        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        while (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cdc.fullnet_fn_messaging_outbox_event_CT WHERE Id IN (@first,@last) AND __$operation=2", new { first, last }) != 2)
        {
            Assert.IsTrue(DateTimeOffset.UtcNow < deadline, "CDC 必须捕获同一事务的两条事件。");
            await Task.Delay(300);
        }
        var rows = (await db.QueryAsync<(Guid Id, byte[] Commit, byte[] Change)>("""
            SELECT Id, CAST(__$start_lsn AS varbinary(10)), CAST(__$seqval AS varbinary(10))
            FROM cdc.fullnet_fn_messaging_outbox_event_CT WHERE Id IN (@first,@last) AND __$operation=2
            ORDER BY __$command_id, __$seqval
            """, new { first, last })).ToArray();
        Assert.AreEqual(first, rows[0].Id); Assert.AreEqual(last, rows[1].Id);
        var commit = SqlServerCdcLsnCoordinates.FromBytes(rows[0].Commit).CommitLsn;
        Assert.AreEqual(commit, SqlServerCdcLsnCoordinates.FromBytes(rows[1].Commit).CommitLsn);
        await using var scope = bootstrap.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IEventDeliveryProducerFencePositionReader>();
        var fence = await reader.TryReadAsync(type, 1, generation);
        Assert.IsNotNull(fence);
        CdcDeliveryPosition At(int index) => CdcDeliveryPosition.ForSqlServer(null, commit,
            SqlServerCdcLsnCoordinates.FromBytes(rows[index].Change).CommitLsn, 1);
        Assert.IsFalse(await reader.IsSameTransactionCoveredAsync(type, 1, generation, fence.ProducerFencePosition, At(0)));
        Assert.IsTrue(await reader.IsSameTransactionCoveredAsync(type, 1, generation, fence.ProducerFencePosition, At(1)));
        Assert.IsFalse(await reader.IsSameTransactionCoveredAsync(type, 1, Guid.CreateVersion7(), fence.ProducerFencePosition, At(1)));
        Assert.IsFalse(await reader.IsSameTransactionCoveredAsync(type, 1, generation, fence.ProducerFencePosition,
            CdcDeliveryPosition.ForSqlServer(null, commit, SqlServerCdcLsnCoordinates.FromBytes(rows[1].Change).CommitLsn, 2)));

        // 单条 SQL 写入多行时以实际 CDC 元数据判断；同 command 内的其他目标行始终拒绝。
        var ambiguousA = Guid.CreateVersion7(); var ambiguousB = Guid.CreateVersion7();
        await db.ExecuteAsync("""
            INSERT INTO fn_messaging_outbox_event (Id,MessageType,SchemaVersion,ContentType,PartitionKey,Producer,Payload,OccurredAtUtc)
            VALUES (@a,@type,1,'application/x-memorypack','test','test',0x01,SYSDATETIMEOFFSET()),
                   (@b,@type,1,'application/x-memorypack','test','test',0x01,SYSDATETIMEOFFSET())
            """, new { a = ambiguousA, b = ambiguousB, type });
        deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        while (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cdc.fullnet_fn_messaging_outbox_event_CT WHERE Id IN (@a,@b) AND __$operation=2", new { a = ambiguousA, b = ambiguousB }) != 2)
        {
            Assert.IsTrue(DateTimeOffset.UtcNow < deadline, "CDC 必须捕获同一命令的两条事件。");
            await Task.Delay(300);
        }
        var ambiguous = (await db.QueryAsync<(Guid Id, byte[] Commit, byte[] Change, int? Command)>("""
            SELECT Id, CAST(__$start_lsn AS varbinary(10)), CAST(__$seqval AS varbinary(10)), __$command_id
            FROM cdc.fullnet_fn_messaging_outbox_event_CT WHERE Id IN (@a,@b) AND __$operation=2
            """, new { a = ambiguousA, b = ambiguousB })).ToArray();
        Assert.AreEqual(2, ambiguous.Length);
        CollectionAssert.AreEquivalent(new[] { ambiguousA, ambiguousB }, ambiguous.Select(row => row.Id).ToArray());
        CollectionAssert.AreEqual(ambiguous[0].Commit, ambiguous[1].Commit, "多行命令必须实际位于同一提交。");
        fence = await reader.TryReadAsync(type, 1, generation);
        Assert.IsNotNull(fence);
        foreach (var row in ambiguous)
        {
            var other = ambiguous.Single(item => item.Id != row.Id);
            var expected = row.Command.HasValue && other.Command.HasValue && row.Command > other.Command
                && row.Change.AsSpan().SequenceCompareTo(other.Change) > 0;
            Console.WriteLine($"CDC 多行命令: command={row.Command}, change={Convert.ToHexString(row.Change)}, expectedCovered={expected}");
            Assert.AreEqual(expected, await reader.IsSameTransactionCoveredAsync(type, 1, generation, fence.ProducerFencePosition,
                CdcDeliveryPosition.ForSqlServer(null, SqlServerCdcLsnCoordinates.FromBytes(row.Commit).CommitLsn,
                    SqlServerCdcLsnCoordinates.FromBytes(row.Change).CommitLsn, 1)));
        }

        // 同事务内另一流的后续记录被过滤后，已提交位点仍可证明之前的目标流记录完成。
        var target = Guid.CreateVersion7(); var otherStream = Guid.CreateVersion7();
        await using (var transaction = await db.BeginTransactionAsync())
        {
            const string insert = """
                INSERT INTO fn_messaging_outbox_event (Id,MessageType,SchemaVersion,ContentType,PartitionKey,Producer,Payload,OccurredAtUtc)
                VALUES (@Id,@type,1,'application/x-memorypack','test','test',0x01,SYSDATETIMEOFFSET())
                """;
            await db.ExecuteAsync(insert, new { Id = target, type }, transaction);
            await db.ExecuteAsync(insert, new { Id = otherStream, type = "fullnet.messaging.test.other" }, transaction);
            await transaction.CommitAsync();
        }
        deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        while (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cdc.fullnet_fn_messaging_outbox_event_CT WHERE Id IN (@target,@otherStream) AND __$operation=2", new { target, otherStream }) != 2)
        {
            Assert.IsTrue(DateTimeOffset.UtcNow < deadline, "CDC 必须捕获两条真实的事务内记录。");
            await Task.Delay(300);
        }
        var otherPosition = await db.QuerySingleAsync<(byte[] Commit, byte[] Change)>("""
            SELECT CAST(__$start_lsn AS varbinary(10)), CAST(__$seqval AS varbinary(10))
            FROM cdc.fullnet_fn_messaging_outbox_event_CT WHERE Id=@otherStream AND __$operation=2
            """, new { otherStream });
        fence = await reader.TryReadAsync(type, 1, generation);
        Assert.IsNotNull(fence);
        Assert.IsTrue(await reader.IsSameTransactionCoveredAsync(type, 1, generation, fence.ProducerFencePosition,
            CdcDeliveryPosition.ForSqlServer(null, SqlServerCdcLsnCoordinates.FromBytes(otherPosition.Commit).CommitLsn,
                SqlServerCdcLsnCoordinates.FromBytes(otherPosition.Change).CommitLsn, 1)));
    }
}
