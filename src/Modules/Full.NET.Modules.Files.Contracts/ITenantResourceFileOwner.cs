namespace Full.NET.Modules.Files.Contracts;

/// <summary>资源所属模块确认当前可信租户的持久化引用；Files 不读取其他模块表。</summary>
/// <remarks>用于兼容读取与清理对账；实现必须查询真实资源或受控上传意图，不信任调用方给出的文件声明。上传意图的过期清理必须与最终绑定通过持久化状态 CAS 仲裁。</remarks>
public interface ITenantResourceFileOwner
{
    /// <summary>模块代码拥有的稳定键。</summary>
    string OwnerModuleKey { get; }

    /// <summary>核对当前租户下的资源是否仍持久化引用精确文件。</summary>
    /// <param name="resourceId">资源标识。</param>
    /// <param name="fileId">待核对文件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 若当前可信租户下由 <paramref name="resourceId"/> 标识的资源仍持久化引用 <paramref name="fileId"/>
    /// 对应文件则为 <see langword="true"/>；否则为 <see langword="false"/>。实现必须查询真实资源表，
    /// 不得直接信任调用方传入的声明。
    /// </returns>
    Task<bool> IsReferencedAsync(Guid resourceId, Guid fileId, CancellationToken cancellationToken = default);
}
