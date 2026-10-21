using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.ManageHostFolders;
using Full.NET.Modules.Files.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;
using Dapper;
using System.Data;
using System.Text.Json;
using Full.NET.Modules.Files.Serialization;

namespace Full.NET.UnitTests.Files;

/// <summary>单元测试（文件）：Host Folder Query Service。</summary>
[TestClass]
public sealed class HostFolderQueryServiceTests
{
    [TestMethod]
    [DataRow(typeof(int))]
    [DataRow(typeof(long))]
    public void Child_projection_converts_database_integer_presence_with_real_dapper(Type presenceType)
    {
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid)); table.Columns.Add("ParentId", typeof(Guid));
        table.Columns.Add("Name", typeof(string)); table.Columns.Add("DisplayOrder", typeof(int));
        table.Columns.Add("Revision", typeof(long)); table.Columns.Add("HasChildren", presenceType);
        var id = Guid.CreateVersion7();
        table.Rows.Add(id, DBNull.Value, "folder", 0, 0L, Convert.ChangeType(1, presenceType));
        table.Rows.Add(Guid.CreateVersion7(), id, "leaf", 1, 0L, Convert.ChangeType(0, presenceType));
        using var reader = table.CreateDataReader();
        var parse = reader.GetRowParser<HostFolderChildRecord>();
        Assert.IsTrue(reader.Read());
        var row = parse(reader);
        Assert.AreEqual(id, row.Id);
        Assert.IsNull(row.ParentId);
        Assert.IsTrue(row.HasChildren);
        Assert.IsTrue(reader.Read());
        var leaf = parse(reader);
        Assert.AreEqual(id, leaf.ParentId);
        Assert.IsFalse(leaf.HasChildren);
    }

    [TestMethod]
    public async Task GetTree_returns_only_direct_children_and_child_presence()
    {
        var rootId = Guid.CreateVersion7();
        var childId = Guid.CreateVersion7();
        var rows = new[]
        {
            CreateRecord(childId, rootId, "child", 2),
            CreateRecord(rootId, null, "root", 1),
        };
        var service = CreateService(rows);

        var result = await service.GetTreeAsync(cancellationToken: CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(1, result.Value!);
        Assert.AreEqual("root", result.Value![0].Name);
        Assert.IsTrue(result.Value![0].HasChildren);
        var children = await service.GetTreeAsync(rootId);
        Assert.HasCount(1, children.Value!);
        Assert.AreEqual(childId, children.Value![0].Id);
        Assert.IsFalse(children.Value![0].HasChildren);
        // HTTP Mapper 按声明的只读列表序列化，不能依赖旧递归属性间接生成集合元数据。
        Assert.IsNotNull(FilesJsonSerializerContext.Default.GetTypeInfo(typeof(IReadOnlyList<HostFolderTreeNode>)));
        using var rootJson = JsonDocument.Parse(JsonSerializer.Serialize(result.Value,
            typeof(IReadOnlyList<HostFolderTreeNode>), FilesJsonSerializerContext.Default));
        using var childJson = JsonDocument.Parse(JsonSerializer.Serialize(children.Value,
            typeof(IReadOnlyList<HostFolderTreeNode>), FilesJsonSerializerContext.Default));
        Assert.IsTrue(rootJson.RootElement[0].GetProperty("hasChildren").GetBoolean());
        Assert.IsFalse(childJson.RootElement[0].GetProperty("hasChildren").GetBoolean());
    }

    [TestMethod]
    public async Task GetById_returns_not_found_when_missing()
    {
        var service = CreateService(Array.Empty<HostFolderRecord>());
        var result = await service.GetByIdAsync(Guid.CreateVersion7(), CancellationToken.None);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.FolderNotFound, result.Error!.Code);
    }

    [TestMethod]
    public async Task Child_budget_rejects_without_constructing_a_recursive_tree()
    {
        var rows = Enumerable.Range(0, HostFolderQueryService.MaximumChildren + 1)
            .Select(i => CreateRecord(Guid.NewGuid(), null, $"folder-{i}", i)).ToArray();
        var result = await CreateService(rows).GetTreeAsync();
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.FolderQueryLimitExceeded, result.Error!.Code);
    }

    [TestMethod]
    public async Task Cancelled_query_stops_before_database_read()
    {
        var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateService([]).GetTreeAsync(cancellationToken: source.Token));
    }

    private static HostFolderQueryService CreateService(IReadOnlyList<HostFolderRecord> rows)
    {
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QueryAsync<HostFolderChildRecord>(
                HostFolderSql.ListChildrenSqlServer,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => {
                var values = (IDictionary<string, object?>)call.Arg<object?>()!;
                var parent = values["ParentId"] as Guid?;
                return rows.Where(row => row.ParentId == parent)
                    .OrderBy(row => row.DisplayOrder).Select(row => new HostFolderChildRecord { Id = row.Id, ParentId = row.ParentId,
                        Name = row.Name, DisplayOrder = row.DisplayOrder, Revision = row.Revision, HasChildren = rows.Any(child => child.ParentId == row.Id) })
                    .Take((int)values["TakeCount"]!).ToArray();
            });
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
        return new HostFolderQueryService(queryExecutor, Microsoft.Extensions.Options.Options.Create(new DatabaseOptions()));
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
