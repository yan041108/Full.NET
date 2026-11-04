using System.Security.Claims;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>统一解释已认证主体的当前作用域权限；不替代会话、租户与权威撤权校验。</summary>
public interface IIdentityPermissionEvaluator
{
    /// <summary>按身份模块权限目录和已签名作用域判断精确权限，未知或跨作用域权限拒绝。</summary>
    /// <param name="principal">由认证中间件或已验证任务快照产生的主体。</param>
    /// <param name="permissionCode">权限目录中的稳定精确机器码。</param>
    /// <returns>当前作用域允许该精确权限时为真。</returns>
    bool HasPermission(ClaimsPrincipal principal, string permissionCode);

    /// <summary>解析主体权限候选快照；使用前仍须逐项 HasPermission 收口当前操作作用域。</summary>
    /// <param name="principal">已认证主体；不能从普通请求字段构造超级管理员声明。</param>
    /// <returns>由身份权限目录约束的权限机器码候选集合。</returns>
    IReadOnlyList<string> ResolvePermissions(ClaimsPrincipal principal);
}
