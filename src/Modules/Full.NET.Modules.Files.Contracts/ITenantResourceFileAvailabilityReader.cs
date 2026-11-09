namespace Full.NET.Modules.Files.Contracts;

/// <summary>按可信租户和资源归属查询已授权文件的元数据状态。</summary>
public interface ITenantResourceFileAvailabilityReader
{
    /// <summary>没有可核对的就绪记录或释放记录时返回空；调用方负责原业务资源授权。</summary>
    Task<string?> GetAvailabilityKeyAsync(string ownerModuleKey, Guid resourceId, Guid fileId,
        CancellationToken cancellationToken = default);
}

/// <summary>文件元数据能确认的状态，登记状态仅描述元数据。</summary>
public static class FileAvailabilityKeys
{
    /// <summary>文件元数据仍登记为就绪。</summary>
    public const string Registered = "registered";

    /// <summary>现存记录已明确释放读资格。</summary>
    public const string Released = "released";
}
