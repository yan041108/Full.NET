namespace Full.NET.Modules.Files.Contracts;

/// <summary>Host 虚拟目录写操作的稳定权限码目录。</summary>
public static class HostFolderPermissions
{
    /// <summary>创建 Host 虚拟目录。</summary>
    public const string Create = "files.folders.create";

    /// <summary>更新 Host 虚拟目录名称与排序。</summary>
    public const string Update = "files.folders.update";

    /// <summary>删除空的 Host 虚拟目录。</summary>
    public const string Delete = "files.folders.delete";
}

/// <summary>Host 虚拟目录树节点；目录仅表达逻辑归属，不映射磁盘路径。</summary>
/// <param name="Id">目录稳定标识。</param>
/// <param name="ParentId">父目录标识；根节点为 <see langword="null"/>。</param>
/// <param name="Name">同级唯一目录名。</param>
/// <param name="DisplayOrder">同级排序值。</param>
/// <param name="Revision">乐观并发修订号。</param>
/// <param name="Children">子目录树。</param>
public sealed record HostFolderTreeNode(
    Guid Id,
    Guid? ParentId,
    string Name,
    int DisplayOrder,
    long Revision,
    IReadOnlyList<HostFolderTreeNode> Children);

/// <summary>Host 虚拟目录详情响应。</summary>
/// <param name="Id">目录稳定标识。</param>
/// <param name="ParentId">父目录标识；根节点为 <see langword="null"/>。</param>
/// <param name="Name">同级唯一目录名。</param>
/// <param name="DisplayOrder">同级排序值。</param>
/// <param name="Revision">乐观并发修订号。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="CreatedByUserId">创建者 Host 用户标识。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="UpdatedByUserId">最近更新者 Host 用户标识。</param>
public sealed record HostFolderResponse(
    Guid Id,
    Guid? ParentId,
    string Name,
    int DisplayOrder,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId);

/// <summary>创建 Host 虚拟目录请求。</summary>
/// <param name="ParentId">父目录标识；省略或 <see langword="null"/> 表示根目录。</param>
/// <param name="Name">目录名；同级唯一。</param>
/// <param name="DisplayOrder">同级排序值。</param>
public sealed record CreateHostFolderRequest(
    Guid? ParentId,
    string Name,
    int DisplayOrder = 0);

/// <summary>更新 Host 虚拟目录请求。</summary>
/// <param name="ExpectedRevision">客户端最后读取到的修订号。</param>
/// <param name="Name">新目录名。</param>
/// <param name="DisplayOrder">新排序值。</param>
public sealed record UpdateHostFolderRequest(
    long ExpectedRevision,
    string Name,
    int DisplayOrder);

/// <summary>删除 Host 虚拟目录请求。</summary>
/// <param name="ExpectedRevision">客户端最后读取到的修订号。</param>
public sealed record DeleteHostFolderRequest(long ExpectedRevision);

/// <summary>更新 Host 文件元数据请求；不修改对象存储键与内容。</summary>
/// <param name="ExpectedRevision">客户端最后读取到的修订号。</param>
/// <param name="OriginalFileName">新的展示文件名。</param>
/// <param name="FolderId">新的所属目录；<see langword="null"/> 表示移出目录。</param>
public sealed record UpdateHostFileMetadataRequest(
    long ExpectedRevision,
    string OriginalFileName,
    Guid? FolderId);

/// <summary>Host 文件引用声明只读列表项；数据仅来自 Files 模块本地引用声明表。</summary>
/// <param name="Id">引用声明标识。</param>
/// <param name="IdempotencyKey">消费者幂等键。</param>
/// <param name="ConsumerModule">消费者模块稳定键。</param>
/// <param name="ConsumerReferenceId">消费者业务引用标识。</param>
/// <param name="State">声明状态稳定机器码。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="ConfirmedAtUtc">确认时间（UTC）。</param>
/// <param name="ReleasedAtUtc">释放时间（UTC）。</param>
public sealed record HostFileReferenceClaimResponse(
    Guid Id,
    string IdempotencyKey,
    string ConsumerModule,
    Guid ConsumerReferenceId,
    string State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ConfirmedAtUtc,
    DateTimeOffset? ReleasedAtUtc);
