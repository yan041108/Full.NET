namespace Full.NET.Abstractions.Tenancy;

/// <summary>
/// 定义可信基础设施建立、切换和清理当前租户上下文的受限写能力。
/// </summary>
/// <remarks>
/// 该能力只允许由请求租户解析、后台任务、迁移器和经审查的跨租户编排边界使用。
/// 普通业务处理器只应依赖 <see cref="ICurrentTenant"/>，不得根据请求输入直接切换上下文。
/// </remarks>
public interface ICurrentTenantContextWriter : ICurrentTenant
{
    /// <summary>把当前作用域绑定到已经过可信解析和授权的租户。</summary>
    /// <param name="tenant">已验证的租户上下文。</param>
    void SetTenant(TenantContext tenant);

    /// <summary>把当前作用域绑定到已授权的 Host 上下文。</summary>
    void SetHost();

    /// <summary>清除当前作用域的租户或 Host 状态。</summary>
    void Clear();
}
