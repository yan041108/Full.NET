using System.Text.RegularExpressions;

namespace Full.NET.Data.CodeGeneration.Naming;

/// <summary>
/// 表示已经通过 Full.NET 所有权、分段和长度校验的物理表名。
/// </summary>
public sealed record SchemaName
{
    private static readonly NamingProfile Profile = NamingProfile.LoadDefault();
    private static readonly Regex ProjectOwnerPattern = CreateRegex(
        Profile.Database.ProjectOwnerPattern);
    private static readonly Regex ModulePattern = CreateRegex(
        Profile.Database.ModulePattern);
    private static readonly Regex EntityPattern = CreateRegex(
        Profile.Database.EntityPattern);

    private SchemaName(string value)
    {
        Value = value;
    }

    /// <summary>
    /// 获取规范物理表名。
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// 创建 Full.NET 官方模块使用的 <c>fn</c> 所有权表名。
    /// </summary>
    /// <param name="moduleKey">规范的小写模块键。</param>
    /// <param name="entityKey">规范的小写实体键。</param>
    /// <returns>通过校验且不会被静默截断的表名。</returns>
    public static SchemaName CreateFramework(string moduleKey, string entityKey) =>
        CreateCore(Profile.Database.FrameworkOwnerKey, moduleKey, entityKey);

    /// <summary>
    /// 创建具体项目使用的冻结 OwnerKey 表名。
    /// </summary>
    /// <param name="ownerKey">脚手架阶段冻结且不占用保留键的项目 OwnerKey。</param>
    /// <param name="moduleKey">规范的小写模块键。</param>
    /// <param name="entityKey">规范的小写实体键。</param>
    /// <returns>通过校验且不会被静默截断的表名。</returns>
    public static SchemaName CreateProject(
        string ownerKey,
        string moduleKey,
        string entityKey)
    {
        EnsurePattern(ProjectOwnerPattern, ownerKey, nameof(ownerKey));
        if (Profile.Database.ReservedOwnerKeys.Contains(
            ownerKey,
            StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "项目 OwnerKey 不能使用 Full.NET 或数据库保留键。",
                nameof(ownerKey));
        }

        return CreateCore(ownerKey, moduleKey, entityKey);
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    private static SchemaName CreateCore(
        string ownerKey,
        string moduleKey,
        string entityKey)
    {
        EnsurePattern(ModulePattern, moduleKey, nameof(moduleKey));
        EnsurePattern(EntityPattern, entityKey, nameof(entityKey));
        var value = $"{ownerKey}_{moduleKey}_{entityKey}";
        if (value.Length > Profile.Database.MaxIdentifierLength)
        {
            throw new ArgumentException(
                $"表名不能超过 {Profile.Database.MaxIdentifierLength} 个 ASCII 字符。",
                nameof(entityKey));
        }

        return new SchemaName(value);
    }

    private static void EnsurePattern(Regex pattern, string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!pattern.IsMatch(value))
        {
            throw new ArgumentException("名称不符合 Naming Profile。", parameterName);
        }
    }

    private static Regex CreateRegex(string pattern) =>
        new(pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
}
