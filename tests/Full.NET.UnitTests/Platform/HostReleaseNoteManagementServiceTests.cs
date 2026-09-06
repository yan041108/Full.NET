using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Platform.Contracts;
using Full.NET.Modules.Platform.Features.ManageHostReleaseNotes;
using Full.NET.Modules.Platform.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Platform;

/// <summary>Host 更新日志管理服务校验与生命周期测试。</summary>
[TestClass]
public sealed class HostReleaseNoteManagementServiceTests
{
    [TestMethod]
    public void ValidateVersionLabel_rejects_invalid_and_oversized_values()
    {
        var blank = HostReleaseNoteManagementService.ValidateVersionLabel("   ");
        Assert.IsFalse(blank.IsSuccess);
        Assert.AreEqual(
            PlatformErrorCodes.ReleaseNoteValidationFailed,
            blank.Error?.Code);

        var invalid = HostReleaseNoteManagementService.ValidateVersionLabel("v1.0.0");
        Assert.IsFalse(invalid.IsSuccess);
        Assert.AreEqual(
            PlatformErrorCodes.ReleaseNoteInvalidVersionLabel,
            invalid.Error?.Code);

        var oversized = HostReleaseNoteManagementService.ValidateVersionLabel(
            new string('1', HostReleaseNoteManagementService.MaxVersionLabelLength + 1));
        Assert.IsFalse(oversized.IsSuccess);
        Assert.AreEqual(
            PlatformErrorCodes.ReleaseNoteValidationFailed,
            oversized.Error?.Code);
    }

    [TestMethod]
    public void ValidateVersionLabel_accepts_semver_like_labels()
    {
        var result = HostReleaseNoteManagementService.ValidateVersionLabel(" 1.2.3 ");
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("1.2.3", result.Value!.VersionLabel);
        Assert.AreEqual(1_002_003_000L, result.Value.VersionSortKey);
    }

    [TestMethod]
    public void ValidateDraftContent_rejects_blank_title_or_content()
    {
        var blankTitle = HostReleaseNoteManagementService.ValidateDraftContent(" ", "content");
        Assert.IsFalse(blankTitle.IsSuccess);
        Assert.AreEqual(
            PlatformErrorCodes.ReleaseNoteValidationFailed,
            blankTitle.Error?.Code);

        var blankContent = HostReleaseNoteManagementService.ValidateDraftContent("title", " ");
        Assert.IsFalse(blankContent.IsSuccess);
        Assert.AreEqual(
            PlatformErrorCodes.ReleaseNoteValidationFailed,
            blankContent.Error?.Code);
    }

    [TestMethod]
    public async Task Publish_is_idempotent_when_release_note_is_already_published()
    {
        var releaseNoteId = Guid.CreateVersion7();
        var actorUserId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var transaction = new RecordingTransaction();
        var innerQuery = Substitute.For<IQueryExecutor>();
        var query = new ReleaseNoteTestQueryExecutor(innerQuery);
        var command = Substitute.For<ICommandExecutor>();
        var published = CreateRecord(
            releaseNoteId,
            ReleaseNoteStatuses.Published,
            2,
            now,
            now);
        innerQuery.QuerySingleOrDefaultAsync<ReleaseNoteRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(published);
        var queries = new HostReleaseNoteQueryService(
            query,
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
                ConnectionString = "Server=(local);Database=unused",
            }));
        var service = new HostReleaseNoteManagementService(
            query,
            command,
            transaction,
            queries,
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());

        var result = await service.PublishAsync(actorUserId, releaseNoteId, 2);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(ReleaseNoteStatuses.Published, result.Value?.Status);
        await command.DidNotReceive().ExecuteAsync(
            Arg.Is(ReleaseNoteSql.Publish),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Retract_is_idempotent_when_release_note_is_already_retracted()
    {
        var releaseNoteId = Guid.CreateVersion7();
        var actorUserId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var transaction = new RecordingTransaction();
        var innerQuery = Substitute.For<IQueryExecutor>();
        var query = new ReleaseNoteTestQueryExecutor(innerQuery);
        var command = Substitute.For<ICommandExecutor>();
        var retracted = CreateRecord(
            releaseNoteId,
            ReleaseNoteStatuses.Retracted,
            3,
            now,
            now);
        innerQuery.QuerySingleOrDefaultAsync<ReleaseNoteRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(retracted);
        var queries = new HostReleaseNoteQueryService(
            query,
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
                ConnectionString = "Server=(local);Database=unused",
            }));
        var service = new HostReleaseNoteManagementService(
            query,
            command,
            transaction,
            queries,
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());

        var result = await service.RetractAsync(actorUserId, releaseNoteId, 3);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(ReleaseNoteStatuses.Retracted, result.Value?.Status);
        await command.DidNotReceive().ExecuteAsync(
            Arg.Is(ReleaseNoteSql.Retract),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    private static ReleaseNoteRecord CreateRecord(
        Guid id,
        string status,
        int version,
        DateTimeOffset now,
        DateTimeOffset? publishedAtUtc) =>
        new()
        {
            Id = id,
            VersionLabel = "1.0.0",
            VersionSortKey = 1_000_000_000L,
            Title = "Release",
            Content = "Details",
            Status = status,
            PublishedAtUtc = publishedAtUtc,
            CreatedAtUtc = now,
            UpdatedAtUtc = publishedAtUtc,
            CreatedByUserId = Guid.CreateVersion7(),
            Version = version,
        };

    private sealed class ReleaseNoteTestQueryExecutor(IQueryExecutor inner) : IQueryExecutor
    {
        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            inner.QueryAsync<T>(statement, parameters, cancellationToken);
    }

    private sealed class RecordingTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);

        public Task<Result<T>> ExecuteResultAsync<T>(
            Func<CancellationToken, Task<Result<T>>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }
}
