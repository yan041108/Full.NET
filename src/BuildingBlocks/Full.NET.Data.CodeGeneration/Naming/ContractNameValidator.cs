using System.Text.RegularExpressions;

namespace Full.NET.Data.CodeGeneration.Naming;

/// <summary>
/// 使用嵌入的 Naming Profile 校验列名和稳定公共协议名称。
/// </summary>
public static class ContractNameValidator
{
    private static readonly NamingProfile Profile = NamingProfile.LoadDefault();
    private static readonly Regex ColumnPattern = CreateRegex(Profile.Database.ColumnPattern);
    private static readonly Regex PermissionPattern = CreateRegex(Profile.Contracts.Permission.Pattern);
    private static readonly Regex ErrorPattern = CreateRegex(Profile.Contracts.Error.Pattern);
    private static readonly Regex MessagePattern = CreateRegex(Profile.Contracts.Message.Pattern);
    private static readonly Regex StatementPattern = CreateRegex(Profile.Contracts.Statement.Pattern);
    private static readonly Regex DotNetTypePattern = CreateRegex(Profile.DotNet.TypePattern);
    private static readonly Regex HttpPathSegmentPattern = CreateRegex(
        Profile.Contracts.HttpPathSegmentPattern);
    private static readonly Regex JsonPropertyPattern = CreateRegex(
        "^[a-z][A-Za-z0-9]*$");

    /// <summary>判断数据库列名是否符合 PascalCase 规范。</summary>
    /// <param name="value">待校验的数据库列名。</param>
    /// <returns>符合规范时为 <see langword="true"/>。</returns>
    public static bool IsValidColumn(string value) => IsMatch(ColumnPattern, value);

    /// <summary>判断权限码是否符合三段 lower_snake 规范。</summary>
    /// <param name="value">待校验的稳定权限码。</param>
    /// <returns>符合规范时为 <see langword="true"/>。</returns>
    public static bool IsValidPermission(string value) => IsMatch(PermissionPattern, value);

    /// <summary>判断错误码是否包含模块、区域和原因三个以上规范分段。</summary>
    /// <param name="value">待校验的稳定错误码。</param>
    /// <returns>符合规范时为 <see langword="true"/>。</returns>
    public static bool IsValidError(string value) => IsMatch(ErrorPattern, value);

    /// <summary>判断消息类型是否符合四段规范且不携带 SchemaVersion。</summary>
    /// <param name="value">待校验的稳定消息类型。</param>
    /// <returns>符合规范时为 <see langword="true"/>。</returns>
    public static bool IsValidMessage(string value) => IsMatch(MessagePattern, value);

    /// <summary>判断 SQL Statement ID 是否符合点分层 lower_snake 规范。</summary>
    /// <param name="value">待校验的稳定 Statement ID。</param>
    /// <returns>符合规范时为 <see langword="true"/>。</returns>
    public static bool IsValidStatement(string value) => IsMatch(StatementPattern, value);

    /// <summary>判断 .NET 类型或命名空间分段是否符合共享 PascalCase 规则。</summary>
    /// <param name="value">待校验的 .NET 类型或命名空间分段。</param>
    /// <returns>符合规范时为 <see langword="true"/>。</returns>
    public static bool IsValidDotNetType(string value) => IsMatch(DotNetTypePattern, value);

    /// <summary>判断 HTTP 路径分段是否符合共享 kebab-case 规则。</summary>
    /// <param name="value">待校验的 HTTP 路径分段。</param>
    /// <returns>符合规范时为 <see langword="true"/>。</returns>
    public static bool IsValidHttpPathSegment(string value) =>
        IsMatch(HttpPathSegmentPattern, value);

    /// <summary>判断 JSON 属性名是否符合共享 camelCase 规则。</summary>
    /// <param name="value">待校验的 JSON 属性名。</param>
    /// <returns>符合规范时为 <see langword="true"/>。</returns>
    public static bool IsValidJsonProperty(string value) =>
        string.Equals(Profile.Contracts.JsonCase, "camel", StringComparison.Ordinal)
        && IsMatch(JsonPropertyPattern, value);

    private static bool IsMatch(Regex pattern, string value) =>
        !string.IsNullOrWhiteSpace(value) && pattern.IsMatch(value);

    private static Regex CreateRegex(string pattern) =>
        new(pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
}
