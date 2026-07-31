namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 定义生成器能够显式校验的实体交互场景。
/// </summary>
public enum FullNetCrudScene
{
    /// <summary>独立实体的列表、详情与生命周期操作。</summary>
    Single = 0,

    /// <summary>通过可空 ParentId 表达自引用层级的实体。</summary>
    Tree = 1,

    /// <summary>由一条显式关系连接的主实体与明细实体。</summary>
    MasterDetail = 2,

    /// <summary>由两条显式关系连接的多对多关联实体。</summary>
    ManyToMany = 3,
}
