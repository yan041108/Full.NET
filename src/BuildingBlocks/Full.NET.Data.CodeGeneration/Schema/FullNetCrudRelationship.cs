namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 保存关系两端已经确认的语义键、列名和数据作用域。
/// </summary>
/// <param name="PrincipalEntityKey">主端实体的稳定 lower_snake 键。</param>
/// <param name="PrincipalColumnName">主端 PascalCase 关系列名。</param>
/// <param name="PrincipalDataScope">主端数据访问作用域。</param>
/// <param name="DependentEntityKey">从端实体的稳定 lower_snake 键。</param>
/// <param name="DependentColumnName">从端 PascalCase 外键列名。</param>
/// <param name="DependentDataScope">从端数据访问作用域。</param>
/// <param name="CompositeKeyColumnNames">显式复合键列；关系场景缺省时拒绝生成可执行产物。</param>
/// <param name="CascadeDelete">显式级联删除语义；关系场景缺省时拒绝生成可执行产物。</param>
/// <param name="PrincipalSchema">主端为其他实体时冻结其完整单实体定义，禁止根据键猜测表和约束。</param>
/// <param name="DependentSchema">从端为其他实体时冻结其完整单实体定义，决定级联生命周期。</param>
public sealed record FullNetCrudRelationship(
    string PrincipalEntityKey,
    string PrincipalColumnName,
    FullNetCrudDataScope PrincipalDataScope,
    string DependentEntityKey,
    string DependentColumnName,
    FullNetCrudDataScope DependentDataScope,
    IReadOnlyList<string>? CompositeKeyColumnNames = null,
    bool? CascadeDelete = null,
    FullNetCrudSchema? PrincipalSchema = null,
    FullNetCrudSchema? DependentSchema = null);
