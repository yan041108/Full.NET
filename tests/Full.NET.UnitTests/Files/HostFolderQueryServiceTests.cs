using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.ManageHostFolders;
using Full.NET.Modules.Files.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class HostFolderQueryServiceTests
{
    [TestMethod]
    public async Task GetTree_builds_nested_children_in_display_order()
    {
        var rootId = Guid.CreateVersion7();
        var childId = Guid.CreateVersion7();
        var rows = new[]
        {
            CreateRecord(childId, rootId, "child", 2),
            CreateRecord(rootId, null, "root", 1),
        };
        var service = CreateService(rows);

        var result = await service.GetTreeAsync(CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(1, result.Value!);
        Assert.AreEqual("root", result.Value![0].Name);
        Assert.HasCount(1, result.Value![0].Children);
        Assert.AreEqual("child", result.Value![0].Children[0].Name);
    }

    [TestMethod]
    public async Task GetById_returns_not_found_when_missing()
    {
        var service = CreateService(Array.Empty<HostFolderRecord>());
        var result = await service.GetByIdAsync(Guid.CreateVersion7(), CancellationToken.None);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.FolderNotFound, result.Error!.Code);
    }

    private static HostFolderQueryService CreateService(IReadOnlyList<HostFolderRecord> rows)
    {
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QueryAsync<HostFolderRecord>(
                HostFolderSql.ListActive,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(rows);
        queryExecutor.QuerySingleOrDefaultAsync<HostFolderRecord>(
                HostFolderSql.FindActiveById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var parameters = call.Arg<object?>();
                var folderId = ReadFolderId(parameters);
                return rows.FirstOrDefault(row => row.Id == folderId);
            });
        return new HostFolderQueryService(queryExecutor);
    }

    private static HostFolderRecord CreateRecord(
        Guid id,
        Guid? parentId,
        string name,
        int displayOrder) =>
        new(
            id,
            parentId,
            name,
            displayOrder,
            0,
            DateTimeOffset.UtcNow,
            Guid.CreateVersion7(),
            null,
            null);

    private static Guid ReadFolderId(object? parameters)
    {
        if (parameters is IDictionary<string, object?> dictionary
            && dictionary.TryGetValue("FolderId", out var value)
            && value is Guid folderId)
        {
            return folderId;
        }

        throw new InvalidOperationException("FolderId parameter was not provided.");
    }
}
