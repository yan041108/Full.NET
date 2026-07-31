using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 保存数据库导入命令中无法从表结构安全推断的显式契约。
/// </summary>
internal sealed record DatabaseImportCliOptions(
    DatabaseMetadataProvider Provider,
    string ConnectionEnvironmentVariable,
    string OwnerKey,
    string ModuleKey,
    string EntityKey,
    string RootNamespace,
    string ClrTypeName,
    string ApiResourceName,
    string PermissionResourceName,
    FullNetCrudDataScope DataScope,
    bool HasVersion)
{
    public DatabaseCrudImportOptions ToImportOptions() =>
        new(
            OwnerKey,
            ModuleKey,
            EntityKey,
            RootNamespace,
            ClrTypeName,
            ApiResourceName,
            PermissionResourceName,
            DataScope,
            HasVersion);
}
