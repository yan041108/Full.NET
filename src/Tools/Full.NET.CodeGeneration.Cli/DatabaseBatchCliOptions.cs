using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 保存数据库批量命令的基础设施参数；逐表业务语义由独立映射文件承载。
/// </summary>
internal sealed record DatabaseBatchCliOptions(
    DatabaseMetadataProvider Provider,
    string ConnectionEnvironmentVariable,
    string MappingPath);
