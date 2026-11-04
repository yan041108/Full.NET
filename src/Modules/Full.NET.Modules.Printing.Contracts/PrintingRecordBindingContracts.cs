using System.Security.Claims;
using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Printing.Contracts;

/// <summary>业务模块显式贡献固定表单目录；只含静态元数据，不依赖请求或数据库。</summary>
public interface IPrintingFormSchemaContributor
{
    /// <summary>获取模块拥有的固定业务表单；键与字段发布后保持稳定。</summary>
    IReadOnlyList<PrintingFormSchemaDefinition> Schemas { get; }
}

/// <summary>业务模块提供当前租户的记录绑定；必须自行校验业务权限、会话及数据范围。</summary>
public interface IPrintingRecordBindingSource
{
    /// <summary>获取对应的已注册固定表单键。</summary>
    string FormSchemaKey { get; }

    /// <summary>解析当前可信租户中可读取的记录；不得接受客户端租户、SQL 或字段覆盖。</summary>
    /// <param name="recordId">客户端选择的业务记录标识；不是授权依据。</param>
    /// <param name="principal">认证边界已验证的当前主体，不能从请求字段重建。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时返回固定字段值；无权读取、记录缺失或取消时不得交付内容。</returns>
    Task<Result<IReadOnlyDictionary<string, string?>>> ResolveAsync(
        Guid recordId, ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}
