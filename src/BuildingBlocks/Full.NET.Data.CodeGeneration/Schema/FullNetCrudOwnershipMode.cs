namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 定义实体在租户边界之外的显式业务所有权。
/// </summary>
public enum FullNetCrudOwnershipMode
{
    /// <summary>实体没有通用组织所有权字段。</summary>
    None = 0,

    /// <summary>实体由 OrganizationUnitId 指向的组织单元拥有。</summary>
    OrganizationUnit = 1,
}
