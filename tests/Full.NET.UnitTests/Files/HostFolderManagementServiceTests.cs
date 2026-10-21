using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.ManageHostFolders;
using Full.NET.Modules.Files.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

/// <summary>单元测试（文件）：Host Folder Management Service。</summary>
[TestClass]
public sealed class HostFolderManagementServiceTests
{
    [TestMethod]
    public async Task Delete_rejects_non_empty_folder()
    {
        var folderId = Guid.CreateVersion7();
        var existing = new HostFolderRecord(
            folderId,
            null,
            "docs",
            0,
            1,
            DateTimeOffset.UtcNow,
            Guid.CreateVersion7(),
            null,
            null);
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<HostFolderRecord>(
                HostFolderSql.FindActiveById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(existing);
        queryExecutor.QuerySingleOrDefaultAsync<long>(
                HostFolderSql.CountActiveChildren,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1L);
        queryExecutor.QuerySingleOrDefaultAsync<long>(
                HostFolderSql.CountActiveFiles,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(0L);
        var commandExecutor = Substitute.For<ICommandExecutor>();
        commandExecutor.ExecuteAsync(
                HostFolderSql.SoftDelete,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(0);
        var service = new HostFolderManagementService(
            commandExecutor,
            queryExecutor,
            new HostFolderQueryService(queryExecutor, Microsoft.Extensions.Options.Options.Create(new DatabaseOptions())),
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());

        var result = await service.DeleteAsync(
            folderId,
            Guid.CreateVersion7(),
            new DeleteHostFolderRequest(1),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.FolderNotEmpty, result.Error!.Code);
    }

    [TestMethod]
    public async Task Update_returns_revision_conflict_when_expected_revision_is_stale()
    {
        var folderId = Guid.CreateVersion7();
        var existing = new HostFolderRecord(
            folderId,
            null,
            "docs",
            0,
            2,
            DateTimeOffset.UtcNow,
            Guid.CreateVersion7(),
            null,
            null);
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<HostFolderRecord>(
                HostFolderSql.FindActiveById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(existing);
        var service = new HostFolderManagementService(
            Substitute.For<ICommandExecutor>(),
            queryExecutor,
            new HostFolderQueryService(queryExecutor, Microsoft.Extensions.Options.Options.Create(new DatabaseOptions())),
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());

        var result = await service.UpdateAsync(
            folderId,
            Guid.CreateVersion7(),
            new UpdateHostFolderRequest(1, "renamed", 0),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.RevisionConflict, result.Error!.Code);
    }
    [TestMethod]
    public async Task Create_rejects_parent_at_maximum_depth_without_writing()
    {
        var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<int>(HostFolderSql.AncestorDepthSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(HostFolderQueryService.MaximumDepth);
        var commands = Substitute.For<ICommandExecutor>();
        var folders = new HostFolderQueryService(queries, Microsoft.Extensions.Options.Options.Create(new DatabaseOptions()));
        var service = new HostFolderManagementService(commands, queries, folders, Substitute.For<IClock>(), Substitute.For<IIdGenerator>());
        var result = await service.CreateAsync(Guid.NewGuid(), new CreateHostFolderRequest(Guid.NewGuid(), "too-deep"), CancellationToken.None);
        Assert.IsFalse(result.IsSuccess);
        await commands.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

}
