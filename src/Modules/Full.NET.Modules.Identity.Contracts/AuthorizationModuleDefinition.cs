namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 授权目录中的稳定模块节点；仅用于角色授权页分组，不对应可分配权限码。
/// </summary>
/// <param name="Key">稳定模块标识。</param>
/// <param name="Title">中文模块标题。</param>
/// <param name="Order">同级排序值。</param>
public sealed record AuthorizationModuleDefinition(
    string Key,
    string Title,
    int Order);