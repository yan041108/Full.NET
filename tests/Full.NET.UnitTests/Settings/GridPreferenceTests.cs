using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Settings.Catalogs;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Features.ManageMyGridPreferences;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Full.NET.UnitTests.Settings;

[TestClass]
public sealed class GridPreferenceTests
{
    [TestMethod]
    public void Catalog_rejects_unknown_grid_keys()
    {
        Assert.IsTrue(GridPreferenceCatalog.TryGet("identity.users", out var definition));
        Assert.AreEqual(1, definition.SchemaVersion);
        Assert.IsFalse(GridPreferenceCatalog.TryGet("identity.remote-script", out _));
    }

    [TestMethod]
    public void Validation_rejects_unknown_and_duplicate_column_keys()
    {
        var definition = GridPreferenceCatalog.GetRequired("identity.users");
        var unknown = GridPreferencePolicy.ValidateAndNormalize(
            definition,
            new UpdateGridPreferenceRequest(
                definition.SchemaVersion,
                [new GridColumnPreference("remoteScript", 0, 120, true, null)],
                0));
        var duplicate = GridPreferencePolicy.ValidateAndNormalize(
            definition,
            new UpdateGridPreferenceRequest(
                definition.SchemaVersion,
                [
                    new GridColumnPreference("username", 0, 120, true, null),
                    new GridColumnPreference("username", 1, 180, false, "left"),
                ],
                0));
        var missingColumns = GridPreferencePolicy.ValidateAndNormalize(
            definition,
            new UpdateGridPreferenceRequest(
                definition.SchemaVersion,
                null!,
                0));
        var nullColumn = GridPreferencePolicy.ValidateAndNormalize(
            definition,
            new UpdateGridPreferenceRequest(
                definition.SchemaVersion,
                [null!],
                0));

        Assert.IsFalse(unknown.IsSuccess);
        Assert.AreEqual(SettingsErrorCodes.GridColumnUnknown, unknown.Error!.Code);
        Assert.IsFalse(duplicate.IsSuccess);
        Assert.AreEqual(SettingsErrorCodes.GridColumnDuplicate, duplicate.Error!.Code);
        Assert.IsFalse(missingColumns.IsSuccess);
        Assert.AreEqual(
            SettingsErrorCodes.GridPreferenceInvalid,
            missingColumns.Error!.Code);
        Assert.IsFalse(nullColumn.IsSuccess);
        Assert.AreEqual(
            SettingsErrorCodes.GridPreferenceInvalid,
            nullColumn.Error!.Code);
    }

    [TestMethod]
    public void Validation_normalizes_order_and_preserves_supported_presentation()
    {
        var definition = GridPreferenceCatalog.GetRequired("identity.users");
        var result = GridPreferencePolicy.ValidateAndNormalize(
            definition,
            new UpdateGridPreferenceRequest(
                definition.SchemaVersion,
                [
                    new GridColumnPreference("status", 2, 140, false, "right"),
                    new GridColumnPreference("username", 0, 240, true, "left"),
                ],
                0));

        Assert.IsTrue(result.IsSuccess);
        var normalized = result.Value!;
        CollectionAssert.AreEqual(
            new[] { "username", "status" },
            normalized.Select(column => column.ColumnKey).ToArray());
        Assert.AreEqual(240, normalized[0].Width);
        Assert.AreEqual("left", normalized[0].Fixed);
        Assert.IsFalse(normalized[1].Visible);
    }

    [TestMethod]
    public void Schema_version_change_returns_safe_default()
    {
        var definition = GridPreferenceCatalog.GetRequired("identity.users");
        var restored = GridPreferencePolicy.Restore(
            definition,
            persistedSchemaVersion: definition.SchemaVersion - 1,
            persistedVersion: 7,
            [
                new GridColumnPreference("username", 0, 320, false, "left"),
            ]);

        Assert.AreEqual("identity.users", restored.GridKey);
        Assert.AreEqual(definition.SchemaVersion, restored.SchemaVersion);
        Assert.AreEqual(0, restored.Version);
        Assert.IsEmpty(restored.Columns);

        var corrupted = GridPreferencePolicy.Restore(
            definition,
            definition.SchemaVersion,
            persistedVersion: 8,
            [null!]);
        Assert.AreEqual(0, corrupted.Version);
        Assert.IsEmpty(corrupted.Columns);
    }

    [TestMethod]
    public async Task Post_commit_cache_invalidation_ignores_request_cancellation()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        await using var provider = services.BuildServiceProvider();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        var service = new MyGridPreferenceService(
            Substitute.For<IQueryExecutor>(),
            command,
            new PassThroughTransaction(),
            provider.GetRequiredService<HybridCache>(),
            environment,
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());
        var userId = Guid.CreateVersion7();
        var primed = await service.GetAsync(
            userId,
            "identity.users",
            CancellationToken.None);
        Assert.IsTrue(primed.IsSuccess);
        using var source = new CancellationTokenSource();
        source.Cancel();

        var result = await service.DeleteAsync(
            userId,
            "identity.users",
            source.Token);

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task Concurrent_first_write_unique_constraint_returns_version_conflict()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        await using var provider = services.BuildServiceProvider();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new DataCommandException(
                DataCommandFailureKind.UniqueConstraint,
                new InvalidOperationException("provider detail")));
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        var service = new MyGridPreferenceService(
            Substitute.For<IQueryExecutor>(),
            command,
            new PassThroughTransaction(),
            provider.GetRequiredService<HybridCache>(),
            environment,
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());

        var result = await service.PutAsync(
            Guid.CreateVersion7(),
            "identity.users",
            new UpdateGridPreferenceRequest(1, [], 0));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            SettingsErrorCodes.GridPreferenceVersionConflict,
            result.Error!.Code);
    }

    private sealed class PassThroughTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }
}
