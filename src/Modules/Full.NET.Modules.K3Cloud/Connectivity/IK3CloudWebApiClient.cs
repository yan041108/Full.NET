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

    /// <summary>仅保存单据；返回的标识必须先持久化，才能提交。</summary>
    /// <param name="config">连接配置。</param>
    /// <param name="password">本次调用的解密密码。</param>
    /// <param name="formId">表单标识。</param>
    /// <param name="payloadJson">原始单据载荷。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<(bool Succeeded, string? BillId, string? BillNo, string Message)> SaveDocumentAsync(
        K3CloudConnectionConfigRecord config, string password, string formId, string payloadJson,
        CancellationToken cancellationToken = default);

    /// <summary>只提交已保存的单据，禁止重新执行 Save。</summary>
    /// <param name="config">连接配置。</param>
    /// <param name="password">本次调用的解密密码。</param>
    /// <param name="formId">表单标识。</param>
    /// <param name="billId">持久化的外部单据标识。</param>
    /// <param name="billNo">持久化的外部单据编号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<(bool Succeeded, string Message)> SubmitDocumentAsync(
        K3CloudConnectionConfigRecord config, string password, string formId, string? billId, string? billNo,
        CancellationToken cancellationToken = default);
}
