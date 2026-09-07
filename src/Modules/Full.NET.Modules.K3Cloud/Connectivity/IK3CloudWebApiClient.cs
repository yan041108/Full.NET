using Full.NET.Modules.K3Cloud.Persistence;

namespace Full.NET.Modules.K3Cloud.Connectivity;

/// <summary>金蝶 K3Cloud WebAPI 的受控远程边界，禁止在数据库事务内调用。</summary>
internal interface IK3CloudWebApiClient
{
    /// <summary>执行 ValidateUser 连通性探测。</summary>
    /// <param name="config">连接配置。</param>
    /// <param name="password">已解保护的明文密码，仅用于本次调用。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>登录探测结果。</returns>
    Task<(bool Succeeded, string ResponseBody, string Message)> ValidateUserAsync(
        K3CloudConnectionConfigRecord config,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>登录后执行 Save 与 Submit；调用方必须先提交本地同步意图。</summary>
    /// <param name="config">连接配置。</param>
    /// <param name="password">已解保护的明文密码，仅用于本次调用。</param>
    /// <param name="formId">金蝶表单标识。</param>
    /// <param name="payloadJson">已持久化的单据载荷。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>远程单据标识或失败摘要。</returns>
    Task<(bool Succeeded, string? BillId, string? BillNo, string Message)> SaveAndSubmitAsync(
        K3CloudConnectionConfigRecord config,
        string password,
        string formId,
        string payloadJson,
        CancellationToken cancellationToken = default);
}
