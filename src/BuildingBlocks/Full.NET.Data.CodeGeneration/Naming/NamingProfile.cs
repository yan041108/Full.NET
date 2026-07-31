using System.Text.Json;

namespace Full.NET.Data.CodeGeneration.Naming;

/// <summary>
/// 表示嵌入代码生成程序集的命名规范快照。
/// </summary>
public sealed class NamingProfile
{
    private const string ResourceName =
        "Full.NET.Data.CodeGeneration.fullnet-naming-profile.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 获取命名规范结构版本。
    /// </summary>
    public int SchemaVersion { get; init; }

    /// <summary>
    /// 获取数据库命名规则。
    /// </summary>
    public required DatabaseNamingProfile Database { get; init; }

    /// <summary>
    /// 获取稳定协议命名规则。
    /// </summary>
    public required ContractNamingProfile Contracts { get; init; }

    /// <summary>
    /// 获取 .NET 标识符命名规则。
    /// </summary>
    public required DotNetNamingProfile DotNet { get; init; }

    /// <summary>
    /// 从程序集嵌入资源读取构建时冻结的默认命名规范。
    /// </summary>
    /// <returns>经过版本和关键字段校验的命名规范。</returns>
    public static NamingProfile LoadDefault()
    {
        using var stream = typeof(NamingProfile).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("找不到嵌入的 Naming Profile。");
        var profile = JsonSerializer.Deserialize<NamingProfile>(stream, JsonOptions)
            ?? throw new InvalidDataException("Naming Profile 内容为空。");
        if (profile.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"不支持 Naming Profile 版本：{profile.SchemaVersion}。");
        }

        if (profile.Database.MaxIdentifierLength <= 0
            || profile.Database.ConstraintDigest.HexLength <= 0
            || profile.Database.ConstraintDigest.PrefixLength <= 0)
        {
            throw new InvalidDataException("Naming Profile 数据库长度规则无效。");
        }

        return profile;
    }
}

/// <summary>
/// 定义数据库对象的命名配置。
/// </summary>
public sealed class DatabaseNamingProfile
{
    /// <summary>获取 Full.NET 官方表固定使用的 OwnerKey。</summary>
    public required string FrameworkOwnerKey { get; init; }

    /// <summary>获取具体项目 OwnerKey 的校验正则。</summary>
    public required string ProjectOwnerPattern { get; init; }

    /// <summary>获取项目不得占用的数据库和框架保留 OwnerKey。</summary>
    public required IReadOnlyList<string> ReservedOwnerKeys { get; init; }

    /// <summary>获取模块键的校验正则。</summary>
    public required string ModulePattern { get; init; }

    /// <summary>获取实体键的校验正则。</summary>
    public required string EntityPattern { get; init; }

    /// <summary>获取数据库列名的校验正则。</summary>
    public required string ColumnPattern { get; init; }

    /// <summary>获取 SQL Server/MySQL 共同采用的标识符长度上限。</summary>
    public int MaxIdentifierLength { get; init; }

    /// <summary>获取索引和约束长名称的摘要配置。</summary>
    public required ConstraintDigestProfile ConstraintDigest { get; init; }
}

/// <summary>
/// 定义数据库对象长名称的确定性摘要配置。
/// </summary>
public sealed class ConstraintDigestProfile
{
    /// <summary>获取摘要算法名称。</summary>
    public required string Algorithm { get; init; }

    /// <summary>获取压缩名称保留的原名称前缀长度。</summary>
    public int PrefixLength { get; init; }

    /// <summary>获取摘要使用的小写十六进制字符数。</summary>
    public int HexLength { get; init; }

    /// <summary>获取名称前缀和摘要之间的分隔符。</summary>
    public required string Separator { get; init; }
}

/// <summary>
/// 定义公共协议和稳定机器码的命名配置。
/// </summary>
public sealed class ContractNamingProfile
{
    /// <summary>获取 HTTP 路径分段规则。</summary>
    public required string HttpPathSegmentPattern { get; init; }

    /// <summary>获取 JSON 属性大小写约定。</summary>
    public required string JsonCase { get; init; }

    /// <summary>获取权限码规则。</summary>
    public required PatternProfile Permission { get; init; }

    /// <summary>获取错误码规则。</summary>
    public required PatternProfile Error { get; init; }

    /// <summary>获取消息类型规则。</summary>
    public required PatternProfile Message { get; init; }

    /// <summary>获取 SQL Statement ID 规则。</summary>
    public required PatternProfile Statement { get; init; }
}

/// <summary>
/// 定义代码生成器使用的 .NET 标识符规则。
/// </summary>
public sealed class DotNetNamingProfile
{
    /// <summary>获取 .NET 类型名规则。</summary>
    public required string TypePattern { get; init; }
}

/// <summary>
/// 封装 Naming Profile 中的单项正则规则。
/// </summary>
public sealed class PatternProfile
{
    /// <summary>获取与跨工具 Naming Profile 一致的正则表达式。</summary>
    public required string Pattern { get; init; }
}
