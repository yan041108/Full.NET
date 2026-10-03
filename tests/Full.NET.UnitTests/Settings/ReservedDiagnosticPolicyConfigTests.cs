using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Observability;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Features.ManageHostConfigEntries;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Settings;

[TestClass]
public sealed class ReservedDiagnosticPolicyConfigTests
{
    [TestMethod]
    public async Task Generic_config_api_cannot_create_or_read_diagnostic_policy_key()
    {
        var query = Substitute.For<IQueryExecutor>();
        var reads = new HostConfigEntryQueryService(query, Options.Create(new DatabaseOptions()));
        var management = new HostConfigEntryManagementService(
            query,
            Substitute.For<ICommandExecutor>(),
            new InlineTransaction(),
            reads,
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());

        var created = await management.CreateAsync(
            new CreateConfigEntryRequest(
                DiagnosticPolicyLimits.ConfigKey,
                "diagnostic policy",
                null,
                null,
                ConfigValueKinds.Json,
                "{}",
                0));
        var read = await reads.GetByKeyAsync(DiagnosticPolicyLimits.ConfigKey);

        Assert.IsFalse(created.IsSuccess);
        Assert.IsFalse(read.IsSuccess);
        _ = query.DidNotReceive().QuerySingleOrDefaultAsync<ConfigEntryIdentityRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
        _ = query.DidNotReceive().QuerySingleOrDefaultAsync<ConfigEntryRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Generic_config_api_cannot_mutate_or_read_reserved_entry_by_id()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var identity = new ConfigEntryIdentityRecord(
            id, DiagnosticPolicyLimits.ConfigKey, "diagnostic policy", null, null,
            ConfigValueKinds.Json, "{}", 0, true, 1);
        var record = new ConfigEntryRecord(
            id, DiagnosticPolicyLimits.ConfigKey, "diagnostic policy", null, null,
            ConfigValueKinds.Json, "{}", 0, true, now, null, 1);
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<ConfigEntryIdentityRecord>(
                Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ConfigEntryIdentityRecord?>(identity));
        query.QuerySingleOrDefaultAsync<ConfigEntryRecord>(
                Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ConfigEntryRecord?>(record));
        var reads = new HostConfigEntryQueryService(query, Options.Create(new DatabaseOptions()));
        var management = new HostConfigEntryManagementService(
            query,
            Substitute.For<ICommandExecutor>(),
            new InlineTransaction(),
            reads,
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());

        Assert.IsFalse((await reads.GetByIdAsync(id)).IsSuccess);
        Assert.IsFalse((await reads.GetByKeyAsync("ordinary.config")).IsSuccess);
        Assert.IsFalse((await reads.GetByKeyAsync(
            DiagnosticPolicyLimits.ConfigKey.ToUpperInvariant())).IsSuccess);
        Assert.IsFalse((await management.UpdateAsync(id,
            new UpdateConfigEntryRequest("diagnostic policy", null, null, "{}", 0, 1))).IsSuccess);
        Assert.IsFalse((await management.DisableAsync(id)).IsSuccess);
        Assert.IsFalse((await management.DeleteAsync(id, 1)).IsSuccess);
        Assert.IsFalse((await management.BatchDeleteAsync([id])).IsSuccess);
        Assert.IsFalse((await management.BatchUpdateValuesAsync(
            [new ConfigValueUpdate(DiagnosticPolicyLimits.ConfigKey, "{}")])).IsSuccess);
    }

    private sealed class InlineTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) => action(cancellationToken);
    }
}
