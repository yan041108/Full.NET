using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>专用测试库内阻断检查点确认事务；不改操作行、所有权、Inbox 或业务结果。</summary>
internal sealed class MessagingReplayCheckpointGate(DbConnection owner, DatabaseProvider provider, string resource) : IAsyncDisposable
{
    private bool _released;

    internal static async Task<MessagingReplayCheckpointGate> CreateAsync(DbConnection owner, DatabaseProvider provider)
    {
        var resource = "fullnet.replay.checkpoint." + Guid.NewGuid().ToString("N");
        if (provider == DatabaseProvider.SqlServer)
        {
            var result = await owner.ExecuteScalarAsync<int>("DECLARE @Result int; EXEC @Result = sys.sp_getapplock @Resource=@Resource, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @Result", new { Resource = resource });
            Assert.IsTrue(result >= 0);
            await owner.ExecuteAsync($"""
                CREATE TRIGGER dbo.fn_messaging_test_replay_gate ON dbo.fn_messaging_replay_operation AFTER UPDATE AS
                BEGIN
                    IF EXISTS (SELECT 1 FROM inserted i INNER JOIN deleted d ON i.Id=d.Id
                        WHERE d.InFlightOffset IS NOT NULL AND i.InFlightOffset IS NULL)
                    BEGIN
                        DECLARE @Result int;
                        EXEC @Result = sys.sp_getapplock @Resource='{resource}', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=60000;
                        IF @Result < 0 THROW 51000, 'Test checkpoint gate timed out', 1;
                    END
                END
                """);
        }
        else
        {
            Assert.AreEqual(1, await owner.ExecuteScalarAsync<int>("SELECT GET_LOCK(@Resource, 0)", new { Resource = resource }));
            await owner.ExecuteAsync($"""
                CREATE TRIGGER fn_messaging_test_replay_gate AFTER UPDATE ON fn_messaging_replay_operation FOR EACH ROW
                BEGIN
                    DECLARE Acquired int;
                    IF OLD.InFlightOffset IS NOT NULL AND NEW.InFlightOffset IS NULL THEN
                        SET Acquired = GET_LOCK('{resource}', 60);
                        IF Acquired <> 1 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Test checkpoint gate timed out'; END IF;
                        DO RELEASE_LOCK('{resource}');
                    END IF;
                END
                """);
        }
        return new(owner, provider, resource);
    }

    internal async Task ReleaseAsync()
    {
        if (_released) return;
        _released = true;
        await owner.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "EXEC sys.sp_releaseapplock @Resource=@Resource, @LockOwner='Session'"
            : "DO RELEASE_LOCK(@Resource)", new { Resource = resource });
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync();
        await owner.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "DROP TRIGGER dbo.fn_messaging_test_replay_gate" : "DROP TRIGGER fn_messaging_test_replay_gate");
    }
}
