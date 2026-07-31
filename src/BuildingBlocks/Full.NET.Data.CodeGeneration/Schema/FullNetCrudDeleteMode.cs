namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 定义实体的删除与不可变生命周期策略。
/// </summary>
public enum FullNetCrudDeleteMode
{
    /// <summary>实体通过物理删除结束生命周期。</summary>
    HardDelete = 0,

    /// <summary>实体通过 IsDeleted 和可选删除审计字段结束生命周期。</summary>
    SoftDelete = 1,

    /// <summary>实体创建后不可更新或删除，用于追加型事实记录。</summary>
    Immutable = 2,
}
