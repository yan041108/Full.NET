using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.SerialNumbers.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using static Full.NET.IntegrationTests.Workflow.WorkflowDurableDelayApiTests;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实所有者组件验证规则/凭据原子提交、重复、取消墓碑、精确补偿和后续写入保护。</summary>
[TestClass]
public sealed class WorkflowOwnerCommandReceiptApiTests
{
    [TestMethod]
    public async Task Owner_receipts_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Owner_receipts_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        var faults = new ReceiptFaults();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services => {
            var commandRegistration = services.Last(item => item.ServiceType == typeof(ICommandExecutor));
            var createCommandExecutor = commandRegistration.ImplementationFactory ?? throw new InvalidOperationException("The formal command executor factory is required.");
            services.Replace(ServiceDescriptor.Scoped<ICommandExecutor>(scope => new ReceiptFaultExecutor((ICommandExecutor)createCommandExecutor(scope), faults)));
        });
        await factory.InitializeAsync(useSchemaTemplate: false);
        var actor = await factory.CreateHostIdentityAsync("owner.receipt." + Guid.NewGuid().ToString("N"),
            [SerialNumberRulePermissions.Create, SerialNumberRulePermissions.Read, SerialNumberRulePermissions.Update]);
        using var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new("Bearer", actor.AccessToken);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var created = await PostAsync(client, "/api/v1/serial-numbers/rules", new CreateSerialNumberRuleRequest("receipt." + Guid.NewGuid().ToString("N"),
            "Original", "original description", SerialNumberRuleScope.Host, SerialNumberResetInterval.Never, "CMD-{sequence:4}", 1, 9999, 0, true), HttpStatusCode.Created);
        var ruleId = created.GetProperty("id").GetGuid();
        async Task<SerialNumberRuleResponse> Rule() => (await client.GetFromJsonAsync<SerialNumberRuleResponse>($"/api/v1/serial-numbers/rules/{ruleId:D}"))!;
        UpdateSerialNumberRuleRequest Change(SerialNumberRuleResponse current, string name) => new(name, current.Description, current.Scope, current.ResetInterval,
            current.Pattern, current.MinimumValue, current.MaximumValue, current.DisplayOrder, current.IsEnabled, current.Version);
        DataApprovalScenarioCommand Command(SerialNumberRuleResponse current, string name) => new(Guid.CreateVersion7(), Guid.CreateVersion7(),
            DataApprovalScenarioKeys.SerialRuleHostUpdate, ruleId, JsonSerializer.Serialize(Change(current, name), new JsonSerializerOptions(JsonSerializerDefaults.Web)), actor.UserId);
        async Task<Full.NET.Abstractions.Results.Result<DataApprovalCommandEffect>> Run(DataApprovalScenarioCommand command, bool compensate = false) {
            await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var handler = scope.ServiceProvider.GetServices<IDataApprovalScenarioCommandHandler>().Single(item => item.Supports(command.ScenarioKey));
            return compensate ? await handler.CompensateAsync(command, default) : await handler.ApplyAsync(command, default);
        }
        async Task<string?> Receipt(Guid id) => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_serialnumbers_approval_cmd WHERE Id=@id", new { id });
        async Task<Full.NET.Abstractions.Results.Result<DataApprovalCommandEffect>> Redeliver(DataApprovalScenarioCommand pending, bool compensate) {
            for (var attempt = 0; ; attempt++) {
                try { return await Run(pending, compensate); }
                catch (Exception error) when (attempt < 2 && (error is DataCommandException { Kind: DataCommandFailureKind.Deadlock } ||
                    error is SqlException { Number: 1205 } || error is MySqlException { ErrorCode: MySqlErrorCode.LockDeadlock or MySqlErrorCode.UserLockDeadlock })) {
                    // 对已回滚的整个事务重投同一命令身份，模拟可靠消息交付；不重试单条写命令。
                    await Task.Delay(25);
                }
            }
        }
        var original = await Rule(); var command = Command(original, "Applied");
        var concurrent = await Task.WhenAll(Run(command), Run(command));
        foreach (var result in concurrent) { Assert.IsTrue(result.IsSuccess, result.Error?.Code); Assert.AreEqual("applied", result.Value!.OutcomeKey); }
        Assert.AreEqual(concurrent[0].Value!.InputHash, concurrent[1].Value!.InputHash);
        Assert.AreEqual(original.Version + 1, (await Rule()).Version);
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_serialnumbers_approval_cmd WHERE Id=@id", new { id = command.CommandId }));
        Assert.IsFalse((await Run(command with { TargetEntityId = Guid.CreateVersion7() })).IsSuccess);
        var restored = await Run(command, true); Assert.IsTrue(restored.IsSuccess, restored.Error?.Code); Assert.AreEqual("compensated", restored.Value!.OutcomeKey);
        Assert.AreEqual(original.DisplayName, (await Rule()).DisplayName); Assert.AreEqual(original.Version + 2, (await Rule()).Version);
        Assert.IsTrue((await Run(command, true)).IsSuccess); Assert.AreEqual("compensated", (await Run(command)).Value!.OutcomeKey);
        Assert.AreEqual(original.Version + 2, (await Rule()).Version);

        var cancelled = Command(await Rule(), "Must never apply");
        Assert.AreEqual("cancelled", (await Run(cancelled, true)).Value!.OutcomeKey);
        Assert.AreEqual("cancelled", (await Run(cancelled)).Value!.OutcomeKey);
        Assert.AreEqual(original.Version + 2, (await Rule()).Version);

        // 规则已写而凭据未提交时返回失败，真实Result事务必须撤回规则写入；不能只验证mock的返回值。
        var rollback = Command(await Rule(), "Atomic apply"); faults.Arm("serial_numbers.approval_command.insert");
        var rejectedInsert = await Run(rollback); Assert.IsFalse(rejectedInsert.IsSuccess);
        Assert.AreEqual(original.Version + 2, (await Rule()).Version); Assert.IsNull(await Receipt(rollback.CommandId));
        Assert.IsTrue((await Run(rollback)).IsSuccess); var applied = await Rule();
        faults.Arm("serial_numbers.approval_command.complete_compensation");
        Assert.IsFalse((await Run(rollback, true)).IsSuccess);
        Assert.AreEqual(applied.Version, (await Rule()).Version); Assert.AreEqual("Atomic apply", (await Rule()).DisplayName);
        Assert.AreEqual("applied", await Receipt(rollback.CommandId));
        Assert.IsTrue((await Run(rollback, true)).IsSuccess); Assert.AreEqual(original.DisplayName, (await Rule()).DisplayName);

        var conflict = Command(await Rule(), "Before a later user edit"); Assert.IsTrue((await Run(conflict)).IsSuccess);
        using (var response = await client.PutAsJsonAsync($"/api/v1/serial-numbers/rules/{ruleId:D}", Change(await Rule(), "Later user edit")))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var later = await Rule(); var blocked = await Run(conflict, true);
        Assert.IsFalse(blocked.IsSuccess); Assert.AreEqual(SerialNumberErrorCodes.ApprovalCommandCompensationConflict, blocked.Error!.Code);
        Assert.AreEqual(later.Version, (await Rule()).Version); Assert.AreEqual("Later user edit", (await Rule()).DisplayName);
        Assert.AreEqual("applied", await Receipt(conflict.CommandId));
        var disable = new DataApprovalScenarioCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), DataApprovalScenarioKeys.SerialRuleHostDisable, ruleId,
            JsonSerializer.Serialize(new ChangeSerialNumberRuleStatusRequest(later.Version), new JsonSerializerOptions(JsonSerializerDefaults.Web)), actor.UserId);
        Assert.IsTrue((await Run(disable)).IsSuccess); Assert.IsFalse((await Rule()).IsEnabled);
        Assert.IsTrue((await Run(disable, true)).IsSuccess); Assert.IsTrue((await Rule()).IsEnabled); Assert.AreEqual("Later user edit", (await Rule()).DisplayName);
        var beforeRace = await Rule(); var race = Command(beforeRace, "Concurrent apply and cancellation");
        var raced = await Task.WhenAll(Redeliver(race, false), Redeliver(race, true));
        foreach (var result in raced) Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.IsTrue(await Receipt(race.CommandId) is "cancelled" or "compensated");
        var afterRace = await Rule(); Assert.AreEqual(beforeRace.DisplayName, afterRace.DisplayName);
        Assert.IsTrue(afterRace.Version == beforeRace.Version || afterRace.Version == beforeRace.Version + 2);

        await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync("UPDATE fn_serialnumbers_approval_cmd SET StatusKey='compensated' WHERE Id=@id", new { id = cancelled.CommandId }); });
        var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_serialnumbers_approval_cmd");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "ALTER TABLE dbo.fn_serialnumbers_approval_cmd DROP CONSTRAINT fk_fn_serialnumbers_approval_cmd_rule; DROP INDEX ix_fn_serialnumbers_approval_cmd_rule ON dbo.fn_serialnumbers_approval_cmd; ALTER TABLE dbo.fn_serialnumbers_approval_cmd DROP CONSTRAINT ck_fn_serialnumbers_approval_cmd_snapshot; DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%316_SerialRuleApprovalCommands.sql'"
            : "ALTER TABLE fn_serialnumbers_approval_cmd DROP FOREIGN KEY fk_fn_serialnumbers_approval_cmd_rule; ALTER TABLE fn_serialnumbers_approval_cmd DROP INDEX ix_fn_serialnumbers_approval_cmd_rule; ALTER TABLE fn_serialnumbers_approval_cmd DROP CHECK ck_fn_serialnumbers_approval_cmd_snapshot; DELETE FROM schemaversions WHERE ScriptName LIKE '%316_SerialRuleApprovalCommands.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection); await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        Assert.AreEqual(count, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_serialnumbers_approval_cmd"));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>(provider == DatabaseProvider.SqlServer
            ? "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('fn_serialnumbers_approval_cmd') AND name='fk_fn_serialnumbers_approval_cmd_rule'"
            : "SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_serialnumbers_approval_cmd' AND CONSTRAINT_NAME='fk_fn_serialnumbers_approval_cmd_rule'"));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>(provider == DatabaseProvider.SqlServer
            ? "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('fn_serialnumbers_approval_cmd') AND name='ix_fn_serialnumbers_approval_cmd_rule'"
            : "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_serialnumbers_approval_cmd' AND INDEX_NAME='ix_fn_serialnumbers_approval_cmd_rule'"));
        if (provider == DatabaseProvider.MySql) Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=DATABASE() AND ROUTINE_NAME='fn_serialnumbers_ensure_approval_cmd'"));
    }
    /// <summary>只使当前测试指定的凭据CAS失效，全部其他命令仍经正式执行器和真实事务。</summary>
    private sealed class ReceiptFaultExecutor(ICommandExecutor inner, ReceiptFaults faults) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            faults.Take(statement.Name) ? Task.FromResult(0) : inner.ExecuteAsync(statement, parameters, cancellationToken);
    }
    private sealed class ReceiptFaults
    {
        private string? target;
        public void Arm(string name) => Interlocked.Exchange(ref target, name);
        public bool Take(string name) { var current = Volatile.Read(ref target); return current == name && ReferenceEquals(Interlocked.CompareExchange(ref target, null, current), current); }
    }
}
