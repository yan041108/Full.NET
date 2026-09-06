namespace Full.NET.Modules.Identity.Contracts;

/// <summary>为其他模块提供 Host 活动用户计数的只读端口。</summary>
public interface IHostActiveUserCountReader
{
    /// <summary>统计当前启用 Host 用户总数。</summary>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>活动 Host 用户计数。</returns>
    Task<long> CountActiveHostUsersAsync(
        CancellationToken cancellationToken = default);
}
