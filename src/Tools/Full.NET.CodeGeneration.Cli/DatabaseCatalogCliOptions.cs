using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 保存基础表目录命令所需的数据库方言与连接环境变量名。
/// </summary>
internal sealed record DatabaseCatalogCliOptions(
    DatabaseMetadataProvider Provider,
    string ConnectionEnvironmentVariable);
